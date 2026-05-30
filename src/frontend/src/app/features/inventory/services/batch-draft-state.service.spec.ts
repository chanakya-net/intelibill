import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import {
  InventoryDraftIndexedDbService,
  InventoryInboundDraftRow,
} from '../../../core/storage/inventory-draft-indexeddb.service';
import { BatchDraftStateService } from './batch-draft-state.service';

describe('BatchDraftStateService', () => {
  const draftStorage = {
    loadRows: vi.fn<InventoryDraftIndexedDbService['loadRows']>().mockResolvedValue([]),
    saveRows: vi.fn<InventoryDraftIndexedDbService['saveRows']>().mockResolvedValue(undefined),
    clearRows: vi.fn<InventoryDraftIndexedDbService['clearRows']>().mockResolvedValue(undefined),
  };

  function setup() {
    TestBed.configureTestingModule({
      providers: [{ provide: InventoryDraftIndexedDbService, useValue: draftStorage }],
    });

    return TestBed.inject(BatchDraftStateService);
  }

  beforeEach(() => {
    draftStorage.loadRows.mockReset();
    draftStorage.loadRows.mockResolvedValue([]);
    draftStorage.saveRows.mockReset();
    draftStorage.saveRows.mockResolvedValue(undefined);
    draftStorage.clearRows.mockReset();
    draftStorage.clearRows.mockResolvedValue(undefined);
  });

  it('loads draft rows into state', async () => {
    draftStorage.loadRows.mockResolvedValueOnce([
      {
        clientRowId: 'row-1',
        itemName: 'Milk',
        barcode: 'B001',
        itemDescription: null,
        uom: 'ltr',
        batchNumber: 'BN-1',
        quantity: 1,
        totalPurchaseCost: 42,
        mrp: 50,
        salesPrice: 48,
        taxRatePercent: 18,
        taxIncluded: true,
        purchaseTaxIncluded: true,
        hsnCode: null,
        expiryDate: null,
        manufacturingDate: null,
        supplierId: null,
        referenceNumber: null,
        notes: null,
        performedAt: new Date().toISOString(),
      } satisfies InventoryInboundDraftRow,
    ]);

    const service = setup();
    const rows = await service.loadDraftRows('shop-1');

    expect(draftStorage.loadRows).toHaveBeenCalledWith('shop-1');
    expect(rows).toHaveLength(1);
    expect(service.pendingRows()).toHaveLength(1);
    expect(service.loadingDraft()).toBe(false);
  });

  it('adds a row and persists the full draft set', async () => {
    const service = setup();

    await service.addRow('shop-1', {
      clientRowId: 'row-1',
      itemName: 'Milk',
      barcode: 'B001',
      itemDescription: null,
      uom: 'ltr',
      batchNumber: 'BN-1',
      quantity: 1,
      totalPurchaseCost: 42,
      mrp: 50,
      salesPrice: 48,
      taxRatePercent: 18,
      taxIncluded: true,
      purchaseTaxIncluded: true,
      hsnCode: null,
      expiryDate: null,
      manufacturingDate: null,
      supplierId: null,
      referenceNumber: null,
      notes: null,
      performedAt: new Date().toISOString(),
    } satisfies InventoryInboundDraftRow);

    expect(draftStorage.saveRows).toHaveBeenCalledWith(
      'shop-1',
      expect.arrayContaining([expect.objectContaining({ clientRowId: 'row-1' })]),
    );
    expect(service.pendingRows()).toHaveLength(1);
  });

  it('removes the final row by clearing storage', async () => {
    const service = setup();
    service.pendingRows.set([
      {
        clientRowId: 'row-1',
        itemName: 'Milk',
        barcode: 'B001',
        itemDescription: null,
        uom: 'ltr',
        batchNumber: 'BN-1',
        quantity: 1,
        totalPurchaseCost: 42,
        mrp: 50,
        salesPrice: 48,
        taxRatePercent: 18,
        taxIncluded: true,
        purchaseTaxIncluded: true,
        hsnCode: null,
        expiryDate: null,
        manufacturingDate: null,
        supplierId: null,
        referenceNumber: null,
        notes: null,
        performedAt: new Date().toISOString(),
      } satisfies InventoryInboundDraftRow,
    ]);

    await service.removeRow('shop-1', 'row-1');

    expect(draftStorage.clearRows).toHaveBeenCalledWith('shop-1');
    expect(service.pendingRows()).toHaveLength(0);
  });

  it('increments quantity for a scanned barcode', async () => {
    const service = setup();
    service.pendingRows.set([
      {
        clientRowId: 'row-1',
        itemName: 'Milk',
        barcode: 'B001',
        itemDescription: null,
        uom: 'ltr',
        batchNumber: 'BN-1',
        quantity: 1,
        totalPurchaseCost: 42,
        mrp: 50,
        salesPrice: 48,
        taxRatePercent: 18,
        taxIncluded: true,
        purchaseTaxIncluded: true,
        hsnCode: null,
        expiryDate: null,
        manufacturingDate: null,
        supplierId: null,
        referenceNumber: null,
        notes: null,
        performedAt: new Date().toISOString(),
      } satisfies InventoryInboundDraftRow,
    ]);

    const updated = await service.incrementRowQuantity('shop-1', 'B001');

    expect(updated?.quantity).toBe(2);
    expect(draftStorage.saveRows).toHaveBeenCalledWith(
      'shop-1',
      expect.arrayContaining([expect.objectContaining({ quantity: 2 })]),
    );
  });
});
