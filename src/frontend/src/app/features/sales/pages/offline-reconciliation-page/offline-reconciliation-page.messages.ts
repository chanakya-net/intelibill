import { HttpErrorResponse } from '@angular/common/http';

import type {
  ReconciliationAction,
  ReconciliationCaseStatus,
  ReconciliationStatusFilter,
  RecoveryInvoiceStatus,
} from '../../services/offline-reconciliation.models';
import { offlineReviewReasonKey } from '../../utils/offline-sale-sync.mapper';

// Every server enum and error code the console shows is translated through these maps; anything the
// server adds later falls back to a generic label instead of leaking its raw code.

export const RESOLVE_REASON_MAX_LENGTH = 500;

/** Actions that point at another operation: the adjustment (stock) or the sale (link). */
export const LINKED_OPERATION_ACTIONS: readonly ReconciliationAction[] = [
  'LinkStockCorrection',
  'LinkExistingSale',
];

const ACTION_LABEL_KEYS: Readonly<Record<ReconciliationAction, string>> = {
  ReconcileTillSale: 'tills.reconcileTillSale',
  RetryHistoricalLease: 'sales.offlineReconciliation.actions.retryHistoricalLease',
  LinkExistingSale: 'sales.offlineReconciliation.actions.linkExistingSale',
  AcknowledgeVariance: 'sales.offlineReconciliation.actions.acknowledgeVariance',
  LinkStockCorrection: 'sales.offlineReconciliation.actions.linkStockCorrection',
  Escalate: 'sales.offlineReconciliation.actions.escalate',
};

const STATUS_LABEL_KEYS: Readonly<Record<ReconciliationCaseStatus, string>> = {
  Open: 'sales.offlineReconciliation.status.open',
  Escalated: 'sales.offlineReconciliation.status.escalated',
  Resolved: 'sales.offlineReconciliation.status.resolved',
};

const STATUS_SEVERITIES: Readonly<Record<ReconciliationCaseStatus, 'warn' | 'danger' | 'success'>> =
  {
    Open: 'warn',
    Escalated: 'danger',
    Resolved: 'success',
  };

const ISSUE_TYPE_LABEL_KEYS: Readonly<Record<string, string>> = {
  TillReview: 'tills.tillReview',
  ExpiryReview: 'sales.offlineReconciliation.issueTypes.expiryReview',
  StockVariance: 'sales.offlineReconciliation.issueTypes.stockVariance',
  InvoiceConflict: 'sales.offlineReconciliation.issueTypes.invoiceConflict',
  ValidationConflict: 'sales.offlineReconciliation.issueTypes.validationConflict',
  PricingVariance: 'sales.offlineReconciliation.issueTypes.pricingVariance',
  DiscountVariance: 'sales.offlineReconciliation.issueTypes.discountVariance',
  CustomerVariance: 'sales.offlineReconciliation.issueTypes.customerVariance',
  ArithmeticConflict: 'sales.offlineReconciliation.issueTypes.arithmeticConflict',
  IssueTimingReview: 'sales.offlineReconciliation.issueTypes.issueTimingReview',
  ProvenanceReview: 'sales.offlineReconciliation.issueTypes.provenanceReview',
  RecoveryReview: 'sales.offlineReconciliation.issueTypes.recoveryReview',
  // Informational: a device-issued sale posted without a GST document because the shop's profile did not allow one.
  DocumentReview: 'sales.offlineReconciliation.issueTypes.documentReview',
  // Held because the invoice's month is closed; posts after an owner reopens it.
  ClosedPeriodReview: 'sales.offlineReconciliation.issueTypes.closedPeriodReview',
  // Held because historical offline licence evidence is uncertain, conflicting, or unproven.
  LicenseHistoryReview: 'sales.offlineReconciliation.issueTypes.licenseHistoryReview',
};

const RECOVERY_STATUS_LABEL_KEYS: Readonly<Record<RecoveryInvoiceStatus, string>> = {
  ReviewOpened: 'sales.offlineReconciliation.bundle.resultStatus.reviewOpened',
  Escalated: 'sales.offlineReconciliation.bundle.resultStatus.escalated',
  AlreadyPosted: 'sales.offlineReconciliation.bundle.resultStatus.alreadyPosted',
  Rejected: 'sales.offlineReconciliation.bundle.resultStatus.rejected',
};

