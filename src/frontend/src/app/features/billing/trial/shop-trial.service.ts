import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { scopedRequestContext } from '../../../core/context/scoped-request';
import type { ScopeOwner } from '../../../core/context/shop-scope';

export interface TrialPolicy {
  readonly policyId: string;
  readonly policyRevision: number;
  readonly termsVersion: string;
  readonly memberCap: number;
  readonly accountTrialCap: number;
  readonly developmentFixture: boolean;
}

export interface TrialPreview {
  readonly shopId: string;
  readonly policy: TrialPolicy | null;
  readonly eligible: boolean;
  readonly reason: string | null;
  readonly prospectiveEnd: string;
  readonly activeMembers: number;
}

export interface TrialAcceptance {
  readonly operationKey: string;
  readonly policyId: string;
  readonly policyRevision: number;
  readonly termsVersion: string;
  readonly termsAccepted: true;
}

export interface TrialOperation {
  readonly operationKey: string;
  readonly status: 'Pending' | 'Applied' | 'NeedsReview';
  readonly reason: string | null;
  readonly acceptedAt: string;
  readonly endsAt: string;
}

@Injectable({ providedIn: 'root' })
export class ShopTrialService {
  private readonly http = inject(HttpClient);

  preview(owner: ScopeOwner) {
    return this.http.get<TrialPreview>(this.path(owner), { context: scopedRequestContext(owner) });
  }

  accept(owner: ScopeOwner, body: TrialAcceptance) {
    return this.http.post<TrialOperation>(this.path(owner), body, { context: scopedRequestContext(owner) });
  }

  recover(owner: ScopeOwner, operation: string) {
    return this.http.get<TrialOperation>(this.path(owner) + '/operations/' + encodeURIComponent(operation),
      { context: scopedRequestContext(owner) });
  }

  private path(owner: ScopeOwner): string {
    return `${API_BASE_URL}/licensing/shops/${encodeURIComponent(owner.shopId)}/trial`;
  }
}
