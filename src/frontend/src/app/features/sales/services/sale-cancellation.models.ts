import type { InvoiceDocument, InvoiceDocumentSeller, InvoiceDocumentBuyer } from './sale.models';
export interface CancelSaleRequest {
  till?: import('../../tills/services/till.models').TillPostingContext;
  operationKey: string;
  expectedVersion: number;
  reason: string;
  goodsRecoveredConfirmed: boolean;
  cashRefundConfirmed: boolean;
}
export interface SaleCancellationPreviewDto {
  saleId: string;
  invoiceNumber: string;
  document: InvoiceDocument | null;
  status: 'Posted' | 'Cancelled';
  expectedVersion: number;
  eligible: boolean;
  postingEnabled: boolean;
  businessDate: string;
  reasons: { code: string; message: string }[];
  lines: {
    saleItemId: string;
    itemId: string | null;
    inventoryBatchId: string | null;
    quantity: number;
    description: string;
    uqc: string;
  }[];
  totalAmount: number;
  dueReductionAmount: number;
  cashRefundAmount: number;
  externalRefundAmount: number;
  creditRestorationAmount: number;
  requiresGoodsConfirmation: boolean;
  requiresCashRefundConfirmation: boolean;
}
export interface SaleCancellationDto {
  id: string;
  saleId: string;
  reference: string;
  reason: string;
  actorUserId: string;
  cancelledAt: string;
  businessDate: string;
  businessTimeZoneId: string;
  policyVersion: string;
  originalInvoiceNumber: string;
  originalBusinessDate: string;
  totalAmount: number;
  revenueAmount: number;
  taxAmount: number;
  dueReductionAmount: number;
  cashRefundAmount: number;
  customerLedgerEntryId: string | null;
  seller: InvoiceDocumentSeller;
  buyer: InvoiceDocumentBuyer;
  goodsRecoveredConfirmed: boolean;
  cashRefundConfirmed: boolean;
  lines: {
    id: string;
    saleItemId: string;
    itemId: string;
    inventoryBatchId: string;
    stockTransactionId: string;
    quantity: number;
    description: string;
    uqc: string;
    originalCostPrice: number;
    revenueAmount: number;
    recordedCostAmount: number;
  }[];
}
export interface SaleCancellationEditableDraft {
  payload: Omit<CancelSaleRequest, 'operationKey'>;
}
export interface SaleCancellationDraftRecord {
  schemaVersion: 1;
  userId: string;
  shopId: string;
  targetId: string;
  revision: number;
  draft: SaleCancellationEditableDraft;
  pending: {
    operationKey: string;
    payload: CancelSaleRequest;
    status: 'sending' | 'unknown';
  } | null;
  confirmedOperationKey: string | null;
  updatedAt: string;
}
