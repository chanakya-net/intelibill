import { HttpErrorResponse } from '@angular/common/http';

import type {
  ReconciliationAction,
  ReconciliationCaseStatus,
  ReconciliationStatusFilter,
  RecoveryInvoiceStatus,
} from '../../services/offline-reconciliation.models';

// Every server enum and error code the console shows is translated through these maps; anything the
// server adds later falls back to a generic label instead of leaking its raw code.

export const RESOLVE_REASON_MAX_LENGTH = 500;

/** Actions that point at another operation: the adjustment (stock) or the sale (link). */
export const LINKED_OPERATION_ACTIONS: readonly ReconciliationAction[] = ['LinkStockCorrection', 'LinkExistingSale'];

const ACTION_LABEL_KEYS: Readonly<Record<ReconciliationAction, string>> = {
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

const STATUS_SEVERITIES: Readonly<Record<ReconciliationCaseStatus, 'warn' | 'danger' | 'success'>> = {
  Open: 'warn',
  Escalated: 'danger',
  Resolved: 'success',
};

const ISSUE_TYPE_LABEL_KEYS: Readonly<Record<string, string>> = {
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
};

const RECOVERY_STATUS_LABEL_KEYS: Readonly<Record<RecoveryInvoiceStatus, string>> = {
  ReviewOpened: 'sales.offlineReconciliation.bundle.resultStatus.reviewOpened',
  Escalated: 'sales.offlineReconciliation.bundle.resultStatus.escalated',
  AlreadyPosted: 'sales.offlineReconciliation.bundle.resultStatus.alreadyPosted',
  Rejected: 'sales.offlineReconciliation.bundle.resultStatus.rejected',
};

const RECOVERY_STATUS_SEVERITIES: Readonly<Record<RecoveryInvoiceStatus, 'info' | 'danger' | 'success' | 'warn'>> = {
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
  'Reconciliation.LinkedOperationRequired': 'sales.offlineReconciliation.resolve.validation.linkedRequired',
  'Reconciliation.LinkedAdjustmentInvalid': 'sales.offlineReconciliation.errors.linkedAdjustmentInvalid',
  'Reconciliation.OperationAlreadyLinked': 'sales.offlineReconciliation.errors.operationAlreadyLinked',
  'Reconciliation.SaleIdentityMismatch': 'sales.offlineReconciliation.errors.saleIdentityMismatch',
  'Reconciliation.ProvenanceUnverified': 'sales.offlineReconciliation.errors.provenanceUnverified',
  'Reconciliation.EnvelopeMissing': 'sales.offlineReconciliation.errors.envelopeMissing',
  'Reconciliation.ReplayRejected': 'sales.offlineReconciliation.errors.replayRejected',
  'Operation.PayloadMismatch': 'sales.offlineReconciliation.errors.payloadMismatch',
  'Sale.InvoiceNumberAlreadyUsed': 'sales.history.offlineQueue.review.reasons.invoiceNumberAlreadyUsed',
  'Recovery.BundleInvalid': 'sales.offlineReconciliation.errors.bundleInvalid',
  'Recovery.BundleFormatUnsupported': 'sales.offlineReconciliation.errors.bundleFormatUnsupported',
  'Recovery.BundleChecksumMismatch': 'sales.offlineReconciliation.errors.bundleChecksumMismatch',
  'Recovery.BundleShopMismatch': 'sales.offlineReconciliation.errors.bundleShopMismatch',
  'Recovery.BundleEmpty': 'sales.offlineReconciliation.errors.bundleEmpty',
  'Recovery.BundleTooLarge': 'sales.offlineReconciliation.errors.bundleTooLarge',
  'Recovery.LeaseNotOwned': 'sales.offlineReconciliation.errors.leaseNotOwned',
  'Recovery.IssuerMismatch': 'sales.offlineReconciliation.errors.issuerMismatch',
  'Recovery.NotAuthorized': 'sales.offlineReconciliation.errors.notAuthorized',
};

export const GENERIC_ERROR_KEY = 'sales.offlineReconciliation.errors.generic';
export const NETWORK_ERROR_KEY = 'sales.offlineReconciliation.errors.network';
export const CASE_CHANGED_KEY = 'sales.offlineReconciliation.errors.caseChanged';
export const BUNDLE_TOO_LARGE_KEY = 'sales.offlineReconciliation.errors.bundleTooLarge';
const OTHER_KEY = 'sales.offlineReconciliation.issueTypes.unknown';

export const STATUS_FILTER_OPTIONS: readonly { readonly label: string; readonly value: ReconciliationStatusFilter }[] = [
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

export function recoveryStatusSeverity(status: string): 'info' | 'danger' | 'success' | 'warn' | 'secondary' {
  return RECOVERY_STATUS_SEVERITIES[status as RecoveryInvoiceStatus] ?? 'secondary';
}

export function paymentMethodLabelKey(method: string): string {
  return PAYMENT_METHOD_LABEL_KEYS[method.trim().toLowerCase()] ?? 'shops.unknown';
}

export function submissionSourceLabelKey(source: string): string {
  return SOURCE_LABEL_KEYS[source] ?? OTHER_KEY;
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
