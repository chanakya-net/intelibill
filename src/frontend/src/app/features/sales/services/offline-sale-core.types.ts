import { InstantDiscountRequest } from './sale.service';

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
  readonly preTaxAmount: number;
  readonly itemDiscountAmount: number;
  readonly saleDiscountAmount: number;
  readonly taxableAmount: number;
  readonly taxAmount: number;
  readonly lineTotal: number;
  readonly configuredRuleId: string | null;
}

export interface OfflineFrozenSaleTotals {
  readonly totalBeforeDiscount: number;
  readonly totalDiscount: number;
  readonly totalTax: number;
  readonly grandTotal: number;
  readonly paidAmount: number;
  readonly dueAmount: number;
}

export interface OfflineFrozenSalePricing {
  readonly lines: readonly OfflineFrozenSaleLine[];
  readonly totals: OfflineFrozenSaleTotals;
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
}