const RECOVERY_STATUS_SEVERITIES: Readonly<
  Record<RecoveryInvoiceStatus, 'info' | 'danger' | 'success' | 'warn'>
> = {
  ReviewOpened: 'info',
  Escalated: 'warn',
  AlreadyPosted: 'success',
  Rejected: 'danger',
};

const PAYMENT_METHOD_LABEL_KEYS: Readonly<Record<string, string>> = {
  cash: 'sales.newSale.paymentMethods.cash',
  upi: 'sales.newSale.paymentMethods.upi',
  card: 'sales.newSale.paymentMethods.card',
  credit: 'sales.newSale.paymentMethods.credit',
};

const SOURCE_LABEL_KEYS: Readonly<Record<string, string>> = {
  Sync: 'sales.offlineReconciliation.detail.source.sync',
  Recovery: 'sales.offlineReconciliation.detail.source.recovery',
};

const ERROR_KEYS: Readonly<Record<string, string>> = {
  'Reconciliation.CaseNotFound': 'sales.offlineReconciliation.errors.caseNotFound',
  'Reconciliation.CaseVersionConflict': 'sales.offlineReconciliation.errors.caseChanged',
  'Reconciliation.ConcurrentChange': 'sales.offlineReconciliation.errors.caseChanged',
  'Reconciliation.CaseAlreadyResolved': 'sales.offlineReconciliation.errors.caseChanged',
  'Reconciliation.ActionNotAllowed': 'sales.offlineReconciliation.errors.actionNotAllowed',
  'Reconciliation.UnknownAction': 'sales.offlineReconciliation.errors.actionNotAllowed',
  'Reconciliation.ActionForbidden': 'sales.offlineReconciliation.errors.actionForbidden',
  'Reconciliation.ReasonRequired': 'sales.offlineReconciliation.resolve.validation.reasonRequired',
  'Reconciliation.LinkedOperationRequired':
    'sales.offlineReconciliation.resolve.validation.linkedRequired',
  'Reconciliation.LinkedAdjustmentInvalid':
    'sales.offlineReconciliation.errors.linkedAdjustmentInvalid',
  'Reconciliation.OperationAlreadyLinked':
    'sales.offlineReconciliation.errors.operationAlreadyLinked',
  'Reconciliation.SaleIdentityMismatch': 'sales.offlineReconciliation.errors.saleIdentityMismatch',
  'Reconciliation.ProvenanceUnverified': 'sales.offlineReconciliation.errors.provenanceUnverified',
  'Reconciliation.EnvelopeMissing': 'sales.offlineReconciliation.errors.envelopeMissing',
  'Reconciliation.ReplayRejected': 'sales.offlineReconciliation.errors.replayRejected',
  'Operation.PayloadMismatch': 'sales.offlineReconciliation.errors.payloadMismatch',
  'Sale.InvoiceNumberAlreadyUsed':
    'sales.history.offlineQueue.review.reasons.invoiceNumberAlreadyUsed',
  'Recovery.BundleInvalid': 'sales.offlineReconciliation.errors.bundleInvalid',
  'Recovery.BundleFormatUnsupported': 'sales.offlineReconciliation.errors.bundleFormatUnsupported',
  'Recovery.BundleChecksumMismatch': 'sales.offlineReconciliation.errors.bundleChecksumMismatch',
  'Recovery.BundleShopMismatch': 'sales.offlineReconciliation.errors.bundleShopMismatch',
  'Recovery.BundleEmpty': 'sales.offlineReconciliation.errors.bundleEmpty',
  'Recovery.BundleTooLarge': 'sales.offlineReconciliation.errors.bundleTooLarge',
  'Recovery.LeaseNotOwned': 'sales.offlineReconciliation.errors.leaseNotOwned',
  'Recovery.IssuerMismatch': 'sales.offlineReconciliation.errors.issuerMismatch',
  'Recovery.NotAuthorized': 'sales.offlineReconciliation.errors.notAuthorized',
  'Licence.SecuritySuspended': 'sales.offlineReconciliation.errors.licenceSecuritySuspended',
  'PostingPeriod.Closed': 'apiErrors.postingPeriodClosed',
  'PostingPeriod.Busy': 'apiErrors.postingPeriodBusy',
  'OfflineLicenceHistory.EvidenceUnsupported':
    'sales.history.offlineQueue.review.reasons.licenceHistoryUnsupported',
  'OfflineLicenceHistory.EvidenceInvalid':
    'sales.history.offlineQueue.review.reasons.licenceHistoryInvalid',
  'OfflineLicenceHistory.OutsidePermitWindow':
    'sales.history.offlineQueue.review.reasons.licenceHistoryOutsideWindow',
  'OfflineLicenceHistory.RestrictionConflict':
    'sales.history.offlineQueue.review.reasons.licenceHistoryRestrictionConflict',
  'OfflineLicenceHistory.EvidenceMissing':
    'sales.history.offlineQueue.review.reasons.licenceHistoryMissing',
  'OfflineLicenceHistory.ConflictingReceipt':
    'sales.history.offlineQueue.review.reasons.licenceHistoryConflictingReceipt',
};

