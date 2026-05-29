import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, Subject } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import type { AuthSession } from '../../../core/auth/auth.models';
import { NetworkStatusService } from '../../../core/services/network-status.service';
import { OfflineSalesDeviceSettingsStorage } from '../../../core/storage/offline-sales-device-settings.storage';
import type { OfflineQueuedSaleRecord } from './offline-sale-core.types';
import { OfflineSalesQueueIndexedDbService } from './offline-sales-queue-indexeddb.service';
import { OfflineSalesQueueSyncService } from './offline-sales-queue-sync.service';
import { SaleService } from './sale.service';

describe('OfflineSalesQueueSyncService', () => {
  const activeSession = signal<AuthSession | null>(makeSession('shop-1'));
  const canReachApi = signal(true);
  const queueDb = {
    getPendingSales: vi.fn<OfflineSalesQueueIndexedDbService['getPendingSales']>(),
    getRetryableSales: vi.fn<OfflineSalesQueueIndexedDbService['getRetryableSales']>(),
    getQueuedSale: vi.fn<OfflineSalesQueueIndexedDbService['getQueuedSale']>(),
    markSyncInProgress: vi.fn<OfflineSalesQueueIndexedDbService['markSyncInProgress']>(),
    applySyncResult: vi.fn<OfflineSalesQueueIndexedDbService['applySyncResult']>(),
    getStatusCounts: vi.fn<OfflineSalesQueueIndexedDbService['getStatusCounts']>(),
    deleteOldSyncedRecords: vi.fn<OfflineSalesQueueIndexedDbService['deleteOldSyncedRecords']>(),
  };
  const saleService = {
    syncOfflineSales: vi.fn<SaleService['syncOfflineSales']>(),
  };
  const settingsStorage = {
    loadSettings: vi.fn<OfflineSalesDeviceSettingsStorage['loadSettings']>(),
  };
  const networkStatus = {
    canReachApi,
    checkConnectivity: vi.fn<NetworkStatusService['checkConnectivity']>(),
  };

  function setup(): OfflineSalesQueueSyncService {
    TestBed.configureTestingModule({
      providers: [
        OfflineSalesQueueSyncService,
        { provide: AuthService, useValue: { session: activeSession } },
        { provide: NetworkStatusService, useValue: networkStatus },
        { provide: OfflineSalesDeviceSettingsStorage, useValue: settingsStorage },
        { provide: OfflineSalesQueueIndexedDbService, useValue: queueDb },
        { provide: SaleService, useValue: saleService },
      ],
    });

    return TestBed.inject(OfflineSalesQueueSyncService);
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
    activeSession.set(makeSession('shop-1'));
    canReachApi.set(true);
    vi.clearAllMocks();
    settingsStorage.loadSettings.mockReturnValue({
      shopId: 'shop-1',
      deviceId: 'device-1',
      label: 'Counter 1',
      enabled: true,
      enabledAt: '2026-05-22T00:00:00.000Z',
      enabledByUserId: 'user-1',
      enabledByUserName: 'Test User',
      lastCompleteSnapshotAt: '2026-05-22T00:00:00.000Z',
      lastApiVerifiedAt: '2026-05-22T00:00:00.000Z',
      lastSnapshotWarningMarker: null,
      lastReservedLease: null,
    });
    queueDb.getPendingSales.mockResolvedValue([]);
    queueDb.getRetryableSales.mockResolvedValue([]);
    queueDb.getQueuedSale.mockResolvedValue(null);
    queueDb.markSyncInProgress.mockImplementation(async (shopId, deviceId, clientSaleId) =>
      makeRecord(clientSaleId, { shopId, deviceId, status: 'Syncing' }),
    );
    queueDb.applySyncResult.mockResolvedValue(null);
    queueDb.getStatusCounts.mockResolvedValue({
      Pending: 0,
      Syncing: 0,
      Synced: 0,
      SyncedWithWarnings: 0,
      NeedsReview: 0,
      Failed: 0,
    });
    queueDb.deleteOldSyncedRecords.mockResolvedValue(0);
    saleService.syncOfflineSales.mockReturnValue(of({ results: [] }));
    networkStatus.checkConnectivity.mockResolvedValue(undefined);
  });

  it('syncs pending active-shop records in batches of at most 50', async () => {
    const service = setup();
    const records = Array.from({ length: 120 }, (_, index) => makeRecord(`sale-${index + 1}`));
    queueDb.getPendingSales.mockResolvedValue(records);
    saleService.syncOfflineSales.mockImplementation((request) =>
      of({
        results: request.sales.map((sale) => ({
          clientSaleId: sale.clientSaleId,
          status: 'created',
          saleId: `server-${sale.clientSaleId}`,
          invoiceNumber: sale.invoiceNumber,
          errors: [],
          warnings: [],
        })),
      }),
    );

    const result = await service.syncActiveShop();

    expect(result.syncedCount).toBe(120);
    expect(saleService.syncOfflineSales).toHaveBeenCalledTimes(3);
    expect(saleService.syncOfflineSales.mock.calls.map(([request]) => request.sales.length)).toEqual([50, 50, 20]);
    expect(queueDb.deleteOldSyncedRecords).toHaveBeenCalled();
  });

  it('locks concurrent sync runs per shop and device', async () => {
    const service = setup();
    queueDb.getPendingSales.mockResolvedValue([makeRecord('sale-1')]);
    const response$ = new Subject<{ results: { clientSaleId: string; status: string; saleId: string; invoiceNumber: string; errors: never[]; warnings: never[] }[] }>();
    saleService.syncOfflineSales.mockReturnValue(response$);

    const first = service.syncForShop('shop-1', 'device-1');
    const second = service.syncForShop('shop-1', 'device-1');
    response$.next({ results: [{ clientSaleId: 'sale-1', status: 'Synced', saleId: 'server-1', invoiceNumber: 'INV-sale-1', errors: [], warnings: [] }] });
    response$.complete();
    await Promise.all([first, second]);

    expect(saleService.syncOfflineSales).toHaveBeenCalledTimes(1);
  });

  it('locks a single-record manual retry behind an in-flight shop sync', async () => {
    const service = setup();
    queueDb.getPendingSales.mockResolvedValue([makeRecord('sale-1')]);
    const response$ = new Subject<{ results: { clientSaleId: string; status: string; saleId: string; invoiceNumber: string; errors: never[]; warnings: never[] }[] }>();
    saleService.syncOfflineSales.mockReturnValue(response$);

    const syncRun = service.syncForShop('shop-1', 'device-1');
    const retryRun = service.retryQueuedSale('shop-1', 'device-1', 'sale-1');
    response$.next({ results: [{ clientSaleId: 'sale-1', status: 'created', saleId: 'server-1', invoiceNumber: 'INV-sale-1', errors: [], warnings: [] }] });
    response$.complete();
    await Promise.all([syncRun, retryRun]);

    expect(queueDb.getQueuedSale).not.toHaveBeenCalled();
    expect(saleService.syncOfflineSales).toHaveBeenCalledTimes(1);
  });

  it('applies each backend per-sale result independently', async () => {
    const service = setup();
    queueDb.getPendingSales.mockResolvedValue([makeRecord('created'), makeRecord('duplicate'), makeRecord('warning'), makeRecord('review'), makeRecord('failed')]);
    saleService.syncOfflineSales.mockReturnValue(of({
      results: [
        { clientSaleId: 'created', status: 'created', saleId: 'server-1', invoiceNumber: 'INV-created', errors: [], warnings: [] },
        { clientSaleId: 'duplicate', status: 'duplicate', saleId: 'server-duplicate', invoiceNumber: 'INV-duplicate', errors: [], warnings: [] },
        { clientSaleId: 'warning', status: 'SyncedWithWarnings', saleId: 'server-2', invoiceNumber: 'INV-warning', errors: [], warnings: ['price changed'] },
        { clientSaleId: 'review', status: 'NeedsReview', saleId: null, invoiceNumber: 'INV-review', errors: [{ code: 'CONFLICT', message: 'Invoice conflict' }], warnings: [] },
        { clientSaleId: 'failed', status: 'failed', saleId: null, invoiceNumber: 'INV-failed', errors: [{ code: 'RETRY', message: 'Retry later' }], warnings: [] },
      ],
    }));

    await service.syncForShop('shop-1', 'device-1');

    expect(queueDb.applySyncResult).toHaveBeenCalledWith('shop-1', 'device-1', 'created', expect.objectContaining({ status: 'Synced', serverSaleId: 'server-1' }));
    expect(queueDb.applySyncResult).toHaveBeenCalledWith('shop-1', 'device-1', 'duplicate', expect.objectContaining({ status: 'Synced', serverSaleId: 'server-duplicate' }));
    expect(queueDb.applySyncResult).toHaveBeenCalledWith('shop-1', 'device-1', 'warning', expect.objectContaining({ status: 'SyncedWithWarnings', warnings: ['price changed'] }));
    expect(queueDb.applySyncResult).toHaveBeenCalledWith('shop-1', 'device-1', 'review', expect.objectContaining({ status: 'NeedsReview', errorCode: 'CONFLICT' }));
    expect(queueDb.applySyncResult).toHaveBeenCalledWith('shop-1', 'device-1', 'failed', expect.objectContaining({ status: 'Failed', errorCode: 'RETRY' }));
  });

  it('passes frozen discount override and rule metadata to the backend sync request', async () => {
    const service = setup();
    const baseRecord = makeRecord('discounted');
    const discountedRecord = makeRecord('discounted', {
      payload: {
        ...baseRecord.payload,
        pricing: {
          ...baseRecord.payload.pricing,
          saleDiscountOverrideType: 1,
          saleDiscountOverrideValue: 5,
          configuredSaleRuleId: 'sale-rule-1',
          configuredSaleRuleType: 'SaleThresholdPercentage',
          configuredSaleRulePercentage: 10,
          configuredSaleRuleThresholdAmount: 500,
          lines: [{
            ...baseRecord.payload.pricing.lines[0],
            configuredRuleId: 'batch-rule-1',
            configuredRulePercentage: 7,
            itemDiscountOverrideType: 2,
            itemDiscountOverrideValue: 12,
          }],
        },
      },
    });
    queueDb.getPendingSales.mockResolvedValue([discountedRecord]);
    queueDb.markSyncInProgress.mockResolvedValueOnce({
      ...discountedRecord,
      status: 'Syncing',
    });
    saleService.syncOfflineSales.mockReturnValue(of({
      results: [{ clientSaleId: 'discounted', status: 'created', saleId: 'server-1', invoiceNumber: 'INV-discounted', errors: [], warnings: [] }],
    }));

    await service.syncForShop('shop-1', 'device-1');

    expect(saleService.syncOfflineSales.mock.calls[0][0].sales[0]).toMatchObject({
      saleDiscountOverrideType: 1,
      saleDiscountOverrideValue: 5,
      configuredSaleRuleId: 'sale-rule-1',
      configuredSaleRuleType: 'SaleThresholdPercentage',
      configuredSaleRulePercentage: 10,
      configuredSaleRuleThresholdAmount: 500,
      items: [expect.objectContaining({
        configuredBatchRuleId: 'batch-rule-1',
        configuredBatchRulePercentage: 7,
        itemDiscountOverrideType: 2,
        itemDiscountOverrideValue: 12,
      })],
    });
  });

  it('does not sync records from other shops on active-shop sync', async () => {
    const service = setup();

    await service.syncActiveShop();

    expect(queueDb.getPendingSales).toHaveBeenCalledWith('shop-1', 'device-1');
    expect(queueDb.getPendingSales).not.toHaveBeenCalledWith('shop-2', expect.any(String));
  });

  it('manual retry syncs pending and failed records but not needs-review records', async () => {
    const service = setup();
    queueDb.getRetryableSales.mockResolvedValue([
      makeRecord('pending', { status: 'Pending' }),
      makeRecord('failed', { status: 'Failed' }),
    ]);
    saleService.syncOfflineSales.mockImplementation((request) =>
      of({ results: request.sales.map((sale) => ({ clientSaleId: sale.clientSaleId, status: 'Synced', saleId: sale.clientSaleId, invoiceNumber: sale.invoiceNumber, errors: [], warnings: [] })) }),
    );

    await service.retryActiveShop();

    expect(queueDb.getRetryableSales).toHaveBeenCalledWith('shop-1', 'device-1');
    expect(saleService.syncOfflineSales.mock.calls[0][0].sales.map((sale) => sale.clientSaleId)).toEqual(['pending', 'failed']);

    queueDb.getQueuedSale.mockResolvedValue(makeRecord('review', { status: 'NeedsReview' }));
    const retryOne = await service.retryQueuedSale('shop-1', 'device-1', 'review');

    expect(retryOne.skippedReason).toBe('NOT_RETRYABLE');
  });

  it('refreshes visible counts for pending, syncing, failed, warning, and needs-review statuses', async () => {
    const service = setup();
    queueDb.getStatusCounts.mockResolvedValue({
      Pending: 2,
      Syncing: 1,
      Synced: 7,
      SyncedWithWarnings: 3,
      NeedsReview: 4,
      Failed: 5,
    });

    await service.refreshActiveStatusCounts();

    expect(service.visibleCounts()).toEqual({
      pending: 2,
      syncing: 1,
      failed: 5,
      warning: 3,
      needsReview: 4,
      totalVisible: 15,
    });
  });

  it('startup cleanup runs before non-blocking startup sync when API is reachable', async () => {
    const service = setup();
    queueDb.getPendingSales.mockResolvedValue([makeRecord('sale-1')]);

    service.runStartupSync();
    await flushMicrotasks();

    expect(queueDb.deleteOldSyncedRecords).toHaveBeenCalled();
    expect(networkStatus.checkConnectivity).toHaveBeenCalled();
    expect(saleService.syncOfflineSales).toHaveBeenCalled();
  });

  it('auto watchers trigger sync when API returns and when active shop changes', async () => {
    canReachApi.set(false);
    const service = setup();
    const syncSpy = vi.spyOn(service, 'syncActiveShop').mockResolvedValue({
      attemptedCount: 0,
      syncedCount: 0,
      warningCount: 0,
      needsReviewCount: 0,
      failedCount: 0,
    });

    service.startAutoSyncWatchers();
    flushEffects();

    canReachApi.set(true);
    flushEffects();
    activeSession.set(makeSession('shop-2'));
    flushEffects();

    expect(syncSpy).toHaveBeenCalledTimes(2);
  });

  async function flushMicrotasks(): Promise<void> {
    for (let i = 0; i < 10; i++) {
      await Promise.resolve();
    }
  }

  function flushEffects(): void {
    (TestBed as unknown as { flushEffects: () => void }).flushEffects();
  }

  function makeSession(activeShopId: string): AuthSession {
    return {
      accessToken: 'token',
      refreshToken: 'refresh',
      accessTokenExpiresAt: '2099-01-01T00:00:00.000Z',
      refreshTokenExpiresAt: '2099-01-01T00:00:00.000Z',
      rememberMe: true,
      user: {
        id: 'user-1',
        email: 'a@b.com',
        phoneNumber: null,
        firstName: 'Test',
        lastName: 'User',
        language: 'en-IN',
      },
      activeShopId,
      shops: [{ shopId: activeShopId, shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null }],
    };
  }

  function makeRecord(
    clientSaleId: string,
    overrides: Partial<OfflineQueuedSaleRecord> = {},
  ): OfflineQueuedSaleRecord {
    const shopId = overrides.shopId ?? 'shop-1';
    const deviceId = overrides.deviceId ?? 'device-1';
    const soldAt = overrides.soldAt ?? '2026-05-22T10:00:00.000Z';

    return {
      key: `${shopId}::${deviceId}::${clientSaleId}`,
      shopId,
      deviceId,
      clientSaleId,
      idempotencyKey: `idem-${clientSaleId}`,
      invoiceNumber: `INV-${clientSaleId}`,
      soldAt,
      status: overrides.status ?? 'Pending',
      warnings: [],
      errorCode: null,
      errorMessage: null,
      serverSaleId: null,
      createdAt: soldAt,
      updatedAt: soldAt,
      syncAttemptCount: 0,
      lastSyncAttemptAt: null,
      syncAttempts: [],
      payload: {
        clientSaleId,
        idempotencyKey: `idem-${clientSaleId}`,
        shopId,
        deviceId,
        invoiceNumber: `INV-${clientSaleId}`,
        soldAt,
        pricing: {
          lines: [{
            clientLineId: `line-${clientSaleId}`,
            inventoryBatchId: '11111111-1111-4111-8111-111111111111',
            itemId: 'item-1',
            barcode: 'BC-1',
            itemName: 'Item 1',
            batchNumber: 'B1',
            quantity: 1,
            salesPrice: 100,
            mrp: 100,
            costPrice: 80,
            taxRatePercent: 0,
            taxIncluded: false,
            hsnCode: null,
            preTaxAmount: 100,
            itemDiscountAmount: 0,
            saleDiscountAmount: 0,
            taxableAmount: 100,
            taxAmount: 0,
            lineTotal: 100,
            configuredRuleId: null,
            lineType: 'goods',
            serviceId: null,
          }],
          totals: {
            totalBeforeDiscount: 100,
            totalDiscount: 0,
            totalTax: 0,
            grandTotal: 100,
            paidAmount: 100,
            dueAmount: 0,
          },
        },
        paymentMethod: 1,
        customerId: null,
        customerName: null,
        customerPhone: null,
      },
      ...overrides,
    };
  }
});
