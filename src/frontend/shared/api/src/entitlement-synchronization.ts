/** Operational facts from subscription-status-v1. Revisions are independent across account and shop. */
export interface EntitlementSynchronization {
  readonly state: 'Pending' | 'Applied' | 'NeedsReview';
  readonly delivery: 'Queued' | 'Retrying' | 'AwaitingAcknowledgement' | 'Acknowledged' | 'DeadLetter';
  readonly savedAccountRevision: number;
  readonly savedShopRevision: number;
  readonly savedVerifiedAt: string;
  readonly appliedAccountRevision: number | null;
  readonly appliedShopRevision: number | null;
  readonly lastSynchronizedAt: string | null;
  readonly attention: string | null;
}
