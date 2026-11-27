import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { problemCode } from '@intelibill/api';
import { BILLING_ACCOUNT_ENDPOINTS } from '../services/billing-account.service';
import { scopedRequestContext } from '../../../core/context/scoped-request';
import type { ScopeOwner } from '../../../core/context/shop-scope';

export interface CoverageTerm {
  readonly termId: string;
  readonly mode: 'Grouped' | 'Independent';
  readonly capacity: number;
  readonly startsAt: string;
  readonly endsAt: string;
}

export interface AffiliationRequest {
  readonly requestId: string;
  readonly accountId: string;
  readonly accountName: string;
  readonly shopId: string;
  readonly term: CoverageTerm | null;
  readonly expiresAt: string;
  readonly status: string;
  readonly shopName: string | null;
  readonly ownerName: string | null;
  readonly appliedAt: string | null;
}

export interface AffiliationList {
  readonly accountId: string;
  readonly accountName: string;
  readonly requests: readonly AffiliationRequest[];
  readonly terms: readonly CoverageTerm[];
}

export interface AffiliationConsent {
  readonly request: AffiliationRequest;
  readonly shopName: string;
  readonly ownerName: string;
  readonly status: string;
}

export type ConsentDecision = 'grant' | 'revoke' | 'accept';

export function affiliationError(error: unknown): string {
  switch (problemCode(error)) {
    case 'Billing.ShopOwnerRequired': return 'billing.affiliation.ownerRequired';
    case 'Billing.AffiliationConflict': return 'billing.affiliation.selectionUnavailable';
    case 'Billing.ConsentInvalid': return 'billing.affiliation.consentInvalid';
    case 'Billing.AffiliationInvalid': return 'billing.affiliation.invalidChoice';
    case 'Billing.AccountAccessDenied': return 'billing.denied';
    default: return 'billing.unavailable';
  }
}

export function definitiveAffiliationFailure(error: unknown): boolean {
  return error instanceof HttpErrorResponse && (error.status === 400 || error.status === 409);
}

@Injectable({ providedIn: 'root' })
export class AffiliationApi {
  private readonly http = inject(HttpClient);

  list(account: string) {
    return this.http.get<AffiliationList>(this.path(account));
  }

  request(account: string, body: unknown, owner: ScopeOwner) {
    return this.http.post<AffiliationRequest>(this.path(account), body, { context: scopedRequestContext(owner) });
  }

  readConsent(account: string, shop: string, id: string) {
    return this.http.get<AffiliationConsent>(this.consentPath(account, shop, id));
  }

  decide(account: string, shop: string, id: string, action: ConsentDecision, operationKey: string, owner: ScopeOwner) {
    return this.http.post<AffiliationConsent>(this.consentPath(account, shop, id) + '/' + action,
      { operationKey }, { context: scopedRequestContext(owner) });
  }

  private path(account: string): string {
    return BILLING_ACCOUNT_ENDPOINTS.accounts + '/' + encodeURIComponent(account) + '/affiliations';
  }

  private consentPath(account: string, shop: string, id: string): string {
    return this.path(account) + '/' + encodeURIComponent(shop) + '/' + encodeURIComponent(id) + '/consent';
  }
}
