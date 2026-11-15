import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { API_BASE_URL } from '../../../core/auth/auth.constants';
import {
  BillingAccount,
  CreateBillingAccountRequest,
  CreatedBillingAccount,
} from './billing-account.models';

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
    return this.http
      .get<{ accounts: readonly BillingAccount[] }>(BILLING_ACCOUNT_ENDPOINTS.accounts)
      .pipe(map((response) => response.accounts));
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
