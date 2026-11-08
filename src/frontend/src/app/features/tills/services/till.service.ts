import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, defer, firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { scopedRequestContext, isScopeChangeError } from '../../../core/context/scoped-request';
import { ScopeOwner } from '../../../core/context/shop-scope';
import { FinancialOperationIntentService } from '../../../core/services/financial-operation-intent.service';
import { TillDraftService } from './till-draft.service';
import {
  Till,
  TillCapabilities,
  TillSession,
  TillSessionDetail,
  TillPage,
  TillActionPayload,
} from './till.models';
@Injectable({ providedIn: 'root' })
export class TillService {
  private readonly http = inject(HttpClient);
  private readonly drafts = inject(TillDraftService);
  private readonly intents = inject(FinancialOperationIntentService);
  private readonly base = `${API_BASE_URL}/tills`;
  capabilities() {
    return this.http.get<TillCapabilities>(`${this.base}/capabilities`);
  }
  members() {
    return this.http.get<{ userId: string; firstName: string; lastName: string; status: string }[]>(
      `${API_BASE_URL}/users`,
    );
  }
  tills() {
    return this.http.get<Till[]>(this.base);
  }
  sessions(page = 1, state = '') {
    return this.http.get<TillPage<TillSession>>(`${this.base}/sessions`, {
      params: new HttpParams().set('page', page).set('pageSize', 20).set('state', state),
    });
  }
  detail(id: string, page = 1) {
    return this.http.get<TillSessionDetail>(`${this.base}/sessions/${encodeURIComponent(id)}`, {
      params: { page, pageSize: 20 },
    });
  }
  /** Frozen owner/action/target/payload commits before lookup or POST. Never discard unknown work. */
  execute<T>(
    requestOwner: ScopeOwner,
    action: string,
    target: string,
    cycle: string,
    payload: Record<string, unknown>,
    path: string,
    source = false,
    recover?: (response: unknown) => Promise<T>,
    recoverFrozen = false,
  ): Observable<T> {
    const owner = { userId: requestOwner.userId, shopId: requestOwner.shopId };
    const frozenPayload = structuredClone(payload);
    return defer(async () => {
      const prefix = `${action}:${target}:`;
      const all = await this.drafts.list(owner);
      const prior = all.find((x) => x.targetId.startsWith(prefix) && x.pending);
      let identity = prior?.targetId ?? `${prefix}${cycle}`;
      const current = prior ?? (await this.drafts.load(owner, identity));
      let recovery = !!current?.pending;
      const payment = action === 'customer-payment' || action === 'supplier-payment';
      const externalKey =
        typeof frozenPayload['operationKey'] === 'string' ? frozenPayload['operationKey'] : null;
      const confirmedPayment = async () => {
        if (!payment || !externalKey) return null;
        const proof = await this.drafts.confirmation(owner, `${prefix}${cycle}`, externalKey);
        if (!proof) return null;
        // Older key-only proofs remain valid, but need an authorized receipt read for the response.
        let response: unknown;
        if (Object.prototype.hasOwnProperty.call(proof, 'response')) response = proof.response;
        else {
          try {
            response = (
              await firstValueFrom(
                this.http.get<{ response: T }>(
                  `${API_BASE_URL}/financial-operations/${action}/${externalKey}`,
                  { context: scopedRequestContext(owner) },
                ),
              )
            ).response;
          } catch (error) {
            if (isScopeChangeError(error)) throw error;
            // A missing response cannot turn a verified old proof into a refusal/new payment.
            throw new Error('Confirmed operation response is not available yet.');
          }
        }
        this.intents.confirmMatching(
          owner,
          action as 'customer-payment' | 'supplier-payment',
          target,
          externalKey,
        );
        return { response: response as T };
      };
      const confirmed = await confirmedPayment();
      if (confirmed) return confirmed.response;
      let record;
      try {
        record = recovery
          ? await this.drafts.claimRecovery(
              owner,
              identity,
              current!.pending!.operationKey,
              current!.pending!.payload,
            )
          : await this.drafts.prepare(
              owner,
              identity,
              { payload: { ...frozenPayload, __requestPath: path } },
              current?.revision ?? 0,
            );
      } catch (error) {
        // Another tab may confirm between the read and atomic claim. Its immutable proof wins.
        const confirmed = await confirmedPayment();
        if (confirmed) return confirmed.response;
        throw error;
      }
      identity = record.targetId;
      const pending = record.pending!;
      recovery ||= pending.status === 'unknown';
      if (
        recovery &&
        !recoverFrozen &&
        ((payment && externalKey && externalKey !== pending.operationKey) ||
          !sameActionPayload(frozenPayload, pending.payload))
      )
        throw new PendingTillActionError(record.targetId, pending.operationKey, pending.payload);
      const frozenPath = pending.payload['__requestPath'];
      if (typeof frozenPath !== 'string') throw new Error('Frozen request path is missing.');
      const context = scopedRequestContext(owner);
      try {
        if (recovery && action !== 'reconciliation-resolution' && action !== 'payment-reversal') {
          const lookup = source
            ? `${API_BASE_URL}/financial-operations/${action}/${pending.operationKey}`
            : `${this.base}/operations/${action}/${pending.operationKey}`;
          let saved: { response: T } | null = null;
          try {
            saved = await firstValueFrom(this.http.get<{ response: T }>(lookup, { context }));
          } catch (error) {
            if (isScopeChangeError(error)) throw error; /* Missing lookup never proves failure. */
          }
          if (saved) {
            const proof = saved.response as { saleReturnId?: string; saleId?: string };
            if (action === 'sale-return' && (!proof.saleReturnId || proof.saleId !== target))
              throw new Error('Return receipt identity mismatch.');
            if (
              action === 'sale-return-void' &&
              (proof.saleReturnId !== target ||
                !proof.saleId ||
                (pending.payload['__parentSaleId'] &&
                  proof.saleId !== pending.payload['__parentSaleId']))
            )
              throw new Error('Return void receipt identity mismatch.');
            const response = recover ? await recover(saved.response) : saved.response;
            await this.drafts.settle(owner, identity, pending.operationKey, 'confirmed', {
              response,
            });
            if (payment)
              this.intents.confirmMatching(
                owner,
                action as 'customer-payment' | 'supplier-payment',
                target,
                pending.operationKey,
              );
            return response;
          }
        }
        const { __requestPath: ignored, __parentSaleId: parent, ...body } = pending.payload;
        const response = await firstValueFrom(
          this.http.post<T>(`${API_BASE_URL}${frozenPath}`, body, { context }),
        );
        await this.drafts.settle(owner, identity, pending.operationKey, 'confirmed', { response });
        if (payment)
          this.intents.confirmMatching(
            owner,
            action as 'customer-payment' | 'supplier-payment',
            target,
            pending.operationKey,
          );
        return response;
      } catch (error) {
        await this.drafts.settleFailure(owner, identity, pending.operationKey, error, recovery);
        throw error;
      }
    });
  }
}

export class PendingTillActionError extends Error {
  constructor(
    readonly targetId: string,
    readonly operationKey: string,
    readonly frozenPayload: TillActionPayload,
  ) {
    super('tills.pendingRecovery');
  }
}
function sameActionPayload(left: unknown, right: unknown): boolean {
  const canonical = (value: unknown): unknown =>
    Array.isArray(value)
      ? value.map(canonical)
      : value && typeof value === 'object'
        ? Object.fromEntries(
            Object.entries(value)
              .filter(([key]) => !['operationKey', '__requestPath', '__parentSaleId'].includes(key))
              .sort(([a], [b]) => a.localeCompare(b))
              .map(([key, nested]) => [key, canonical(nested)]),
          )
        : value;
  return JSON.stringify(canonical(left)) === JSON.stringify(canonical(right));
}
