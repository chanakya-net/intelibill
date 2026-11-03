// Mirrors the camelCase JSON of the offline reconciliation API. Every enum travels as its name.

export type ReconciliationStatusFilter = 'open' | 'escalated' | 'resolved' | 'all';
export type ReconciliationCaseStatus = 'Open' | 'Escalated' | 'Resolved';
export type ReconciliationAction =
  | 'RetryHistoricalLease'
  | 'LinkExistingSale'
  | 'AcknowledgeVariance'
  | 'LinkStockCorrection'
  | 'Escalate';

/**
 * A person named on a case. `displayName` is an empty string for anyone who is not (and never was) a member of
 * the shop, for example an issuer the device merely asserted; the UI must then show its own translated
 * "unknown user" text instead of an empty name.
 */
export interface ReconciliationPerson {
  readonly userId: string;
  readonly displayName: string;
}

export interface ReconciliationCaseSummary {
  readonly caseId: string;
  readonly version: number;
  readonly invoiceNumber: string | null;
  readonly clientSaleId: string;
  readonly deviceId: string;
  readonly issueType: string;
  readonly status: ReconciliationCaseStatus;
  readonly reasonCodes: readonly string[];
  readonly message: string;
  readonly saleId: string | null;
  readonly resolution: ReconciliationAction | null;
  readonly createdAt: string;
  readonly createdBy: ReconciliationPerson;
  readonly resolvedAt: string | null;
}

export interface ReconciliationCasePage {
  readonly items: readonly ReconciliationCaseSummary[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly unresolvedCount: number;
}

export interface OfflineInvoiceSnapshotLine {
  readonly itemName: string;
  readonly barcode: string;
  readonly batchNumber: string | null;
  readonly quantity: number;
  readonly salesPrice: number;
  readonly taxRatePercent: number;
  readonly taxAmount: number;
  readonly totalAmount: number;
}

export interface OfflineInvoiceSnapshot {
  readonly invoiceNumber: string;
  readonly soldAt: string;
  readonly customerName: string | null;
  readonly customerPhone: string | null;
  readonly paymentMethod: string;
  readonly subtotalBeforeDiscount: number;
  readonly totalDiscountAmount: number;
  readonly totalTaxAmount: number;
  readonly totalAmount: number;
  readonly paidAmount: number;
  readonly dueAmount: number;
  readonly lines: readonly OfflineInvoiceSnapshotLine[];
}

export interface OfflineSubmission {
  readonly submissionId: string;
  readonly source: string;
  readonly claimedSoldAt: string;
  readonly receivedAt: string;
  readonly payloadHash: string;
  readonly originalLeaseId: string | null;
  readonly submittedBy: ReconciliationPerson;
  readonly assertedIssuer: ReconciliationPerson | null;
  readonly originalIssuer: ReconciliationPerson | null;
  readonly recoveryOperationId: string | null;
  readonly invoice: OfflineInvoiceSnapshot;
}

export interface ReconciliationCaseEvent {
  readonly action: string;
  readonly fromStatus: string;
  readonly toStatus: string;
  readonly actor: ReconciliationPerson;
  readonly reason: string;
  readonly linkedOperationId: string | null;
  readonly occurredAt: string;
}

export interface ReconciliationCase extends ReconciliationCaseSummary {
  readonly itemId: string | null;
  readonly inventoryBatchId: string | null;
  readonly printedQuantity: number | null;
  readonly availableQuantity: number | null;
  readonly consumedQuantity: number | null;
  readonly unresolvedQuantity: number | null;
  readonly resolutionReason: string | null;
  readonly linkedOperationId: string | null;
  readonly resolutionStockTransactionId: string | null;
  readonly resolvedBy: ReconciliationPerson | null;
  readonly hasOriginalEnvelope: boolean;
  readonly submission: OfflineSubmission | null;
  /** Authoritative: the console offers exactly these and nothing else. */
  readonly availableActions: readonly ReconciliationAction[];
  readonly events: readonly ReconciliationCaseEvent[];
}

export interface ResolveReconciliationCaseRequest {
  /** A fresh id per resolution intent, reused when retrying the same one. */
  readonly operationId: string;
  /** The case `version` the reviewer was shown. */
  readonly expectedVersion: number;
  readonly action: ReconciliationAction;
  /** Required, up to 500 characters. */
  readonly reason: string;
  /** The adjustment (LinkStockCorrection) or sale (LinkExistingSale). */
  readonly linkedOperationId?: string | null;
}

export type RecoveryInvoiceStatus = 'ReviewOpened' | 'Escalated' | 'AlreadyPosted' | 'Rejected';

export interface RecoveryInvoiceError {
  readonly code: string;
  readonly message: string;
}

export interface RecoveryInvoiceResult {
  readonly clientSaleId: string;
  readonly invoiceNumber: string;
  readonly status: RecoveryInvoiceStatus;
  readonly caseId: string | null;
  readonly saleId: string | null;
  readonly errors: readonly RecoveryInvoiceError[];
}

export interface RecoverySubmissionResult {
  readonly operationId: string;
  readonly results: readonly RecoveryInvoiceResult[];
}
