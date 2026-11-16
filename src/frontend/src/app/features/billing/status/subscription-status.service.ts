import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { EMPTY, Observable, catchError, expand, reduce, throwError } from 'rxjs';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import type { AccountSubscriptionStatus, AccountTermStatus, ShopSubscriptionStatus, StatusFailure } from './subscription-status.models';

export class StatusReadError extends Error {
  constructor(readonly failure: StatusFailure) {
    super(failure);
  }
}

/** Fetches observe-only status; failures are classified so an outage is never shown as an expiry. */
@Injectable({ providedIn: 'root' })
export class SubscriptionStatusService {
  private readonly http = inject(HttpClient);

  shop(shopId: string): Observable<ShopSubscriptionStatus> {
    return this.http.get<ShopSubscriptionStatus>(`${API_BASE_URL}/licensing/shops/${encodeURIComponent(shopId)}/status`)
      .pipe(catchError(classify));
  }

  /** Every page of the account's terms, in order: a list is never silently truncated. */
  account(accountId: string): Observable<{ readonly evaluatedAt: string; readonly items: readonly AccountTermStatus[] }> {
    const page = (after: string | null) => this.http.get<AccountSubscriptionStatus>(
      `${API_BASE_URL}/billing/accounts/${encodeURIComponent(accountId)}/subscriptions`, { params: after ? { after } : {} });
    return page(null).pipe(
      expand(result => result.nextCursor ? page(result.nextCursor) : EMPTY),
      reduce((all, result) => ({ evaluatedAt: all.evaluatedAt || result.evaluatedAt, items: [...all.items, ...result.items] }),
        { evaluatedAt: '', items: [] as AccountTermStatus[] }),
      catchError(classify),
    );
  }
}

function classify(error: unknown): Observable<never> {
  const status = error instanceof HttpErrorResponse ? error.status : 0;
  const code = error instanceof HttpErrorResponse ? (error.error as { code?: string } | null)?.code : undefined;
  const failure: StatusFailure = status === 401 ? 'session'
    : status === 403 && code === 'Licensing.ShopScopeRequired' ? 'scope'
    : status === 403 ? 'denied' : 'unavailable';
  return throwError(() => new StatusReadError(failure));
}
