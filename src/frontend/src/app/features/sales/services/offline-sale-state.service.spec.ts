import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthSession } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { OfflineSalesDeviceSettingsStorage } from '../../../core/storage/offline-sales-device-settings.storage';
import { OfflineSalesSnapshotIndexedDbService } from '../../../core/storage/offline-sales-snapshot-indexeddb.service';
import { OfflineSaleFinalizationService } from './offline-sale-finalization.service';
import { OfflineSalesQueueSyncService } from './offline-sales-queue-sync.service';
import { OfflineSubmitRequest, OfflineSaleSubmitResult, OfflineSaleStateService } from './offline-sale-state.service';

describe('OfflineSaleStateService', () => {
  const activeSession = {
    activeShopId: 'shop-1',
  } as unknown as AuthSession;

  const settingsStorage = {
    loadSettings: vi.fn<OfflineSalesDeviceSettingsStorage['loadSettings']>(),
    updateSettings: vi.fn<OfflineSalesDeviceSettingsStorage['updateSettings']>(),
  };

  const snapshotDb = {
    getUsableDiscountRules: vi.fn<OfflineSalesSnapshotIndexedDbService['getUsableDiscountRules']>(),
    getUsableCustomers: vi.fn<OfflineSalesSnapshotIndexedDbService['getUsableCustomers']>(),
    getUsableSnapshotInfo: vi.fn<OfflineSalesSnapshotIndexedDbService['getUsableSnapshotInfo']>(),
  };

  const queueSync = {
    refreshActiveStatusCounts: vi.fn<OfflineSalesQueueSyncService['refreshActiveStatusCounts']>(),
  };

  const finalization = {
    finalizeAndQueue: vi.fn<OfflineSaleFinalizationService['finalizeAndQueue']>(),
  };

  const authService = {
    session: vi.fn<AuthService['session']>(),
    canUseOfflineSalesAuthGrace: vi.fn<AuthService['canUseOfflineSalesAuthGrace']>(),
  };

  function makeBaseSettings() {
    return {
      shopId: 'shop-1',
      deviceId: 'device-1',
      label: 'Counter 1',
      enabled: true,
      enabledAt: '2026-05-22T00:00:00.000Z',
      enabledByUserId: 'user-1',
      enabledByUserName: 'Owner',
      lastCompleteSnapshotAt: '2026-05-22T00:00:00.000Z',
      lastApiVerifiedAt: '2026-05-22T00:00:00.000Z',
      lastSnapshotWarningMarker: null,
      lastReservedLease: {
        leaseId: 'lease-1',
        fiscalYear: '2026-27',
        remainingCount: 5,
        expiresAt: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
      },
    };
  }

  function buildSubmitPayload(customerId: string | null): OfflineSubmitRequest {
    return {
      paymentMethod: 1,
      paidAmount: 100,
      dueAmount: 0,
      totalAmount: 100,
      customerId,
      customerName: 'Alice',
      customerPhone: '9999999999',
      selectedCustomerId: customerId,
      saleDiscountType: 0,
      saleDiscountValue: 0,
      lines: [{
        clientLineId: 'line-1',
        inventoryBatchId: 'batch-1',
        itemId: 'item-1',
        barcode: '111',
        itemName: 'Item 1',
        batchNumber: 'B1',
        quantity: 1,
        salesPrice: 100,
        mrp: 100,
        costPrice: 80,
        taxRatePercent: 0,
        taxIncluded: false,
        itemDiscount: { type: 0, value: 0 },
        hsnCode: null,
      }],
    };
  }

  function setup() {
    TestBed.configureTestingModule({
      providers: [
        OfflineSaleStateService,
        { provide: AuthService, useValue: authService },
        { provide: OfflineSalesDeviceSettingsStorage, useValue: settingsStorage },
        { provide: OfflineSalesSnapshotIndexedDbService, useValue: snapshotDb },
        { provide: OfflineSalesQueueSyncService, useValue: queueSync },
        { provide: OfflineSaleFinalizationService, useValue: finalization },
      ],
    });

    return TestBed.inject(OfflineSaleStateService);
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
    authService.session.mockReturnValue(activeSession);
    authService.canUseOfflineSalesAuthGrace.mockResolvedValue(true);
    settingsStorage.loadSettings.mockReturnValue(makeBaseSettings());
    settingsStorage.updateSettings.mockImplementation((shopId, update) => {
      const current = makeBaseSettings();
      if (current.shopId !== shopId) {
        return null;
      }
      return update(current);
    });
    snapshotDb.getUsableSnapshotInfo.mockResolvedValue({
      snapshotId: 'snapshot-1',
      completedAt: new Date().toISOString(),
    });
    queueSync.refreshActiveStatusCounts.mockResolvedValue({
      pending: 1,
      syncing: 0,
      failed: 0,
      warning: 0,
      needsReview: 0,
      totalVisible: 1,
    });
    vi.clearAllMocks();
  });

  it('builds an offline preview from cart state', async () => {
    snapshotDb.getUsableDiscountRules.mockResolvedValue([
      {
        ruleId: 'rule-1',
        ruleType: 'BatchPercentage',
        name: 'Batch Rule',
        percentage: 10,
        inventoryBatchId: 'batch-1',
        belowCostConfirmed: false,
      },
    ]);

    const service = setup();
    const preview = await service.buildOfflinePreview(
      [{
        clientLineKey: 'line-1',
        inventoryBatchId: 'batch-1',
        barcode: '111',
        itemName: 'Item 1',
        batchNumber: 'B1',
        quantity: 1,
        availableQuantity: 10,
        salesPrice: 100,
        mrp: 110,
        costPrice: 80,
        taxRatePercent: 0,
        taxIncluded: false,
        itemDiscountType: 0,
        itemDiscountValue: 0,
        hsnCode: null,
      }],
      {
        paymentMethod: 1,
        paidAmount: 100,
        customerId: null,
        customerName: null,
        customerPhone: null,
        saleDiscountType: 0,
        saleDiscountValue: 0,
      },
    );

    expect(preview.totalAmount).toBe(90);
    expect(preview.configuredSaleRule).toBeNull();
    expect(preview.lines).toHaveLength(1);
    expect(preview.lines[0].configuredBatchRuleId).toBe('rule-1');
    expect(preview.lines[0].hasClientPriceMismatch).toBe(false);
  });

  it('submits a sale when payload is valid', async () => {
    snapshotDb.getUsableCustomers.mockResolvedValue([{ customerId: 'customer-1', name: 'Alice', phoneNumber: '9999999999' }]);
    finalization.finalizeAndQueue.mockResolvedValue({
      ok: true,
      payload: {
        clientSaleId: 'client-1',
        idempotencyKey: 'off-1',
        shopId: 'shop-1',
        deviceId: 'device-1',
        invoiceNumber: 'INV-1',
        soldAt: new Date().toISOString(),
        pricing: { totals: { grandTotal: 100 } } as any,
        paymentMethod: 1,
        customerId: 'customer-1',
        customerName: 'Alice',
        customerPhone: '9999999999',
      } as never,
      remainingInvoiceCount: 4,
    });

    const service = setup();
    const result: OfflineSaleSubmitResult = await service.submitOfflineSale(buildSubmitPayload('customer-1'));

    expect(result.ok).toBe(true);
    const confirmation = result.ok ? result.confirmation : null;
    expect(confirmation?.invoiceNumber).toBe('INV-1');
    expect(service.offlineInvoiceRemaining()).toBe(4);
    expect(finalization.finalizeAndQueue).toHaveBeenCalledTimes(1);
  });

  it('returns auth-grace error when auth grace check fails', async () => {
    authService.canUseOfflineSalesAuthGrace.mockResolvedValue(false);
    snapshotDb.getUsableCustomers.mockResolvedValue([{ customerId: 'customer-1', name: 'Alice', phoneNumber: '9999999999' }]);
    finalization.finalizeAndQueue.mockResolvedValue({ ok: false, reason: 'INVOICE_UNAVAILABLE' } as never);

    const service = setup();
    const result = await service.submitOfflineSale(buildSubmitPayload('customer-1'));

    expect(result.ok).toBe(false);
    expect(result).toEqual({ ok: false, errorKey: 'sales.newSale.offline.blockAuthGraceInvalid' });
    expect(finalization.finalizeAndQueue).not.toHaveBeenCalled();
  });
});