const LICENCE_HISTORY_SUBREASON_KEYS: Readonly<Record<string, string>> = {
  Supported: 'sales.offlineReconciliation.detail.licenceHistory.subreasons.supported',
  SecuritySuspended: 'sales.offlineReconciliation.detail.licenceHistory.subreasons.securitySuspended',
  LicenceDenied: 'sales.offlineReconciliation.detail.licenceHistory.subreasons.licenceDenied',
  EnvelopeMissing: 'sales.offlineReconciliation.detail.licenceHistory.subreasons.envelopeMissing',
  PayloadCorrupted: 'sales.offlineReconciliation.detail.licenceHistory.subreasons.payloadCorrupted',
  LeaseMissing: 'sales.offlineReconciliation.detail.licenceHistory.subreasons.leaseMissing',
  UndatedTradingRestriction:
    'sales.offlineReconciliation.detail.licenceHistory.subreasons.undatedTradingRestriction',
  CompetingReceipt: 'sales.offlineReconciliation.detail.licenceHistory.subreasons.competingReceipt',
  EvidenceMissingOnAnchoredLease:
    'sales.offlineReconciliation.detail.licenceHistory.subreasons.evidenceMissingOnAnchoredLease',
  ObserveOnlyUnproven:
    'sales.offlineReconciliation.detail.licenceHistory.subreasons.observeOnlyUnproven',
  LeaseReserverUnproven:
    'sales.offlineReconciliation.detail.licenceHistory.subreasons.leaseReserverUnproven',
  OutsideLeaseWindow:
    'sales.offlineReconciliation.detail.licenceHistory.subreasons.outsideLeaseWindow',
  RestrictionReleasedAfterIssue:
    'sales.offlineReconciliation.detail.licenceHistory.subreasons.restrictionReleasedAfterIssue',
  SoldAtAtOrAfterCutoff:
    'sales.offlineReconciliation.detail.licenceHistory.subreasons.soldAtAtOrAfterCutoff',
  CutoffUnknown: 'sales.offlineReconciliation.detail.licenceHistory.subreasons.cutoffUnknown',
  HistoricalCoverageUnproven:
    'sales.offlineReconciliation.detail.licenceHistory.subreasons.historicalCoverageUnproven',
  HistoricalRestrictionConflict:
    'sales.offlineReconciliation.detail.licenceHistory.subreasons.historicalRestrictionConflict',
  EvidenceMalformed: 'sales.offlineReconciliation.detail.licenceHistory.subreasons.evidenceMalformed',
  EvidenceVersionUnsupported:
    'sales.offlineReconciliation.detail.licenceHistory.subreasons.evidenceVersionUnsupported',
  SnapshotIdInvalid: 'sales.offlineReconciliation.detail.licenceHistory.subreasons.snapshotIdInvalid',
  LeaseMismatch: 'sales.offlineReconciliation.detail.licenceHistory.subreasons.leaseMismatch',
  IssuerMismatch: 'sales.offlineReconciliation.detail.licenceHistory.subreasons.issuerMismatch',
  PermitNotMatched: 'sales.offlineReconciliation.detail.licenceHistory.subreasons.permitNotMatched',
  KeyMismatch: 'sales.offlineReconciliation.detail.licenceHistory.subreasons.keyMismatch',
  SignatureMismatch: 'sales.offlineReconciliation.detail.licenceHistory.subreasons.signatureMismatch',
  TillSessionMismatch:
    'sales.offlineReconciliation.detail.licenceHistory.subreasons.tillSessionMismatch',
  ActorScopeMismatch:
    'sales.offlineReconciliation.detail.licenceHistory.subreasons.actorScopeMismatch',
  GrantNotMatched: 'sales.offlineReconciliation.detail.licenceHistory.subreasons.grantNotMatched',
  AffiliationMismatch:
    'sales.offlineReconciliation.detail.licenceHistory.subreasons.affiliationMismatch',
  GrantDigestMismatch:
    'sales.offlineReconciliation.detail.licenceHistory.subreasons.grantDigestMismatch',
  SoldAtBeforePermit:
    'sales.offlineReconciliation.detail.licenceHistory.subreasons.soldAtBeforePermit',
  SoldAtAtOrAfterPermitEnd:
    'sales.offlineReconciliation.detail.licenceHistory.subreasons.soldAtAtOrAfterPermitEnd',
  FirstReceiptBeforePermit:
    'sales.offlineReconciliation.detail.licenceHistory.subreasons.firstReceiptBeforePermit',
  SoldAtAfterFirstReceiptSkew:
    'sales.offlineReconciliation.detail.licenceHistory.subreasons.soldAtAfterFirstReceiptSkew',
  VerificationKeyUnavailable:
    'sales.offlineReconciliation.detail.licenceHistory.subreasons.verificationKeyUnavailable',
  SignatureInvalid: 'sales.offlineReconciliation.detail.licenceHistory.subreasons.signatureInvalid',
  ClaimMismatch: 'sales.offlineReconciliation.detail.licenceHistory.subreasons.claimMismatch',
  RestrictionReleasedAfterPermit:
    'sales.offlineReconciliation.detail.licenceHistory.subreasons.restrictionReleasedAfterPermit',
};

