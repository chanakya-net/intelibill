import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, defer, map, of, tap, throwError } from 'rxjs';

import { FINANCIAL_OPERATION_ENDPOINTS } from '../auth/auth.constants';

/** Route slugs shared with `api/financial-operations/{type}/{key}`. */
export type FinancialOperationType = 'customer-payment' | 'supplier-payment';

export interface FinancialOperationScope {
  readonly userId: string;
  readonly shopId: string;
}

/**
 * `prepared`: stored, not yet answered. `unknown`: sent, but the answer was lost — it may have
 * committed. Neither is ever rebuilt from a form; only the frozen payload is resent.
 */
export type FinancialOperationIntentStatus = 'prepared' | 'unknown';

export interface FinancialOperationIntent<TPayload = unknown> {
  readonly schemaVersion: 1;
  readonly operationKey: string;
  readonly operationType: FinancialOperationType;
  readonly targetId: string;
  readonly userId: string;
  readonly shopId: string;
  readonly payload: TPayload;
  readonly status: FinancialOperationIntentStatus;
  readonly createdAt: string;
  readonly updatedAt: string;
}

interface FinancialOperationLookup<TResponse> {
  readonly operationKey: string;
  readonly response: TResponse;
}

/** How a failed send left the operation: definitively refused, or possibly committed. */
export type FinancialOperationSendFailure = 'rejected' | 'uncertain';

export class ForeignFinancialOperationScopeError extends Error {
  constructor() {
    super('Financial operation intent belongs to another user or shop.');
    this.name = 'ForeignFinancialOperationScopeError';
  }
}

const STORAGE_KEY = 'intelibill.financial-operation-intents.v1';

/**
 * Owns a financial operation's identity from the click until its committed outcome is known.
 *
 * `prepare` writes the frozen payload and a fresh operation key to storage before anything is
 * sent; if that write fails it throws and nothing is sent. Every retry replays that stored payload
 * under the same key, so the server's receipt scope (shop, type, key) converges duplicates to one
 * posting. A transport loss, timeout, 429 or 5xx leaves the intent `unknown`; only a matching
 * committed result (a 2xx answer or a found lookup) or a definitive refusal clears it. An intent
 * is only ever sent from the user and shop that created it.
 */
@Injectable({ providedIn: 'root' })
export class FinancialOperationIntentService {
  private readonly http = inject(HttpClient);
  private readonly revisionSignal = signal(0);

  /** Bumps on every change, so a `computed` over `pending` re-evaluates. */
  readonly revision = this.revisionSignal.asReadonly();

  prepare<TPayload>(
    scope: FinancialOperationScope,
    operationType: FinancialOperationType,
    targetId: string,
    payload: TPayload,
  ): FinancialOperationIntent<TPayload> {
    if (!scope.userId || !scope.shopId) {
      throw new Error('Financial operation scope is incomplete.');
    }

    const now = new Date().toISOString();
    const intent: FinancialOperationIntent<TPayload> = deepFreeze({
      schemaVersion: 1,
      operationKey: `${operationType}-${crypto.randomUUID()}`,
      operationType,
      targetId,
      userId: scope.userId,
      shopId: scope.shopId,
      payload: structuredClone(payload),
      status: 'prepared',
      createdAt: now,
      updatedAt: now,
    });

    this.write({ ...this.read(), [intent.operationKey]: intent });
    return intent;
  }

  /** The oldest unresolved intent for this target in this scope, if any. */
  pending<TPayload>(
    scope: FinancialOperationScope,
    operationType: FinancialOperationType,
    targetId: string,
  ): FinancialOperationIntent<TPayload> | null {
    const matches = Object.values(this.read())
      .filter(
        (intent) =>
          intent.userId === scope.userId &&
          intent.shopId === scope.shopId &&
          intent.operationType === operationType &&
          intent.targetId === targetId,
      )
      .sort((a, b) => a.createdAt.localeCompare(b.createdAt));
    return (matches[0] as FinancialOperationIntent<TPayload> | undefined) ?? null;
  }

  /**
   * Sends the stored payload with its key. Refuses, without sending, an intent from another scope.
   * Success clears the intent; a definitive refusal discards it (a corrected payment is a new
   * action); anything else marks it `unknown`. The original error is always rethrown.
   */
  send<TPayload, TResponse>(
    scope: FinancialOperationScope,
    intent: FinancialOperationIntent<TPayload>,
    sender: (payload: TPayload, operationKey: string) => Observable<TResponse>,
  ): Observable<TResponse> {
    return defer(() => {
      if (!isSameScope(scope, intent)) {
        return throwError(() => new ForeignFinancialOperationScopeError());
      }
      return sender(intent.payload, intent.operationKey).pipe(
        tap(() => this.remove(intent.operationKey)),
        catchError((error: unknown) => {
          if (classifyFinancialOperationFailure(error) === 'rejected') {
            this.remove(intent.operationKey);
          } else {
            this.markUnknown(intent.operationKey);
          }
          return throwError(() => error);
        }),
      );
    });
  }

  /**
   * Read-only reconciliation. Resolves to the committed response (and clears the intent) when the
   * server has one, or to null when nothing is visible yet — which never means it failed.
   */
  check<TResponse>(
    scope: FinancialOperationScope,
    intent: FinancialOperationIntent,
  ): Observable<TResponse | null> {
    return defer(() => {
      if (!isSameScope(scope, intent)) {
        return throwError(() => new ForeignFinancialOperationScopeError());
      }
      return this.http
        .get<FinancialOperationLookup<TResponse>>(
          FINANCIAL_OPERATION_ENDPOINTS.lookup(intent.operationType, intent.operationKey),
        )
        .pipe(
          map((lookup) => {
            this.remove(intent.operationKey);
            return lookup.response;
          }),
          catchError((error: unknown) =>
            error instanceof HttpErrorResponse && error.status === 404 ? of(null) : throwError(() => error),
          ),
        );
    });
  }

  private markUnknown(operationKey: string): void {
    const intents = this.read();
    const intent = intents[operationKey];
    if (!intent || intent.status === 'unknown') {
      return;
    }
    try {
      this.write({
        ...intents,
        [operationKey]: { ...intent, status: 'unknown', updatedAt: new Date().toISOString() },
      });
    } catch {
      // The intent is still stored as `prepared`, which is just as unresolved.
    }
  }

  private remove(operationKey: string): void {
    const intents = { ...this.read() };
    if (!(operationKey in intents)) {
      return;
    }
    delete intents[operationKey];
    try {
      this.write(intents);
    } catch {
      // Left in storage it only offers a harmless replay of a committed operation.
    }
  }

  private read(): Record<string, FinancialOperationIntent> {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      return raw ? (JSON.parse(raw) as Record<string, FinancialOperationIntent>) : {};
    } catch {
      return {};
    }
  }

  private write(intents: Record<string, FinancialOperationIntent>): void {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(intents));
    this.revisionSignal.update((revision) => revision + 1);
  }
}

export function classifyFinancialOperationFailure(error: unknown): FinancialOperationSendFailure {
  if (!(error instanceof HttpErrorResponse)) {
    return 'uncertain';
  }
  const status = error.status;
  if (status === 0 || status === 408 || status === 429 || status >= 500) {
    return 'uncertain';
  }
  return status >= 400 ? 'rejected' : 'uncertain';
}

function isSameScope(scope: FinancialOperationScope, intent: FinancialOperationIntent): boolean {
  return !!scope.userId && !!scope.shopId && intent.userId === scope.userId && intent.shopId === scope.shopId;
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
