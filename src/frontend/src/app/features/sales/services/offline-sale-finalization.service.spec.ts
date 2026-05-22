import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';

import { InvoiceLeaseIndexedDbService } from '../../../core/storage/invoice-lease-indexeddb.service';
import { OfflineSalesSnapshotIndexedDbService } from '../../../core/storage/offline-sales-snapshot-indexeddb.service';
import { OfflineFinalizeRequest, OfflineSaleFinalizationService } from './offline-sale-finalization.service';
import { OfflineSalesQueueIndexedDbService } from './offline-sales-queue-indexeddb.service';
import { OfflineSalesShadowStockIndexedDbService } from './offline-sales-shadow-stock-indexeddb.service';

describe('OfflineSaleFinalizationService', () => {
  function setup() {
    const snapshotDb = {
      getUsableSnapshotInfo: vi.fn().mockResolvedValue({ snapshotId: 'snap-1', completedAt: new Date().toISOString() }),
      getUsableBatches: vi.fn().mockResolvedValue([makeBatchSnapshot()]),
      getUsableDiscountRules: vi.fn().mockResolvedValue([]),
      getUsableCustomers: vi.fn().mockResolvedValue([{ customerId: 'cust-1' }]),
    };
    const leaseDb = {
      consumeNextInvoiceNumber: vi.fn().mockResolvedValue({ invoiceNumber: 'INV-1', lease: { nextNumber: 2 } }),
      rollbackConsumedInvoiceNumber: vi.fn().mockResolvedValue({ nextNumber: 1 }),
    };
    const queueDb = {
      savePendingSale: vi.fn().mockResolvedValue(undefined),
    };
    const shadowDb = {
      ensureQuantity: vi.fn().mockResolvedValue(10),
      reduceQuantity: vi.fn().mockResolvedValue(9),
    };

    TestBed.configureTestingModule({
      providers: [
        OfflineSaleFinalizationService,
        { provide: OfflineSalesSnapshotIndexedDbService, useValue: snapshotDb },
        { provide: InvoiceLeaseIndexedDbService, useValue: leaseDb },
        { provide: OfflineSalesQueueIndexedDbService, useValue: queueDb },
        { provide: OfflineSalesShadowStockIndexedDbService, useValue: shadowDb },
      ],
    });

    return {
      service: TestBed.inject(OfflineSaleFinalizationService),
      snapshotDb,
      leaseDb,
      queueDb,
      shadowDb,
    };
  }

  it('queues sale, consumes invoice, and reduces shadow stock', async () => {
    vi.spyOn(crypto, 'randomUUID').mockReturnValueOnce('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa').mockReturnValueOnce('11111111-1111-4111-8111-111111111111');
    const { service, leaseDb, queueDb, shadowDb } = setup();

    const result = await service.finalizeAndQueue(makeRequest());

    expect(result.ok).toBe(true);
    expect(leaseDb.consumeNextInvoiceNumber).toHaveBeenCalledWith('shop-1', 'device-1', '2026-27');
    expect(queueDb.savePendingSale).toHaveBeenCalledTimes(1);
    expect(leaseDb.rollbackConsumedInvoiceNumber).not.toHaveBeenCalled();
    expect(shadowDb.reduceQuantity).toHaveBeenCalledWith('shop-1', 'device-1', 'batch-1', 1);
  });

  it('freezes pricing and catalog values from cached batch snapshots', async () => {
    vi.spyOn(crypto, 'randomUUID').mockReturnValueOnce('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa').mockReturnValueOnce('11111111-1111-4111-8111-111111111111');
    const { service, queueDb } = setup();
    const baseRequest = makeRequest();
    const request: OfflineFinalizeRequest = {
      ...baseRequest,
      pricingInput: {
        ...baseRequest.pricingInput,
        lines: [
          {
            ...baseRequest.pricingInput.lines[0],
            clientLineId: 'client-line-1',
            itemId: 'caller-item',
            barcode: 'caller-barcode',
            itemName: 'Caller item',
            batchNumber: 'CALLER-BATCH',
            salesPrice: 1,
            mrp: 2,
            costPrice: 3,
            taxRatePercent: 99,
            taxIncluded: true,
            hsnCode: 'caller-hsn',
          },
        ],
      },
    };

    const result = await service.finalizeAndQueue(request);

    expect(result.ok).toBe(true);
    const saved = queueDb.savePendingSale.mock.calls[0][0];
    expect(saved.payload.pricing.lines[0]).toMatchObject({
      clientLineId: 'client-line-1',
      inventoryBatchId: 'batch-1',
      itemId: 'item-1',
      barcode: '111',
      itemName: 'A',
      batchNumber: 'B1',
      quantity: 1,
      salesPrice: 100,
      mrp: 100,
      costPrice: 80,
      taxRatePercent: 0,
      taxIncluded: false,
      hsnCode: null,
      preTaxAmount: 100,
      lineTotal: 100,
    });
  });

  it('blocks stale snapshots older than 48h', async () => {
    const { service, snapshotDb } = setup();
    snapshotDb.getUsableSnapshotInfo.mockResolvedValue({
      snapshotId: 'snap-1',
      completedAt: new Date(Date.now() - 49 * 60 * 60 * 1000).toISOString(),
    });

    const result = await service.finalizeAndQueue(makeRequest());
    expect(result).toEqual({ ok: false, reason: 'SNAPSHOT_STALE' });
  });

  it('blocks missing catalog item', async () => {
    const { service, snapshotDb } = setup();
    snapshotDb.getUsableBatches.mockResolvedValue([]);

    const result = await service.finalizeAndQueue(makeRequest());
    expect(result).toEqual({ ok: false, reason: 'MISSING_CATALOG_ITEM' });
  });

  it('blocks insufficient local shadow stock', async () => {
    const { service, shadowDb } = setup();
    shadowDb.ensureQuantity.mockResolvedValue(0);

    const result = await service.finalizeAndQueue(makeRequest());
    expect(result).toEqual({ ok: false, reason: 'INSUFFICIENT_SHADOW_STOCK' });
  });

  it('blocks when combined quantity for same batch exceeds shadow stock', async () => {
    const { service, shadowDb } = setup();
    shadowDb.ensureQuantity.mockResolvedValue(1);

    const baseRequest = makeRequest();
    const request: OfflineFinalizeRequest = {
      ...baseRequest,
      pricingInput: {
        ...baseRequest.pricingInput,
        lines: [
          baseRequest.pricingInput.lines[0],
          {
            ...baseRequest.pricingInput.lines[0],
            quantity: 1,
          },
        ],
      },
    };

    const result = await service.finalizeAndQueue(request);
    expect(result).toEqual({ ok: false, reason: 'INSUFFICIENT_SHADOW_STOCK' });
    expect(shadowDb.ensureQuantity).toHaveBeenCalledTimes(1);
  });

  it('requires cached customer when due amount exists', async () => {
    vi.spyOn(crypto, 'randomUUID').mockReturnValueOnce('11111111-1111-4111-8111-111111111111');
    const { service, snapshotDb } = setup();
    snapshotDb.getUsableCustomers.mockResolvedValue([]);

    const baseRequest = makeRequest();
    const request: OfflineFinalizeRequest = {
      ...baseRequest,
      pricingInput: {
        ...baseRequest.pricingInput,
        paidAmount: 0,
        customerId: 'missing',
      },
    };

    const result = await service.finalizeAndQueue(request);
    expect(result).toEqual({ ok: false, reason: 'MISSING_DUE_CUSTOMER' });
  });

  it('blocks when invoice is unavailable', async () => {
    const { service, leaseDb } = setup();
    leaseDb.consumeNextInvoiceNumber.mockRejectedValue(new Error('no lease'));

    const result = await service.finalizeAndQueue(makeRequest());
    expect(result).toEqual({ ok: false, reason: 'INVOICE_UNAVAILABLE' });
  });

  it('restores consumed invoice when queue persistence fails', async () => {
    vi.spyOn(crypto, 'randomUUID').mockReturnValueOnce('11111111-1111-4111-8111-111111111111');
    const { service, leaseDb, queueDb, shadowDb } = setup();
    const persistenceError = new Error('queue write failed');
    queueDb.savePendingSale.mockRejectedValue(persistenceError);

    await expect(service.finalizeAndQueue(makeRequest())).rejects.toBe(persistenceError);

    expect(leaseDb.consumeNextInvoiceNumber).toHaveBeenCalledWith('shop-1', 'device-1', '2026-27');
    expect(leaseDb.rollbackConsumedInvoiceNumber).toHaveBeenCalledWith('shop-1', 'device-1', '2026-27', 2);
    expect(shadowDb.reduceQuantity).not.toHaveBeenCalled();
  });

  function makeRequest(): OfflineFinalizeRequest {
    return {
      shopId: 'shop-1',
      deviceId: 'device-1',
      fiscalYear: '2026-27',
      maxSnapshotAgeMs: 48 * 60 * 60 * 1000,
      pricingInput: {
        soldAt: new Date().toISOString(),
        paymentMethod: 1,
        paidAmount: 100,
        customerId: 'cust-1' as string | null,
        customerName: 'A' as string | null,
        customerPhone: '9999999999' as string | null,
        saleDiscount: { type: 0 as const, value: 0 },
        rules: [],
        lines: [
          {
            inventoryBatchId: 'batch-1',
            itemId: 'item-1',
            barcode: '111',
            itemName: 'A',
            batchNumber: 'B1',
            quantity: 1,
            salesPrice: 100,
            mrp: 100,
            costPrice: 80,
            taxRatePercent: 0,
            taxIncluded: false,
            itemDiscount: { type: 0 as const, value: 0 },
            hsnCode: null as string | null,
          },
        ],
      },
    };
  }

  function makeBatchSnapshot() {
    return {
      batchId: 'batch-1',
      itemId: 'item-1',
      barcode: '111',
      itemName: 'A',
      batchNumber: 'B1',
      quantity: 10,
      salesPrice: 100,
      mrp: 100,
      costPrice: 80,
      taxRatePercent: 0,
      taxIncluded: false,
      hsnCode: null,
    };
  }
});
