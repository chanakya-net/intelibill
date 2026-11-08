export type TenderMethod = 'Cash' | 'Upi' | 'Card' | 'BankTransfer' | 'Cheque';
export interface TenderLine {
  method: TenderMethod;
  appliedAmount: number;
  cashTendered?: number | null;
  change?: number | null;
  externalReference?: string | null;
  bankAccountId?: string | null;
}
export interface TillPostingContext {
  sessionId: string;
  overrideReason?: string | null;
}
export interface TillSettlementEvidence {
  disposition: 'NoPhysicalMovement' | 'MoneyReceived' | 'MoneyPaid';
  reason: string;
  tender?: TenderLine | null;
}
export interface TillCapabilities {
  enabled: boolean;
  mixedTenderEnabled: false;
  businessDate: string;
}
export interface Till {
  id: string;
  name: string;
}
export interface TillTotals {
  recordedTenderTotals: Record<string, number>;
  purposeTotals: Record<string, number>;
  storeCreditIssued: number;
  storeCreditRedeemed: number;
  receivableChange: number;
  movementCount: number;
}
export interface TillClose {
  id: string;
  openingFloat: number;
  lastMovementSequence: number;
  totals: TillTotals;
  expectedCash: number;
  countedCash: number;
  variance: number;
  closedBy: string;
  closedAt: string;
  reason: string | null;
  overrideReason: string | null;
  isOverdue: boolean;
  awaitingReview: boolean;
  businessDate: string;
  tillName: string;
  cashierName: string;
  cashierId: string;
}
export interface TillLatePosting {
  id: string;
  offlineSubmissionId: string;
  saleId: string;
  caseId: string;
  sessionId: string | null;
  closeId: string | null;
  sourceBusinessDate: string;
  occurredAt: string;
  recordedAt: string;
  cashierId: string;
  tenderLines: TenderLine[];
  evidenceStatus: 'Recorded' | 'Unknown';
  managerReason: string;
  includedInCount: false;
  evidenceReason?: string | null;
  supplementalAmount: number | null;
}
export interface TillSession {
  id: string;
  tillId: string;
  cashierId: string;
  tillName: string;
  cashierName: string;
  businessDate: string;
  businessTimeZoneId: string;
  openedAt: string;
  state: 'Open' | 'Closed';
  version: number;
  openingFloat: number;
  expectedCash: number;
  recordedTenderTotals: Record<string, number>;
  storeCreditIssued: number;
  storeCreditRedeemed: number;
  receivableChange: number;
  lastMovementSequence: number;
  close: TillClose | null;
  review: {
    id: string;
    closeId: string;
    reviewedBy: string;
    reviewedAt: string;
    reason: string;
  } | null;
  latePostings: TillLatePosting[];
  awaitingReview: boolean;
}
export interface TillPage<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}
export interface TillSource {
  parentSaleId?: string | null;
  paymentReceiptId?: string | null;
  expenseId?: string | null;
  id: string;
  sourceType: string;
  sourceId: string;
  purpose: string;
  occurredAt: string;
  recordedAt: string;
  actorUserId: string;
  operationKey: string;
  reason: string | null;
  overrideReason: string | null;
}
export interface TillMovement extends TillSource {
  sequence: number;
  method: string;
  signedAmount: number;
  cashTendered: number | null;
  change: number | null;
  externalReference: string | null;
  bankAccountId: string | null;
}
export interface TillSessionDetail {
  session: TillSession;
  movements: TillPage<TillMovement>;
  sources: TillPage<TillSource>;
}
export interface TillActionPayload {
  operationKey: string;
  [key: string]: unknown;
}
export interface TillEditableDraft {
  payload: Record<string, unknown>;
}
export interface TillDraftRecord {
  schemaVersion: 1;
  userId: string;
  shopId: string;
  targetId: string;
  revision: number;
  draft: TillEditableDraft;
  pending: {
    operationKey: string;
    payload: TillActionPayload;
    status: 'sending' | 'unknown';
  } | null;
  confirmedOperationKey: string | null;
  updatedAt: string;
}