const LICENCE_HISTORY_FIELD_KEYS: Readonly<Record<string, string>> = {
  version: 'sales.offlineReconciliation.detail.licenceHistory.fields.version',
  licenceEvidence: 'sales.offlineReconciliation.detail.licenceHistory.fields.licenceEvidence',
  snapshotId: 'sales.offlineReconciliation.detail.licenceHistory.fields.snapshotId',
  leaseId: 'sales.offlineReconciliation.detail.licenceHistory.fields.leaseId',
  issuedByUserId: 'sales.offlineReconciliation.detail.licenceHistory.fields.issuedByUserId',
  permitId: 'sales.offlineReconciliation.detail.licenceHistory.fields.permitId',
  grantId: 'sales.offlineReconciliation.detail.licenceHistory.fields.grantId',
  keyId: 'sales.offlineReconciliation.detail.licenceHistory.fields.keyId',
  signedPermit: 'sales.offlineReconciliation.detail.licenceHistory.fields.signedPermit',
  tillSessionId: 'sales.offlineReconciliation.detail.licenceHistory.fields.tillSessionId',
  scope: 'sales.offlineReconciliation.detail.licenceHistory.fields.scope',
  grantSha256: 'sales.offlineReconciliation.detail.licenceHistory.fields.grantSha256',
  soldAt: 'sales.offlineReconciliation.detail.licenceHistory.fields.soldAt',
  firstReceivedAt: 'sales.offlineReconciliation.detail.licenceHistory.fields.firstReceivedAt',
  restriction: 'sales.offlineReconciliation.detail.licenceHistory.fields.restriction',
  sourceCompletionCutoff:
    'sales.offlineReconciliation.detail.licenceHistory.fields.sourceCompletionCutoff',
  payloadHash: 'sales.offlineReconciliation.detail.licenceHistory.fields.payloadHash',
  payloadJson: 'sales.offlineReconciliation.detail.licenceHistory.fields.payloadJson',
};

export const LICENCE_HISTORY_UNKNOWN_SUBREASON_KEY =
  'sales.offlineReconciliation.detail.licenceHistory.subreasons.unknown';
export const LICENCE_HISTORY_UNKNOWN_FIELD_KEY =
  'sales.offlineReconciliation.detail.licenceHistory.fields.unknown';

