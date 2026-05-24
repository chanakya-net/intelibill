import { describe, expect, it } from 'vitest';

import type { OfflineQueuedSaleRecord } from '../services/offline-sale-core.types';
import type { OfflineSaleSyncResultDto } from '../services/sale.models';
import {
  addOfflineSyncResultToTotals,
  buildSyncRequestFromQueuedSale,
  isOfflineSaleSyncRetryable,
  mapOfflineSyncResult,
  mapToVisibleQueueCounts,
  normalizeOfflineQueueSyncStatus,
} from './offline-sale-sync.mapper';

describe('offline-sale-sync.mapper', () => {
  const record: OfflineQueuedSaleRecord = {
    key: 'shop-1::device-1::sale-1',
    shopId: 'shop-1',
    deviceId: 'device-1',
    clientSaleId: 'sale-1',
    idempotencyKey: 'idem-sale-1',
    invoiceNumber: 'INV-1',
    soldAt: '2026-05-24T10:00:00.000Z',
    payload: {
      clientSaleId: 'sale-1',
      idempotencyKey: 'idem-sale-1',
      shopId: 'shop-1',
      deviceId: 'device-1',
      invoiceNumber: 'INV-1',
      soldAt: '2026-05-24T10:00:00.000Z',
      paymentMethod: 1,
      customerId: 'customer-1',
      customerName: 'Customer',
      customerPhone: '555-0101',
      pricing: {
        lines: [
          {
            clientLineId: 'line-1',
            inventoryBatchId: 'batch-1',
            itemId: 'item-1',
            barcode: 'BC-1',
            itemName: 'Test Item',
            batchNumber: 'B-1',
            quantity: 2,
            salesPrice: 100,
            mrp: 120,
            costPrice: 70,
            taxRatePercent: 12,
            taxIncluded: true,
            hsnCode: null,
            preTaxAmount: 178.57,
            itemDiscountAmount: 0,
            saleDiscountAmount: 0,
            taxableAmount: 178.57,
            taxAmount: 21.43,
            lineTotal: 200,
            configuredRuleId: 'rule-1',
            configuredRulePercentage: 5,
            itemDiscountOverrideType: 1,
            itemDiscountOverrideValue: 2,
          },
        ],
        totals: {
          totalBeforeDiscount: 200,
          totalDiscount: 10,
          totalTax: 21,
          grandTotal: 211,
          paidAmount: 0,
          dueAmount: 211,
        },
        saleDiscountOverrideType: 2,
        saleDiscountOverrideValue: 3,
        configuredSaleRuleId: 'sale-rule',
        configuredSaleRuleType: 'threshold',
        configuredSaleRulePercentage: 10,
        configuredSaleRuleThresholdAmount: 300,
      },
    },
    status: 'Pending',
    warnings: [],
    errorCode: null,
    errorMessage: null,
    serverSaleId: null,
    createdAt: '2026-05-24T10:00:00.000Z',
    updatedAt: '2026-05-24T10:00:00.000Z',
    syncAttemptCount: 0,
    lastSyncAttemptAt: null,
    syncAttempts: [],
  };

  it('maps queued records to offline sale sync requests', () => {
    const request = buildSyncRequestFromQueuedSale(record);

    expect(request).toMatchObject({
      clientSaleId: 'sale-1',
      invoiceNumber: 'INV-1',
      customerId: 'customer-1',
      saleDiscountOverrideType: 2,
      saleDiscountOverrideValue: 3,
      configuredSaleRuleId: 'sale-rule',
      items: [expect.objectContaining({
        itemDiscountOverrideType: 1,
        itemDiscountOverrideValue: 2,
        configuredBatchRulePercentage: 5,
      })],
    });
  });

  it('maps backend result variants to queue result statuses', () => {
    const created = mapOfflineSyncResult({
      clientSaleId: 'sale-1',
      status: 'SyncedWithWarnings',
      saleId: 'server-1',
      invoiceNumber: 'INV-1',
      warnings: ['price changed'],
      errors: [],
    });

    expect(created).toEqual({
      status: 'SyncedWithWarnings',
      warnings: ['price changed'],
      errorCode: null,
      errorMessage: null,
      serverSaleId: 'server-1',
    });

    const normalized = normalizeOfflineQueueSyncStatus('Created');
    expect(normalized).toBe('Synced');

    const needsReview = mapOfflineSyncResult({
      clientSaleId: 'sale-1',
      status: 'needsreview',
      saleId: null,
      invoiceNumber: null,
      warnings: [],
      errors: [{ code: 'CONFLICT', message: 'invoice exists' }],
    } as OfflineSaleSyncResultDto);

    expect(needsReview).toEqual({
      status: 'NeedsReview',
      warnings: [],
      errorCode: 'CONFLICT',
      errorMessage: 'invoice exists',
      serverSaleId: null,
    });

    const missing = mapOfflineSyncResult(undefined);
    expect(missing.status).toBe('Failed');
    expect(missing.errorCode).toBe('offline_sync.result_missing');
  });

  it('maps status counts to visible queue counts', () => {
    const visible = mapToVisibleQueueCounts({
      Pending: 2,
      Syncing: 1,
      Synced: 4,
      SyncedWithWarnings: 3,
      NeedsReview: 5,
      Failed: 6,
    });

    expect(visible).toEqual({
      pending: 2,
      syncing: 1,
      failed: 6,
      warning: 3,
      needsReview: 5,
      totalVisible: 17,
    });
  });

  it('aggregates mapped sync result totals', () => {
    const totals = {
      syncedCount: 0,
      warningCount: 0,
      needsReviewCount: 0,
      failedCount: 0,
    };

    addOfflineSyncResultToTotals(totals, 'Synced');
    addOfflineSyncResultToTotals(totals, 'SyncedWithWarnings');
    addOfflineSyncResultToTotals(totals, 'NeedsReview');
    addOfflineSyncResultToTotals(totals, 'Failed');

    expect(totals).toEqual({
      syncedCount: 1,
      warningCount: 1,
      needsReviewCount: 1,
      failedCount: 1,
    });
  });

  it('checks retryable offline sale queue statuses', () => {
    expect(isOfflineSaleSyncRetryable('Failed')).toBe(true);
    expect(isOfflineSaleSyncRetryable('Synced')).toBe(false);
  });
});
