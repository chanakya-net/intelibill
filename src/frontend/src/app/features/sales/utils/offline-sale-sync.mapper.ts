import type { OfflineQueueSyncResult, OfflineSaleQueueStatus } from '../services/offline-sale-core.types';
import type { OfflineSaleSyncRequest, OfflineSaleSyncResultDto } from '../services/sale.models';
import type { OfflineQueuedSaleRecord } from '../services/offline-sale-core.types';

export const OFFLINE_SALES_SYNC_BATCH_SIZE = 50;

export interface OfflineSalesVisibleQueueCounts {
  readonly pending: number;
  readonly syncing: number;
  readonly failed: number;
  readonly warning: number;
  readonly needsReview: number;
  readonly totalVisible: number;
}

export interface OfflineSalesSyncRunResult {
  readonly attemptedCount: number;
  readonly syncedCount: number;
  readonly warningCount: number;
  readonly needsReviewCount: number;
  readonly failedCount: number;
  readonly skippedReason?: 'NO_ACTIVE_SHOP' | 'NO_DEVICE' | 'API_UNREACHABLE' | 'NOT_RETRYABLE';
}

export function buildSyncRequestFromQueuedSale(record: OfflineQueuedSaleRecord): OfflineSaleSyncRequest {
  const payload = record.payload;
  const totals = payload.pricing.totals;

  return {
    clientSaleId: payload.clientSaleId,
    invoiceNumber: payload.invoiceNumber,
    soldAt: payload.soldAt,
    customerId: payload.customerId,
    customerName: payload.customerName,
    customerPhone: payload.customerPhone,
    paymentMethod: payload.paymentMethod,
    paidAmount: totals.paidAmount,
    dueAmount: totals.dueAmount,
    subtotalBeforeDiscount: totals.totalBeforeDiscount,
    totalBeforeDiscount: totals.totalBeforeDiscount,
    totalDiscountAmount: totals.totalDiscount,
    totalTaxAmount: totals.totalTax,
    totalAmount: totals.grandTotal,
    saleDiscountOverrideType: payload.pricing.saleDiscountOverrideType ?? 0,
    saleDiscountOverrideValue: payload.pricing.saleDiscountOverrideValue ?? 0,
    configuredSaleRuleId: payload.pricing.configuredSaleRuleId ?? null,
    configuredSaleRuleType: payload.pricing.configuredSaleRuleType ?? null,
    configuredSaleRulePercentage: payload.pricing.configuredSaleRulePercentage ?? null,
    configuredSaleRuleThresholdAmount: payload.pricing.configuredSaleRuleThresholdAmount ?? null,
    items: payload.pricing.lines.map((line) => ({
      barcode: line.barcode,
      batchNumber: line.batchNumber,
      itemName: line.itemName,
      quantity: line.quantity,
      costPrice: line.costPrice,
      salesPrice: line.salesPrice,
      mrp: line.mrp,
      taxRatePercent: line.taxRatePercent,
      isPriceIncludingTax: line.taxIncluded,
      inventoryBatchId: line.inventoryBatchId,
      preTaxAmountBeforeDiscount: line.preTaxAmount,
      itemDiscountAmount: line.itemDiscountAmount,
      saleDiscountAmount: line.saleDiscountAmount,
      taxableAmount: line.taxableAmount,
      taxAmount: line.taxAmount,
      totalAmount: line.lineTotal,
      configuredBatchRuleId: line.configuredRuleId,
      configuredBatchRulePercentage: line.configuredRulePercentage ?? null,
      itemDiscountOverrideType: line.itemDiscountOverrideType ?? 0,
      itemDiscountOverrideValue: line.itemDiscountOverrideValue ?? 0,
      hsnCode: line.hsnCode,
      lineType: line.lineType === 'service' ? 'Service' : 'Goods',
      serviceId: line.serviceId ?? null,
    })),
  };
}

export function mapOfflineSyncResult(result: OfflineSaleSyncResultDto | undefined): OfflineQueueSyncResult {
  if (!result) {
    return {
      status: 'Failed',
      errorCode: 'offline_sync.result_missing',
      errorMessage: 'Offline sale sync did not return a result for this sale.',
    };
  }

  return {
    status: normalizeOfflineQueueSyncStatus(result.status),
    warnings: result.warnings ?? [],
    errorCode: result.errors[0]?.code ?? null,
    errorMessage: result.errors[0]?.message ?? null,
    serverSaleId: result.saleId ?? null,
  };
}

export function normalizeOfflineQueueSyncStatus(status: string): OfflineQueueSyncResult['status'] {
  const normalized = status.trim().toLowerCase();

  if (normalized === 'created' || normalized === 'duplicate' || normalized === 'synced') return 'Synced';
  if (normalized === 'syncedwithwarnings') return 'SyncedWithWarnings';
  if (normalized === 'needsreview') return 'NeedsReview';
  if (normalized === 'failed') return 'Failed';

  return 'Failed';
}

export function mapToVisibleQueueCounts(
  counts: Record<OfflineSaleQueueStatus, number>,
): OfflineSalesVisibleQueueCounts {
  const visible = {
    pending: counts.Pending,
    syncing: counts.Syncing,
    failed: counts.Failed,
    warning: counts.SyncedWithWarnings,
    needsReview: counts.NeedsReview,
  };

  return {
    ...visible,
    totalVisible: visible.pending + visible.syncing + visible.failed + visible.warning + visible.needsReview,
  };
}

export function isOfflineSaleSyncRetryable(status: OfflineSaleQueueStatus): boolean {
  return status === 'Pending' || status === 'Failed';
}

export function addOfflineSyncResultToTotals(
  totals: { syncedCount: number; warningCount: number; needsReviewCount: number; failedCount: number },
  status: OfflineQueueSyncResult['status'],
): void {
  if (status === 'Synced') totals.syncedCount += 1;
  else if (status === 'SyncedWithWarnings') totals.warningCount += 1;
  else if (status === 'NeedsReview') totals.needsReviewCount += 1;
  else totals.failedCount += 1;
}
