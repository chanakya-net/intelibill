import type { PlanFacts, PlanKind, TrialFacts } from './catalogue-api.service';

export interface TrialSnapshot extends Required<TrialFacts> {
  versionId: string;
  versionNumber: number;
  durationDays: number;
  developmentFixture: boolean;
}

export interface PlanSnapshot {
  plan: PlanFacts & {
    kind: PlanKind;
    versionNumber: number;
    developmentFixture: boolean;
    trialPolicy: TrialSnapshot;
    eligiblePaidGraceDays: number;
    graceExclusions: string[];
    billingTimeZone: string;
  };
  negotiationReason: string | null;
  accountingTreatmentReference: string;
}

export type CatalogueSnapshot = PlanSnapshot | TrialSnapshot;

export interface PublicationTarget {
  planId: string | null;
  versionId: string;
  expectedVersion: number;
}

export interface PreparedPublication extends PublicationTarget {
  operationKey: string;
  snapshotHash: string;
  snapshot: CatalogueSnapshot;
  reason: string;
  publishedAt: string | null;
}

export interface PublishedVersion {
  planId: string | null;
  versionId: string;
  version: number;
  status: 'published' | 'retired';
  snapshot: CatalogueSnapshot;
  publishedAt: string;
}

export interface PublicationHistory {
  items: PublishedVersion[];
  nextCursor: string | null;
  currentTrialVersionId: string | null;
}

export interface PublicationPreparation {
  operationKey: string;
  reason: string;
  trialPolicyVersionId: string | null;
}
