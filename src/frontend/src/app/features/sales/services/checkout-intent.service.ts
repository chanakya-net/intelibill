import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, inject } from '@angular/core';
import { Observable, Subject, catchError, from, map, of, switchMap } from 'rxjs';

import {
  CheckoutIntent,
  CheckoutIntentIndexedDbService,
  UNRESOLVED_CHECKOUT_STATUSES,
} from '../../../core/storage/checkout-intent-indexeddb.service';
import type { RecordSaleRequest, SaleDto } from './sale.models';
import { SaleService } from './sale.service';

export interface CheckoutScope {
  readonly userId: string;
  readonly shopId: string;
}

export type CheckoutRecovery =
  | { readonly kind: 'replayed'; readonly intent: CheckoutIntent; readonly sale: SaleDto }
  | { readonly kind: 'pending'; readonly intent: CheckoutIntent }
  | { readonly kind: 'conflict'; readonly intent: CheckoutIntent };

/** How a failed send left the checkout: definitively refused, or possibly committed. */
export type CheckoutSendFailure = 'rejected' | 'uncertain';

const SEND_LEASE_MS = 60_000;
const CHANNEL_NAME = 'intelibill-checkout-intents';

/**
 * Owns a checkout's lifetime from the cashier's click until the server's answer is known.
 *
 * `prepare` stores the frozen request before anything is sent; a storage failure means nothing is
 * sent. Every send and retry replays that stored request, so the server's idempotency scope
 * (shop, actor, key) converges duplicates to one sale. A send that ends without a definitive answer
 * leaves the intent `uncertain` — never deleted, never rebuilt from the cart, never moved to
 * offline billing.
 */
@Injectable({ providedIn: 'root' })
export class CheckoutIntentService {
  private readonly storage = inject(CheckoutIntentIndexedDbService);
  private readonly saleService = inject(SaleService);
  private readonly changesSubject = new Subject<string>();
  private readonly channel = this.openChannel();

  /** Emits an intent id whenever an intent changes in this tab or another. */
  readonly changes$: Observable<string> = this.changesSubject.asObservable();

  constructor() {
    inject(DestroyRef, { optional: true })?.onDestroy(() => this.channel?.close());
  }

  async prepare(scope: CheckoutScope, request: RecordSaleRequest): Promise<CheckoutIntent> {
    if (!scope.userId || !scope.shopId) {
      throw new Error('Checkout scope is incomplete.');
    }

    const now = new Date().toISOString();
    const frozenRequest = deepFreeze(structuredClone(request));
    const intent: CheckoutIntent = Object.freeze({
      schemaVersion: 1,
      intentId: crypto.randomUUID(),
      userId: scope.userId,
      shopId: scope.shopId,
      idempotencyKey: frozenRequest.idempotencyKey,
      request: frozenRequest,
      createdAt: now,
      updatedAt: now,
      status: 'prepared',
      sendingUntil: null,
      saleId: null,
      invoiceNumber: null,
      rejectionCode: null,
    });

    await this.storage.add(intent);
    this.notify(intent.intentId);
    return intent;
  }

  /**
   * Takes ownership of an unresolved intent for one send. Returns null when another tab holds a
   * live send lease or the intent is already resolved, so two tabs never send concurrently.
   */
  async claimForSend(intentId: string): Promise<CheckoutIntent | null> {
    const now = Date.now();
    const claimed = await this.storage.update(intentId, (current) => {
      const leaseLive =
        current.status === 'sending' &&
        current.sendingUntil !== null &&
        Date.parse(current.sendingUntil) > now;
      if (!UNRESOLVED_CHECKOUT_STATUSES.includes(current.status) || leaseLive) {
        return null;
      }
      return {
        ...current,
        status: 'sending',
        sendingUntil: new Date(now + SEND_LEASE_MS).toISOString(),
        updatedAt: new Date(now).toISOString(),
      };
    });
    if (claimed) this.notify(intentId);
    return claimed;
  }

  async markUncertain(intentId: string): Promise<CheckoutIntent | null> {
    return this.transition(intentId, (current) =>
      UNRESOLVED_CHECKOUT_STATUSES.includes(current.status)
        ? { ...current, status: 'uncertain', sendingUntil: null }
        : null,
    );
  }

