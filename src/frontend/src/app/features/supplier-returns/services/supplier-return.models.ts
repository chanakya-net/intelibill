export type SupplierReturnAction = 'shipment' | 'credit' | 'reversal';
export interface SupplierReturnLineInput {
  receiptValueId: string;
  expectedReceiptVersion: number;
  expectedBatchVersion: number;
  quantity: number;
  proposedCreditAmount: number;
}
export interface SupplierReturnRequest {
  operationKey: string;
  supplierId: string;
  reason: string;
  creditAccepted: boolean;
  supplierReference: string | null;
  lines: SupplierReturnLineInput[];
}
export interface SupplierReturnCreditRequest {
  operationKey: string;
  expectedVersion: number;
  reason: string;
  supplierReference: string | null;
  lines: { returnLineId: string; acceptedAmount: number }[];
}
export interface ReceiptReversalRequest {
  operationKey: string;
  reason: string;
  expectedReceiptVersion: number;
  expectedBatchVersion: number;
}
export interface GroupReceiptReversalRequest {
  operationKey: string;
  reason: string;
  lines: Pick<
    SupplierReturnLineInput,
    'receiptValueId' | 'expectedReceiptVersion' | 'expectedBatchVersion'
  >[];
}
export type SupplierReturnMutationPayload =
  | SupplierReturnRequest
  | SupplierReturnCreditRequest
  | ReceiptReversalRequest
  | GroupReceiptReversalRequest;
export interface SupplierReturnEditableDraft {
  actionKind: SupplierReturnAction;
  payload:
    | Omit<SupplierReturnRequest, 'operationKey'>
    | Omit<SupplierReturnCreditRequest, 'operationKey'>
    | Omit<ReceiptReversalRequest, 'operationKey'>
    | Omit<GroupReceiptReversalRequest, 'operationKey'>;
}
export interface SupplierReturnDraftRecord {
  schemaVersion: 1;
  userId: string;
  shopId: string;
  targetId: string;
  revision: number;
  draft: SupplierReturnEditableDraft;
  pending: null | {
    operationKey: string;
    payload: SupplierReturnMutationPayload;
    status: 'sending' | 'unknown';
  };
  confirmedOperationKey: string | null;
  updatedAt: string;
}
export interface SupplierReturnLine {
  returnLineId: string;
  receiptValueId: string;
  inventoryBatchId: string;
  itemId: string;
  purchaseOrderReceiptLineId: string | null;
  quantity: number;
  proposedCreditAmount: number;
  receivedQuantity: number;
  effectiveTotalCost: number;
  itemName: string;
  uqc: string;
  sourceReference: string | null;
  stockTransactionId: string;
  acceptedAmount: number | null;
  supplierLedgerEntryId: string | null;
}
export interface SupplierReturnDetail {
  returnId: string;
  supplierId: string;
  returnNumber: string;
  status: 'PendingSupplierCredit' | 'CreditAccepted';
  version: number;
  reason: string;
  shippedAt: string;
  businessDate: string;
  businessTimeZoneId: string;
  shopName: string;
  supplierName: string;
  lines: SupplierReturnLine[];
  credit: null | {
    creditId: string;
    acceptedAt: string;
    businessDate: string;
    acceptedBy: string;
    reason: string;
    supplierReference: string | null;
  };
}
export interface ReceiptReversalLot {
  reversalId: string;
  receiptValueId: string;
  quantity: number;
  effectiveAmount: number;
  stockTransactionId: string;
  supplierLedgerEntryId: string | null;
  reversedAt: string;
  reason: string;
}
export interface ReceiptReversalResult {
  lots: ReceiptReversalLot[];
}
export interface PurchaseReceiptDetail {
  receiptValueId: string;
  inventoryBatchId: string;
  itemId: string;
  supplierId: string | null;
  purchaseOrderReceiptLineId: string | null;
  batchNumber: string;
  itemName: string;
  uqc: string;
  quantityScale: number;
  receiptVersion: number;
  batchVersion: number;
  receivedQuantity: number;
  availableQuantity: number;
  returnedQuantity: number;
  reversedQuantity: number;
  netRetainedQuantity: number;
  returnableQuantity: number;
  effectiveTotalCost: number | null;
  returnCreditAmount: number;
  allocatedPaymentAmount: number;
  netPayableAmount: number;
  outstandingAmount: number;
  supplierCreditAmount: number;
  suggestedCreditAmount: number;
  isVoided: boolean;
  reversalEligibilityReasons: string[];
  returns: SupplierReturnDetail[];
  reversals: ReceiptReversalLot[];
}