export const GENERIC_ERROR_KEY = 'sales.offlineReconciliation.errors.generic';
export const NETWORK_ERROR_KEY = 'sales.offlineReconciliation.errors.network';
export const CASE_CHANGED_KEY = 'sales.offlineReconciliation.errors.caseChanged';
export const BUNDLE_TOO_LARGE_KEY = 'sales.offlineReconciliation.errors.bundleTooLarge';
const OTHER_KEY = 'sales.offlineReconciliation.issueTypes.unknown';

export const STATUS_FILTER_OPTIONS: readonly {
  readonly label: string;
  readonly value: ReconciliationStatusFilter;
}[] = [
  { label: STATUS_LABEL_KEYS.Open, value: 'open' },
  { label: STATUS_LABEL_KEYS.Escalated, value: 'escalated' },
  { label: STATUS_LABEL_KEYS.Resolved, value: 'resolved' },
  { label: 'sales.offlineReconciliation.cases.filterAll', value: 'all' },
];

export function actionLabelKey(action: string): string {
  return ACTION_LABEL_KEYS[action as ReconciliationAction] ?? OTHER_KEY;
}

export function caseStatusLabelKey(status: string): string {
  return STATUS_LABEL_KEYS[status as ReconciliationCaseStatus] ?? OTHER_KEY;
}

export function caseStatusSeverity(status: string): 'warn' | 'danger' | 'success' | 'secondary' {
  return STATUS_SEVERITIES[status as ReconciliationCaseStatus] ?? 'secondary';
}

export function issueTypeLabelKey(issueType: string): string {
  return ISSUE_TYPE_LABEL_KEYS[issueType] ?? OTHER_KEY;
}

export function recoveryStatusLabelKey(status: string): string {
  return RECOVERY_STATUS_LABEL_KEYS[status as RecoveryInvoiceStatus] ?? OTHER_KEY;
}

export function recoveryStatusSeverity(
  status: string,
): 'info' | 'danger' | 'success' | 'warn' | 'secondary' {
  return RECOVERY_STATUS_SEVERITIES[status as RecoveryInvoiceStatus] ?? 'secondary';
}

export function paymentMethodLabelKey(method: string): string {
  return PAYMENT_METHOD_LABEL_KEYS[method.trim().toLowerCase()] ?? 'shops.unknown';
}

export function submissionSourceLabelKey(source: string): string {
  return SOURCE_LABEL_KEYS[source] ?? OTHER_KEY;
}

export function licenceHistoryReasonKey(reasonCode: string | null | undefined): string {
  if (reasonCode === 'Licence.SecuritySuspended') {
    return 'sales.offlineReconciliation.errors.licenceSecuritySuspended';
  }
  return offlineReviewReasonKey(reasonCode ?? null);
}

export function licenceHistorySubreasonKey(subreason: string | null | undefined): string {
  return (subreason && LICENCE_HISTORY_SUBREASON_KEYS[subreason]) || LICENCE_HISTORY_UNKNOWN_SUBREASON_KEY;
}

export function licenceHistoryFieldKey(field: string | null | undefined): string {
  return (field && LICENCE_HISTORY_FIELD_KEYS[field]) || LICENCE_HISTORY_UNKNOWN_FIELD_KEY;
}

/** A translated message for a server error code; unknown codes get the generic message. */
export function reconciliationErrorKey(code: string | null): string {
  return (code && ERROR_KEYS[code]) || GENERIC_ERROR_KEY;
}

/** The server answers `{ title: <code> }` (ProblemDetails); `code` is accepted too. */
export function serverErrorCode(error: unknown): string | null {
  if (!(error instanceof HttpErrorResponse)) return null;
  const body: unknown = error.error;
  if (!body || typeof body !== 'object') return null;
  const { title, code } = body as { title?: unknown; code?: unknown };
  if (typeof code === 'string' && code.includes('.')) return code;
  return typeof title === 'string' ? title : null;
}

/**
 * The request may or may not have reached the server (no answer, timeout, throttling or a server fault): a retry
 * must resend the same operation id so the server can replay a committed answer instead of acting twice.
 */
export function isUncertainFailure(error: unknown): boolean {
  if (!(error instanceof HttpErrorResponse)) return true;
  return error.status === 0 || error.status === 408 || error.status === 429 || error.status >= 500;
}