  async confirm(intentId: string, sale: Pick<SaleDto, 'saleId' | 'invoiceNumber'>): Promise<CheckoutIntent | null> {
    return this.transition(intentId, (current) =>
      current.status === 'rejected'
        ? null
        : {
            ...current,
            status: 'confirmed',
            sendingUntil: null,
            saleId: sale.saleId,
            invoiceNumber: sale.invoiceNumber,
          },
    );
  }

  async reject(intentId: string, rejectionCode: string): Promise<CheckoutIntent | null> {
    return this.transition(intentId, (current) =>
      current.status === 'confirmed'
        ? null
        : { ...current, status: 'rejected', sendingUntil: null, rejectionCode },
    );
  }

  async loadUnresolved(scope: CheckoutScope): Promise<readonly CheckoutIntent[]> {
    if (!scope.userId || !scope.shopId) {
      return [];
    }
    const intents = await this.storage.listByScope(scope.userId, scope.shopId);
    return intents
      .filter((intent) => UNRESOLVED_CHECKOUT_STATUSES.includes(intent.status))
      .sort((left, right) => left.createdAt.localeCompare(right.createdAt));
  }

  get(intentId: string): Promise<CheckoutIntent | null> {
    return this.storage.get(intentId);
  }

  /** Sends the stored request exactly as it was prepared. */
  send(intent: CheckoutIntent): Observable<SaleDto> {
    return this.saleService.recordSale(intent.request);
  }

  /**
   * Read-only reconciliation: asks the server whether this intent's key already committed a sale
   * in the intent's own scope. A miss leaves the intent pending; only re-sending the identical
   * request settles it.
   */
  recover(scope: CheckoutScope, intentId: string): Observable<CheckoutRecovery> {
    return from(this.storage.get(intentId)).pipe(
      switchMap((intent) => {
        if (!intent || intent.userId !== scope.userId || intent.shopId !== scope.shopId) {
          throw new Error('Checkout intent is not in the current scope.');
        }
        if (intent.status === 'rejected') {
          return of<CheckoutRecovery>({ kind: 'conflict', intent });
        }
        return this.saleService.getSaleByIdempotencyKey(intent.idempotencyKey).pipe(
          switchMap((sale) =>
            from(this.confirm(intent.intentId, sale)).pipe(
              map((confirmed): CheckoutRecovery => ({ kind: 'replayed', intent: confirmed ?? intent, sale })),
            ),
          ),
          catchError(() => of<CheckoutRecovery>({ kind: 'pending', intent })),
        );
      }),
    );
  }

  private async transition(
    intentId: string,
    update: (current: CheckoutIntent) => CheckoutIntent | null,
  ): Promise<CheckoutIntent | null> {
    const written = await this.storage.update(intentId, (current) => {
      const next = update(current);
      return next ? { ...next, updatedAt: new Date().toISOString() } : null;
    });
    if (written) this.notify(intentId);
    return written;
  }

  private notify(intentId: string): void {
    this.changesSubject.next(intentId);
    this.channel?.postMessage(intentId);
  }

  private openChannel(): BroadcastChannel | null {
    if (typeof BroadcastChannel === 'undefined') {
      return null;
    }
    const channel = new BroadcastChannel(CHANNEL_NAME);
    channel.onmessage = (event: MessageEvent<unknown>) => {
      if (typeof event.data === 'string') {
        this.changesSubject.next(event.data);
      }
    };
    return channel;
  }
}

/**
 * Classifies a failed POST. Only a definitive client-side refusal (validation, conflict,
 * authorization) proves the sale was not committed; transport loss, timeouts, throttling and
 * server faults may have committed it and must be resolved by replay.
 */
export function classifyCheckoutSendFailure(error: unknown): CheckoutSendFailure {
  if (!(error instanceof HttpErrorResponse)) {
    return 'uncertain';
  }
  const status = error.status;
  if (status === 0 || status === 408 || status === 429 || status >= 500) {
    return 'uncertain';
  }
  return status >= 400 ? 'rejected' : 'uncertain';
}

function deepFreeze<T>(value: T): T {
  if (value && typeof value === 'object' && !Object.isFrozen(value)) {
    Object.freeze(value);
    for (const nested of Object.values(value as Record<string, unknown>)) {
      deepFreeze(nested);
    }
  }
  return value;
}
