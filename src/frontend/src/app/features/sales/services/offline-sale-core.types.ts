import type { InstantDiscountRequest, InvoiceDocument } from './sale.models';

export type OfflineSaleQueueStatus = 'Pending' | 'Syncing' | 'Synced' | 'SyncedWithWarnings' | 'NeedsReview' | 'Failed';

export interface OfflineSalePricingLineInput {
  readonly clientLineId?: string;
  readonly inventoryBatchId: string;
  readonly itemId: string;
  readonly barcode: string;
  readonly itemName: string;
  readonly batchNumber: string;
  readonly quantity: number;
  readonly salesPrice: number;
  readonly mrp: number;
  readonly costPrice: number;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
  readonly itemDiscount: InstantDiscountRequest;
  readonly hsnCode: string | null;
  readonly lineType?: 'goods' | 'service';
  readonly serviceId?: string | null;
}

export interface OfflineSalePricingRuleInput {
  readonly ruleId: string;
  readonly ruleType: string;
  readonly inventoryBatchId?: string | null;
  readonly percentage: number;
  readonly thresholdAmount?: number | null;
  readonly startsAt?: string | null;
  readonly endsAt?: string | null;
}

export interface OfflineSalePricingInput {
  readonly soldAt: string;
  readonly paymentMethod: number;
  readonly paidAmount: number;
  readonly customerId: string | null;
  readonly customerName: string | null;
  readonly customerPhone: string | null;
  readonly saleDiscount: InstantDiscountRequest;
  readonly lines: readonly OfflineSalePricingLineInput[];
  readonly rules: readonly OfflineSalePricingRuleInput[];
}

export interface OfflineFrozenSaleLine {
  readonly clientLineId: string;
  readonly inventoryBatchId: string;
  readonly itemId: string;
  readonly barcode: string;
  readonly itemName: string;
  readonly batchNumber: string;
  readonly quantity: number;
  readonly salesPrice: number;
  readonly mrp: number;
  readonly costPrice: number;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
  readonly hsnCode: string | null;
  /** The unit printed on the issued document (UQC); absent on lines frozen before units, which print NOS. */
  readonly documentUnit?: string;
  readonly preTaxAmount: number;
  readonly itemDiscountAmount: number;
  readonly saleDiscountAmount: number;
  readonly taxableAmount: number;
  readonly taxAmount: number;
  readonly lineTotal: number;
  readonly configuredRuleId: string | null;
  readonly configuredRulePercentage?: number | null;
  readonly itemDiscountOverrideType?: number;
  readonly itemDiscountOverrideValue?: number;
  readonly lineType: 'goods' | 'service';
  readonly serviceId: string | null;
}

export interface OfflineFrozenSaleTotals {
  /** Sum of pre-tax line amounts. Absent on unversioned (legacy) payloads, which sent totalBeforeDiscount instead. */
  readonly subtotalBeforeDiscount?: number;
  readonly totalBeforeDiscount: number;
  readonly totalDiscount: number;
  readonly totalTax: number;
  readonly grandTotal: number;
  readonly paidAmount: number;
  readonly dueAmount: number;
}

export interface OfflineFrozenSalePricing {
  /** Arithmetic contract the amounts were frozen under; absent on payloads queued before versioning (legacy). */
  readonly calculationVersion?: number;
  readonly lines: readonly OfflineFrozenSaleLine[];
  readonly totals: OfflineFrozenSaleTotals;
  readonly saleDiscountOverrideType?: number;
  readonly saleDiscountOverrideValue?: number;
  readonly configuredSaleRuleId?: string | null;
  readonly configuredSaleRuleType?: string | null;
  readonly configuredSaleRulePercentage?: number | null;
  readonly configuredSaleRuleThresholdAmount?: number | null;
}

export interface OfflineQueuedSalePayload {
  readonly clientSaleId: string;
  readonly idempotencyKey: string;
  readonly shopId: string;
  readonly deviceId: string;
  readonly invoiceNumber: string;
  readonly soldAt: string;
  readonly pricing: OfflineFrozenSalePricing;
  readonly paymentMethod: number;
  readonly customerId: string | null;
  readonly customerName: string | null;
  readonly customerPhone: string | null;
  /** Signed-in user who issued the invoice. Absent on payloads queued before issuers were recorded (legacy). */
  readonly issuedByUserId?: string | null;
  /**
   * The document the invoice was issued as, frozen once at finalization from the lease's seller profile; every print
   * and reprint renders it. Absent on payloads queued before devices issued documents (legacy).
   */
  readonly document?: InvoiceDocument | null;
}

export interface OfflineArithmeticMismatch {
  readonly path: string;
  readonly supplied: number | null;
  readonly expected: number | null;
  readonly code: string;
}

export interface OfflineSaleSyncAttemptMeta {
  readonly attemptedAt: string;
  readonly ok: boolean;
  readonly errorCode?: string;
  readonly errorMessage?: string;
}

export interface OfflineQueuedSaleRecord {
  readonly key: string;
  readonly shopId: string;
  readonly deviceId: string;
  readonly clientSaleId: string;
  readonly idempotencyKey: string;
  readonly invoiceNumber: string;
  readonly soldAt: string;
  readonly payload: OfflineQueuedSalePayload;
  readonly status: OfflineSaleQueueStatus;
  readonly warnings: readonly string[];
  readonly errorCode: string | null;
  readonly errorMessage: string | null;
  readonly serverSaleId: string | null;
  /** Server reconciliation case for a NeedsReview result; absent on records synced before cases were returned. */
  readonly reviewCaseId?: string | null;
  readonly mismatches?: readonly OfflineArithmeticMismatch[];
  readonly createdAt: string;
  readonly updatedAt: string;
  readonly syncAttemptCount: number;
  readonly lastSyncAttemptAt: string | null;
  readonly syncAttempts: readonly OfflineSaleSyncAttemptMeta[];
}

export interface OfflineQueueSyncResult {
  readonly status: Extract<OfflineSaleQueueStatus, 'Synced' | 'SyncedWithWarnings' | 'NeedsReview' | 'Failed'>;
  readonly warnings?: readonly string[];
  readonly errorCode?: string | null;
  readonly errorMessage?: string | null;
  readonly serverSaleId?: string | null;
  readonly reviewCaseId?: string | null;
  readonly mismatches?: readonly OfflineArithmeticMismatch[];
}
