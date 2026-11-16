import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { type Observable, switchMap } from 'rxjs';

import type {
  PlanRetirement, PlanRetirementTarget, PreparedPublication, PublicationHistory, PublicationPreparation, PublicationTarget,
} from './catalogue-publication.models';

export interface SuccessorSource {
  sourceVersionId: string;
  sourceExpectedVersion: number;
}

/**
 * An ordinary save names the exact draft version it edits as well as its revision: a successor restarts at
 * revision 1, so a revision alone could land a stale predecessor form on the newer draft. Creates name none.
 */
export interface DraftTarget {
  expectedVersion: number;
  expectedDraftVersionId?: string;
}

/** The revision and identity of the loaded draft, or a create when none is loaded. */
export function draftTarget(draft: { versionId: string; version: number } | null | undefined): DraftTarget {
  return draft ? { expectedVersion: draft.version, expectedDraftVersionId: draft.versionId } : { expectedVersion: 0 };
}

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

export interface PlanDraftCommand extends PlanFacts, Partial<SuccessorSource>, DraftTarget {
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

  saveTrial(command: TrialFacts & Partial<SuccessorSource> & DraftTarget) {
    return this.withProof((headers) =>
      this.http.put<{ draft: TrialDraft }>('/api/admin/trial-policy/draft', command, { headers }));
  }

  history(after: string | null) {
    return this.http.get<PublicationHistory>('/api/admin/catalogue/history', { params: after ? { after } : {} });
  }

  preparePublication(target: PublicationTarget, preparation: PublicationPreparation) {
    return this.withProof((headers) => this.http.post<PreparedPublication>(
      this.publicationPath(target) + '/publish-preview',
      { expectedVersion: target.expectedVersion, ...preparation }, { headers }));
  }

  confirmPublication(prepared: PreparedPublication) {
    const { operationKey, expectedVersion, snapshotHash } = prepared;
    return this.withProof((headers) => this.http.post<PreparedPublication>(
      this.publicationPath(prepared) + '/publish', { operationKey, expectedVersion, snapshotHash, confirmed: true }, { headers }));
  }

  recoverPublication(key: string) {
    return this.http.get<PreparedPublication>('/api/admin/catalogue/operations/' + encodeURIComponent(key));
  }

  retirePlan(target: PlanRetirementTarget, command: { operationKey: string; reason: string }) {
    return this.withProof((headers) => this.http.post<PlanRetirement>(
      this.publicationPath(target) + '/retire', { expectedVersion: target.expectedVersion, ...command, confirmed: true }, { headers }));
  }

  recoverRetirement(key: string) {
    return this.http.get<PlanRetirement>('/api/admin/catalogue/retirements/' + encodeURIComponent(key));
  }

  private publicationPath({ planId, versionId }: PublicationTarget) {
    return '/api/admin/' + (planId ? 'plans/' + planId : 'trial-policy') + '/versions/' + versionId;
  }

  private withProof<T>(send: (headers: Record<string, string>) => Observable<T>) {
    return this.http.get<{ requestToken: string }>('/api/admin/auth/sign-in/context').pipe(
      switchMap(({ requestToken }) => send({ 'X-Admin-CSRF': requestToken })),
    );
  }
}
