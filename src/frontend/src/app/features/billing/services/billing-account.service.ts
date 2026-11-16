import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { EMPTY, Observable, expand, map, reduce } from 'rxjs';

import { API_BASE_URL } from '../../../core/auth/auth.constants';
import {
  BillingAccount,
  CreateBillingAccountRequest,
  CreatedBillingAccount,
} from './billing-account.models';

interface BillingAccountPage {
  readonly accounts: readonly BillingAccount[];
  readonly nextCursor?: string | null;
}

export const BILLING_ACCOUNT_ENDPOINTS = {
  accounts: `${API_BASE_URL}/billing/accounts`,
} as const;

/**
 * Commercial billing accounts of the signed-in person. They are separate from shops and customers:
 * the server never adds an account because of a shop, email or GSTIN, only on explicit creation.
 */
@Injectable({ providedIn: 'root' })
export class BillingAccountService {
  private readonly http = inject(HttpClient);

  list(): Observable<readonly BillingAccount[]> {
    // Emit only a complete traversal: a failed later page must not replace the current list.
    return this.page().pipe(
      expand((page) => page.nextCursor ? this.page(page.nextCursor) : EMPTY, 1),
      reduce((accounts, page) => [...accounts, ...page.accounts], [] as readonly BillingAccount[]),
    );
  }

  private page(after?: string): Observable<BillingAccountPage> {
    return this.http.get<BillingAccountPage>(BILLING_ACCOUNT_ENDPOINTS.accounts,
      { params: after ? { after } : {} });
  }

  create(request: CreateBillingAccountRequest): Observable<CreatedBillingAccount> {
    return this.http
      .post<BillingAccount>(
        BILLING_ACCOUNT_ENDPOINTS.accounts,
        { operationKey: request.operationKey, name: request.name },
        { observe: 'response' },
      )
      .pipe(map((response) => ({ account: response.body as BillingAccount, created: response.status === 201 })));
  }
}
