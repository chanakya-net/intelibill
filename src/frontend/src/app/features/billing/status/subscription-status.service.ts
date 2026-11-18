import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { EMPTY, Observable, catchError, expand, reduce, throwError } from 'rxjs';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import type { AccountSubscriptionStatus, AccountTermStatus, ShopSubscriptionStatus, ShopTradingAuthority, StatusFailure, TradingAuthorityFailure } from './subscription-status.models';

export class StatusReadError extends Error {
  constructor(readonly failure: StatusFailure) {
    super(failure);
  }
}

export class TradingAuthorityReadError extends Error {
  constructor(readonly failure: TradingAuthorityFailure) {
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

  tradingAuthority(shopId: string): Observable<ShopTradingAuthority> {
    return this.http.get<ShopTradingAuthority>(`${API_BASE_URL}/licensing/shops/${encodeURIComponent(shopId)}/trading-authority`)
      .pipe(catchError(error => throwError(() => new TradingAuthorityReadError(authorityFailure(error)))));
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
  return throwError(() => new StatusReadError(statusFailure(error)));
}

function statusFailure(error: unknown): StatusFailure {
  const status = error instanceof HttpErrorResponse ? error.status : 0;
  const code = error instanceof HttpErrorResponse ? (error.error as { code?: string } | null)?.code : undefined;
  return status === 401 ? 'session'
    : status === 403 && code === 'Licensing.ShopScopeRequired' ? 'scope'
    : status === 403 ? 'denied' : 'unavailable';
}

function authorityFailure(error: unknown): TradingAuthorityFailure {
  if (error instanceof HttpErrorResponse) {
    const code = (error.error as { code?: string } | null)?.code;
    if (error.status === 403) {
      if (code === 'Licence.Expired') return 'expired';
      if (code === 'Licence.TradingRestricted' || code === 'Licence.SecuritySuspended') return 'restricted';
      if (code === 'Licence.PendingActivation') return 'pending';
    }
    if (error.status === 409 && code === 'Licence.Busy') return 'busy';
  }
  return statusFailure(error);
}
