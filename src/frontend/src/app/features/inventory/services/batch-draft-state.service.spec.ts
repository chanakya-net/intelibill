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
      [],
    );
    expect(service.pendingRows()).toHaveLength(1);
  });

  it('removes the final row by storing an empty draft', async () => {
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

    expect(draftStorage.saveRows).toHaveBeenCalledWith('shop-1', [], []);
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
      [],
    );
  });

  describe('when another tab changed the stored draft', () => {
    const row = (id: string) => ({ clientRowId: id, itemName: 'Goods ' + id, barcode: id, quantity: 1 }) as InventoryInboundDraftRow;
    const [a, b, c] = ['A', 'B', 'C'].map(row);

    async function loaded() {
      draftStorage.loadDraft.mockResolvedValueOnce({ rows: [a, b], submission: null });
      const service = setup();
      await service.loadDraftRows(owner.shopId, owner);
      return service;
    }

    it('checks each change against the rows this tab last stored', async () => {
      const service = await loaded();

      expect(await service.removeRow(owner.shopId, 'A', owner)).toBe(true);
      expect(await service.addRow(owner.shopId, c, owner)).toBe(true);

      expect(draftStorage.saveRows.mock.calls).toEqual([
        [owner.shopId, [b], [a, b], owner],
        [owner.shopId, [b, c], [b], owner],
      ]);
    });

    it('shows the stored rows and the frozen save instead of overwriting them', async () => {
      const service = await loaded();
      draftStorage.saveRows.mockResolvedValueOnce({ rows: [b], submission: stored });

      expect(await service.removeRow(owner.shopId, 'B', owner)).toBe(false);

      expect(service.pendingRows()).toEqual([b]);
      expect(service.hasInterruptedSubmission()).toBe(true);
      expect(draftStorage.saveRows).toHaveBeenCalledTimes(1);
    });

    it('drops a change queued behind one that was refused', async () => {
      const service = await loaded();
      draftStorage.saveRows.mockResolvedValueOnce({ rows: [b], submission: null });

      const first = service.removeRow(owner.shopId, 'B', owner);
      const second = service.clearRows(owner.shopId, owner);

      expect(await Promise.all([first, second])).toEqual([false, false]);
      expect(service.pendingRows()).toEqual([b]);
      expect(draftStorage.saveRows).toHaveBeenCalledTimes(1);
    });

    it('adds a new row after the rows the other tab kept', async () => {
      const service = await loaded();
      draftStorage.saveRows.mockResolvedValueOnce({ rows: [b], submission: null });

      expect(await service.addRow(owner.shopId, c, owner)).toBe(true);

      expect(draftStorage.saveRows).toHaveBeenLastCalledWith(owner.shopId, [b, c], [b], owner);
      expect(service.pendingRows()).toEqual([b, c]);
    });

    it('keeps a new row out of a draft another tab froze', async () => {
      const service = await loaded();
      draftStorage.saveRows.mockResolvedValueOnce({ rows: [b], submission: stored });

      expect(await service.addRow(owner.shopId, c, owner)).toBe(false);

      expect(service.pendingRows()).toEqual([b]);
      expect(draftStorage.saveRows).toHaveBeenCalledTimes(1);
    });

    it('increments the scanned row again on the rows the other tab kept', async () => {
      const service = await loaded();
      draftStorage.saveRows.mockResolvedValueOnce({ rows: [{ ...b, quantity: 4 }], submission: null });

      const updated = await service.incrementRowQuantity(owner.shopId, 'B', owner);

      expect(updated?.quantity).toBe(5);
      expect(service.pendingRows()).toEqual([{ ...b, quantity: 5 }]);
    });

    it('freezes the draft only after a pending row change reached the device', async () => {
      const service = await loaded();
      let reachDevice!: () => void;
      draftStorage.saveRows.mockReturnValueOnce(new Promise<undefined>(resolve => { reachDevice = () => resolve(undefined); }));

      void service.removeRow(owner.shopId, 'A', owner);
      const frozen = service.prepareSubmission(owner, () => undefined);
      await Promise.resolve();
      expect(draftStorage.saveSubmission).not.toHaveBeenCalled();
      reachDevice();

      expect((await frozen)?.chunks[0].rows.map(saved => saved.clientRowId)).toEqual(['B']);
    });
  });
});
