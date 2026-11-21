import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import {
  InventoryDraftIndexedDbService,
  InventoryInboundDraftRow,
} from '../../../core/storage/inventory-draft-indexeddb.service';
import { BatchDraftStateService } from './batch-draft-state.service';

describe('BatchDraftStateService', () => {
  const owner = { userId: 'owner-1', shopId: 'shop-1' };
  const empty = { rows: [], submission: null };
  const stored = { shopId: owner.shopId, chunks: [{ operationKey: 'intake-x-0', rows: [] }], createdAt: '2026-11-01T00:00:00Z' };
  const draftStorage = {
    loadDraft: vi.fn<InventoryDraftIndexedDbService['loadDraft']>().mockResolvedValue(empty),
    saveRows: vi.fn<InventoryDraftIndexedDbService['saveRows']>().mockResolvedValue(undefined),
    clearRows: vi.fn<InventoryDraftIndexedDbService['clearRows']>().mockResolvedValue(undefined),
    saveSubmission: vi.fn<InventoryDraftIndexedDbService['saveSubmission']>().mockImplementation(async (_owner, _rows, freeze) => freeze()),
  };

  function setup() {
    TestBed.configureTestingModule({
      providers: [{ provide: InventoryDraftIndexedDbService, useValue: draftStorage }],
    });

    return TestBed.inject(BatchDraftStateService);
  }

  beforeEach(() => {
    draftStorage.loadDraft.mockReset();
    draftStorage.loadDraft.mockResolvedValue(empty);
    draftStorage.saveRows.mockReset();
    draftStorage.saveRows.mockResolvedValue(undefined);
    draftStorage.clearRows.mockReset();
    draftStorage.clearRows.mockResolvedValue(undefined);
    draftStorage.saveSubmission.mockReset();
    draftStorage.saveSubmission.mockImplementation(async (_owner, _rows, freeze) => freeze());
  });

  it('flags an interrupted submission when one is stored for the shop', async () => {
    draftStorage.loadDraft.mockResolvedValueOnce({ rows: [], submission: stored });

    const service = setup();
    await service.loadDraftRows('shop-1');

    expect(service.hasInterruptedSubmission()).toBe(true);
  });

  it('reuses the displayed submission instead of freezing the current rows again', async () => {
    draftStorage.loadDraft.mockResolvedValue({ rows: [], submission: stored });
    const service = setup();
    await service.loadDraftRows(owner.shopId, owner);

    const submission = await service.prepareSubmission(owner, () => undefined);

    expect(submission).toBe(stored);
    expect(draftStorage.saveSubmission).not.toHaveBeenCalled();
  });

  it('does not create a new manifest when another tab already settled the recovery', async () => {
    const service = setup();
    service.markSubmissionInterrupted(stored);
    const submission = await service.prepareSubmission(owner, () => undefined);

    expect(submission).toBeNull();
    expect(draftStorage.saveSubmission).not.toHaveBeenCalled();
    expect(service.hasInterruptedSubmission()).toBe(false);
  });

  it('loads draft rows into state', async () => {
    draftStorage.loadDraft.mockResolvedValueOnce({ rows: [
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
    ], submission: null });

    const service = setup();
    const rows = await service.loadDraftRows('shop-1');

    expect(draftStorage.loadDraft).toHaveBeenCalledWith('shop-1', undefined);
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
