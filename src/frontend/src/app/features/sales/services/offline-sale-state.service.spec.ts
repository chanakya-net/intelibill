import { disabledTillProviders } from '../../../../tests/tills/disabled-till.providers';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthSession } from '../../../core/auth/auth.models';
import { InvoiceLeaseIndexedDbService } from '../../../core/storage/invoice-lease-indexeddb.service';
import { AuthService } from '../../../core/auth/auth.service';
import { OfflineSalesDeviceSettingsStorage } from '../../../core/storage/offline-sales-device-settings.storage';
import { OfflineSalesSnapshotIndexedDbService } from '../../../core/storage/offline-sales-snapshot-indexeddb.service';
import { OfflineSaleFinalizationService } from './offline-sale-finalization.service';
import { OfflineSalesQueueSyncService } from './offline-sales-queue-sync.service';
import {
  OfflineSubmitRequest,
  OfflineSaleSubmitResult,
  OfflineSaleStateService,
} from './offline-sale-state.service';

describe('OfflineSaleStateService', () => {
  const activeSession = {
    activeShopId: 'shop-1',
  } as unknown as AuthSession;

  const settingsStorage = {
    loadSettings: vi.fn<OfflineSalesDeviceSettingsStorage['loadSettings']>(),
    updateSettings: vi.fn<OfflineSalesDeviceSettingsStorage['updateSettings']>(),
  };

  const snapshotDb = {
    getUsableBatches: vi.fn<OfflineSalesSnapshotIndexedDbService['getUsableBatches']>(),
    getUsableServices: vi.fn<OfflineSalesSnapshotIndexedDbService['getUsableServices']>(),
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
      lines: [
        {
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
        },
      ],
    };
  }

  const leaseDb = { loadLease: vi.fn<InvoiceLeaseIndexedDbService['loadLease']>() };

  function setup() {
    TestBed.configureTestingModule({
      providers: [
        ...disabledTillProviders(),
        OfflineSaleStateService,
        { provide: AuthService, useValue: authService },
        { provide: OfflineSalesDeviceSettingsStorage, useValue: settingsStorage },
        { provide: OfflineSalesSnapshotIndexedDbService, useValue: snapshotDb },
        { provide: OfflineSalesQueueSyncService, useValue: queueSync },
        { provide: OfflineSaleFinalizationService, useValue: finalization },
        { provide: InvoiceLeaseIndexedDbService, useValue: leaseDb },
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
    snapshotDb.getUsableBatches.mockResolvedValue([{ batchId: 'batch-1', itemId: 'item-1', itemName: 'Milk', barcode: 'x', batchNumber: 'B', quantity: 10,
      uom: 'kg', costPrice: 40, mrp: 60, salesPrice: 50, taxRatePercent: 0, taxIncluded: false, purchaseTaxIncluded: false,
      expiryEvidenceId: 'proof', effectivePolicy: { version: 2, mode: 'Ignore', expiryRequired: false, sellThroughExpiryDate: true, warningDays: 7, overrideAllowed: false },
      physicalQuantity: 10, quarantinedQuantity: 0, availableQuantity: 10, batchVersion: 8, expiryCapturedAt: new Date(Date.now() - 1000).toISOString(), expiryEvidenceExpiresAt: new Date(Date.now() + 86400000).toISOString() }]);
    snapshotDb.getUsableSnapshotInfo.mockResolvedValue({
      snapshotId: 'snapshot-1', schemaVersion: 4, businessTimeZoneId: 'Asia/Kolkata', catalogVersion: 3,
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
    leaseDb.loadLease.mockResolvedValue({ sellerProfile: { seriesCode: 'A' } } as never);
  });

  it('reevaluates a retained cart on the current shop day and refuses offline warning approval', async () => {
    snapshotDb.getUsableDiscountRules.mockResolvedValue([]);
    snapshotDb.getUsableBatches.mockResolvedValue([{ batchId: 'batch-1', itemId: 'item-1', barcode: 'x', itemName: 'Milk', batchNumber: 'B', quantity: 10,
      uom: 'kg', costPrice: 40, salesPrice: 50, mrp: 60, taxRatePercent: 0, taxIncluded: false, purchaseTaxIncluded: false,
      expiryDate: '2020-01-01', expiryEvidenceId: 'proof', effectivePolicy: { version: 2, mode: 'Warn', expiryRequired: true, sellThroughExpiryDate: true, warningDays: 7, overrideAllowed: true },
      physicalQuantity: 10, quarantinedQuantity: 3, availableQuantity: 7, batchVersion: 8, expiryCapturedAt: new Date(Date.now() - 1000).toISOString(), expiryEvidenceExpiresAt: new Date(Date.now() + 86400000).toISOString() }]);
    const service = setup();
    const preview = await service.buildOfflinePreview([{ clientLineKey: 'line-1', inventoryBatchId: 'batch-1', barcode: 'x', itemName: 'Milk', batchNumber: 'B', quantity: 1,
      availableQuantity: 10, salesPrice: 50, mrp: 60, costPrice: 40, taxRatePercent: 0, taxIncluded: false, itemDiscountType: 0, itemDiscountValue: 0, hsnCode: null, uom: 'kg', quantityScale: 3, documentUnit: 'KGS' }],
      { paymentMethod: 1, paidAmount: 50, customerId: null, customerName: null, customerPhone: null, saleDiscountType: 0, saleDiscountValue: 0 });
    expect(preview.expiryDecisions?.[0].decision).toMatchObject({ allowed: false, availableQuantity: 0, requiresApproval: true });
  });

  it('offers midpoint-safe item ceilings consistent with issuance', async () => {
    snapshotDb.getUsableDiscountRules.mockResolvedValue([]);
    const service = setup();
    const preview = await service.buildOfflinePreview(
      [
        {
          clientLineKey: 'mid',
          inventoryBatchId: 'batch-1',
          barcode: 'x',
          itemName: 'Midpoint',
          batchNumber: 'B',
          quantity: 0.5,
          availableQuantity: 10,
          salesPrice: 4,
          mrp: 4,
          costPrice: 2.01,
          taxRatePercent: 0,
          taxIncluded: false,
          itemDiscountType: 0,
          itemDiscountValue: 0,
          hsnCode: null,
          uom: 'KG',
          quantityScale: 3,
          documentUnit: 'KGS',
        },
      ],
      {
        paymentMethod: 1,
        paidAmount: 2,
        customerId: null,
        customerName: null,
        customerPhone: null,
        saleDiscountType: 0,
        saleDiscountValue: 0,
      },
    );
    expect(preview!.lines[0].economics?.economicCost).toBe(1.01);
    expect(preview!.lines[0].maxAllowedItemDiscountFlat).toBe(0.99);
    expect(preview!.lines[0].maxAllowedItemDiscountPercent).toBe(49.5);
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
      [
        {
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
          uom: 'PCS',
          quantityScale: 0,
          documentUnit: 'NOS',
        },
      ],
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

  it('sells a schema4 batch without optional unit precision as whole units printed NOS', async () => {
    const batchRow = {
      expiryEvidenceId: 'proof-1', effectivePolicy: { version: 2, mode: 'Ignore' as const, expiryRequired: false, sellThroughExpiryDate: true, warningDays: 7, overrideAllowed: false },
      physicalQuantity: 10, quarantinedQuantity: 0, availableQuantity: 10, batchVersion: 8,
      expiryCapturedAt: new Date(Date.now() - 1000).toISOString(), expiryEvidenceExpiresAt: new Date(Date.now() + 86400000).toISOString(),
      batchId: 'batch-old',
      itemId: 'item-1',
      itemName: 'Rice',
      barcode: 'RICE',
      uom: 'kg',
      batchNumber: 'B1',
      quantity: 10,
      costPrice: 40,
      mrp: 60,
      salesPrice: 50,
      taxRatePercent: 5,
      taxIncluded: false,
      purchaseTaxIncluded: false,
    };
    snapshotDb.getUsableBatches.mockResolvedValue([
      batchRow,
      {
        ...batchRow,
        batchId: 'batch-new',
        batchNumber: 'B2',
        quantityScale: 3,
        documentUnit: 'KGS',
      },
    ]);
    snapshotDb.getUsableServices.mockResolvedValue([]);

    const service = setup();
    const results = await service.searchOfflineCatalog('rice');

    expect(results.find((row) => row.inventoryBatchId === 'batch-old')).toMatchObject({
      uom: 'kg',
      quantityScale: 0,
      documentUnit: 'NOS',
    });
    expect(results.find((row) => row.inventoryBatchId === 'batch-new')).toMatchObject({
      uom: 'kg',
      quantityScale: 3,
      documentUnit: 'KGS',
    });
  });

  it('answers a part quantity an old snapshot cannot sell with its own message', async () => {
    snapshotDb.getUsableCustomers.mockResolvedValue([]);
    finalization.finalizeAndQueue.mockResolvedValue({
      ok: false,
      reason: 'QUANTITY_PRECISION_EXCEEDED',
    });

    const service = setup();
    await service.refreshSnapshot();
    const result = await service.submitOfflineSale(buildSubmitPayload(null));

    expect(result).toEqual({ ok: false, errorKey: 'sales.newSale.offline.blockQuantityPrecision' });
  });

  it('submits a sale when payload is valid', async () => {
    snapshotDb.getUsableCustomers.mockResolvedValue([
      { customerId: 'customer-1', name: 'Alice', phoneNumber: '9999999999' },
    ]);
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
    const result: OfflineSaleSubmitResult = await service.submitOfflineSale(
      buildSubmitPayload('customer-1'),
    );

    expect(result.ok).toBe(true);
    const confirmation = result.ok ? result.confirmation : null;
    expect(confirmation?.invoiceNumber).toBe('INV-1');
    expect(service.offlineInvoiceRemaining()).toBe(4);
    expect(finalization.finalizeAndQueue).toHaveBeenCalledTimes(1);
  });

  it('returns auth-grace error when auth grace check fails', async () => {
    authService.canUseOfflineSalesAuthGrace.mockResolvedValue(false);
    snapshotDb.getUsableCustomers.mockResolvedValue([
      { customerId: 'customer-1', name: 'Alice', phoneNumber: '9999999999' },
    ]);
    finalization.finalizeAndQueue.mockResolvedValue({
      ok: false,
      reason: 'INVOICE_UNAVAILABLE',
    } as never);

    const service = setup();
    const result = await service.submitOfflineSale(buildSubmitPayload('customer-1'));

    expect(result.ok).toBe(false);
    expect(result).toEqual({ ok: false, errorKey: 'sales.newSale.offline.blockAuthGraceInvalid' });
    expect(finalization.finalizeAndQueue).not.toHaveBeenCalled();
  });

  it.each([
    [
      { ok: false, reason: 'SELLER_PROFILE_UNAVAILABLE' },
      'sales.newSale.offline.blockSellerProfileUnavailable',
    ],
    [
      { ok: false, reason: 'CUSTOMER_UNAVAILABLE' },
      'sales.newSale.offline.blockCustomerUnavailable',
    ],
    [
      { ok: false, reason: 'DOCUMENT_UNAVAILABLE', errorCode: 'Invoice.HsnRequired' },
      'errors.invoice.hsnRequired',
    ],
    [
      { ok: false, reason: 'DOCUMENT_UNAVAILABLE', errorCode: 'Invoice.Unknown' },
      'sales.newSale.offline.blockDocumentUnavailable',
    ],
  ])('explains a sale the device cannot issue (%o)', async (refusal, errorKey) => {
    snapshotDb.getUsableCustomers.mockResolvedValue([
      { customerId: 'customer-1', name: 'Alice', phoneNumber: '9999999999' },
    ]);
    finalization.finalizeAndQueue.mockResolvedValue(refusal as never);

    const service = setup();
    const result = await service.submitOfflineSale(buildSubmitPayload('customer-1'));

    expect(result).toEqual({ ok: false, errorKey });
  });

  it('flags a stored lease that froze no seller profile, so the device is re-enabled while still online', async () => {
    leaseDb.loadLease.mockResolvedValue({ leaseId: 'lease-1', sellerProfile: undefined } as never);
    const service = setup();

    await service.refreshSnapshot();

    expect(service.offlineLeaseNeedsRenewal()).toBe(true);
  });

  it('does not flag a lease that carries its seller profile, or a device without offline billing', async () => {
    const service = setup();
    await service.refreshSnapshot();
    expect(service.offlineLeaseNeedsRenewal()).toBe(false);

    settingsStorage.loadSettings.mockReturnValue({ ...makeBaseSettings(), enabled: false });
    leaseDb.loadLease.mockResolvedValue({ leaseId: 'lease-1' } as never);
    await service.refreshSnapshot();
    expect(service.offlineLeaseNeedsRenewal()).toBe(false);
  });
});
