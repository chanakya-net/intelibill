import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { type Observable, switchMap } from 'rxjs';

export const licensingManager = 'licensing-manager';
export type PlanKind = 'single-shop' | 'multi-shop' | 'custom';

export const planKinds: Record<PlanKind, string> = {
  'single-shop': 'Single-shop',
  'multi-shop': 'Multi-shop',
  custom: 'Custom (negotiated)',
};

/** Facts are null while missing: never unlimited, never defaulted. */
export interface PlanFacts {
  name: string;
  monthlyPricePaise: number | null;
  annualPricePaise: number | null;
  includedShops: number | null;
  membersPerShop: number | null;
  extraShopsOffered: boolean | null;
  extraShopMonthlyPricePaise: number | null;
  extraShopAnnualPricePaise: number | null;
  customerTermsVersion: string | null;
  refundPolicyVersion: string | null;
}

export interface PlanDraft extends PlanFacts {
  versionId: string;
  versionNumber: number;
  status: 'draft';
  version: number;
  currency: 'INR';
  negotiationReason: string | null;
  missing: readonly string[];
  complete: boolean;
  updatedAt: string;
}

export interface PlanItem {
  planId: string;
  kind: PlanKind;
  draft: PlanDraft;
}

export interface PlanDraftCommand extends PlanFacts {
  expectedVersion: number;
  kind: PlanKind;
  reason: string | null;
}

export interface TrialFacts {
  activeMemberCap: number | null;
  maxConcurrentTrialShops: number | null;
  trialTermsVersion: string | null;
}

export interface TrialDraft extends TrialFacts {
  versionId: string;
  versionNumber: number;
  status: 'draft';
  version: number;
  durationDays: number;
  missing: readonly string[];
  complete: boolean;
  updatedAt: string;
}

export const factLabels: Record<string, string> = {
  monthlyPricePaise: 'Monthly base price',
  annualPricePaise: 'Annual base price',
  termPrice: 'A monthly or annual price',
  includedShops: 'Purchased shop slots',
  membersPerShop: 'Members per shop',
  extraShopsOffered: 'Extra-shop offer',
  extraShopMonthlyPricePaise: 'Extra-shop monthly price',
  extraShopAnnualPricePaise: 'Extra-shop annual price',
  customerTermsVersion: 'Customer terms version',
  refundPolicyVersion: 'Refund policy version',
  activeMemberCap: 'Active member cap',
  maxConcurrentTrialShops: 'Concurrent trial shop cap',
  trialTermsVersion: 'Trial terms version',
};

@Injectable({ providedIn: 'root' })
export class CatalogueApi {
  private readonly http = inject(HttpClient);

  listPlans(cursor: string | null) {
    return this.http.get<{ items: readonly PlanItem[]; nextCursor: string | null }>('/api/admin/plans',
      { params: cursor ? { cursor } : {} });
  }

  savePlan(planId: string, command: PlanDraftCommand) {
    return this.withProof((headers) =>
      this.http.put<PlanItem>('/api/admin/plans/' + planId + '/draft', command, { headers }));
  }

  trialDraft() {
    return this.http.get<{ draft: TrialDraft | null }>('/api/admin/trial-policy/draft');
  }

  saveTrial(command: TrialFacts & { expectedVersion: number }) {
    return this.withProof((headers) =>
      this.http.put<{ draft: TrialDraft }>('/api/admin/trial-policy/draft', command, { headers }));
  }

  private withProof<T>(send: (headers: Record<string, string>) => Observable<T>) {
    return this.http.get<{ requestToken: string }>('/api/admin/auth/sign-in/context').pipe(
      switchMap(({ requestToken }) => send({ 'X-Admin-CSRF': requestToken })),
    );
  }
}
