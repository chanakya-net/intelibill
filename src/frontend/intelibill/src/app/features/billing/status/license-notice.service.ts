import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { EMPTY, Observable, catchError, expand, reduce, throwError } from 'rxjs';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import type {
  AccountLicenseNoticeItem,
  AccountLicenseNotices,
  LicenseNoticeFailure,
  ShopLicenseNoticeItem,
  ShopLicenseNotices,
} from './license-notice.models';

export class LicenseNoticeReadError extends Error {
  constructor(readonly failure: LicenseNoticeFailure) {
    super(failure);
  }
}

/**
 * Fetches due operational shop notices and account-scoped commercial notices through the merchant API.
 * All requests run with browser credentials under live shop/account authorization and never cache in the worker.
 */
@Injectable({ providedIn: 'root' })
export class LicenseNoticeService {
  private readonly http = inject(HttpClient);

  shop(shopId: string): Observable<ShopLicenseNotices> {
    const page = (after: string | null) =>
      this.http.get<ShopLicenseNotices>(
        `${API_BASE_URL}/licensing/shops/${encodeURIComponent(shopId)}/notices`,
        { params: after ? { after } : {} },
      );

    return page(null).pipe(
      expand((result) => (result.nextCursor ? page(result.nextCursor) : EMPTY)),
      reduce<ShopLicenseNotices, ShopLicenseNotices>(
        (acc, result) => ({
          schemaVersion: 1,
          shopId: result.shopId,
          evaluatedAt: acc.evaluatedAt || result.evaluatedAt,
          notices: [...acc.notices, ...result.notices],
          nextDueAt: acc.nextDueAt ?? result.nextDueAt,
          nextCursor: null,
        }),
        {
          schemaVersion: 1,
          shopId,
          evaluatedAt: '',
          notices: [] as ShopLicenseNoticeItem[],
          nextDueAt: null,
          nextCursor: null,
        },
      ),
      catchError(classifyNoticeError),
    );
  }

  account(accountId: string): Observable<AccountLicenseNotices> {
    const page = (after: string | null) =>
      this.http.get<AccountLicenseNotices>(
        `${API_BASE_URL}/billing/accounts/${encodeURIComponent(accountId)}/notices`,
        { params: after ? { after } : {} },
      );

    return page(null).pipe(
      expand((result) => (result.nextCursor ? page(result.nextCursor) : EMPTY)),
      reduce<AccountLicenseNotices, AccountLicenseNotices>(
        (acc, result) => ({
          schemaVersion: 1,
          accountId: result.accountId,
          evaluatedAt: acc.evaluatedAt || result.evaluatedAt,
          notices: [...acc.notices, ...result.notices],
          nextDueAt: acc.nextDueAt ?? result.nextDueAt,
          nextCursor: null,
        }),
        {
          schemaVersion: 1,
          accountId,
          evaluatedAt: '',
          notices: [] as AccountLicenseNoticeItem[],
          nextDueAt: null,
          nextCursor: null,
        },
      ),
      catchError(classifyNoticeError),
    );
  }
}

function classifyNoticeError(error: unknown): Observable<never> {
  return throwError(() => new LicenseNoticeReadError(toFailure(error)));
}

function toFailure(error: unknown): LicenseNoticeFailure {
  const status = error instanceof HttpErrorResponse ? error.status : 0;
  const code = error instanceof HttpErrorResponse ? (error.error as { code?: string } | null)?.code : undefined;
  if (status === 401) return 'session';
  if (status === 403 && code === 'Licensing.ShopScopeRequired') return 'scope';
  if (status === 403) return 'denied';
  return 'unavailable';
}
