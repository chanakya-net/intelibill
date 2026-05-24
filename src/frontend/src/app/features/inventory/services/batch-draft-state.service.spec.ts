import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { describe, it, expect, beforeEach, vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { InventoryDraftIndexedDbService } from '../../../core/storage/inventory-draft-indexeddb.service';
import { BatchDraftStateService } from './batch-draft-state.service';

describe('BatchDraftStateService', () => {
  const draftStorage = {
    loadRows: vi.fn(async () => []),
    saveRows: vi.fn(async () => undefined),
    clearRows: vi.fn(async () => undefined),
  };

  const authService = {
    session: signal({
      accessToken: 'token',
      refreshToken: 'refresh',
      accessTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
      refreshTokenExpiresAt: new Date(Date.now() + 120_000).toISOString(),
      rememberMe: true,
      user: { id: 'u1', email: 'u@test.com', phoneNumber: null, firstName: 'U', lastName: 'One' },
      activeShopId: 'shop-1',
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null }],
    }),
  };

  function makeRow(id: string, barcode = 'BC-001') {
    return {
      clientRowId: id,
      itemName: `Item ${id}`,
      barcode,
      itemDescription: null,
      uom: 'unit',
      batchNumber: 'BN-001',
      quantity: 1,
      totalPurchaseCost: 10,
      mrp: 12,
      salesPrice: 11,
      taxRatePercent: 5,
      taxIncluded: true,
      hsnCode: null,
      expiryDate: null,
      manufacturingDate: null,
      supplierId: null,
      referenceNumber: null,
      notes: null,
      performedAt: new Date().toISOString(),
    };
  }

  function setup() {
    TestBed.configureTestingModule({
      providers: [
        { provide: InventoryDraftIndexedDbService, useValue: draftStorage },
        { provide: AuthService, useValue: authService },
      ],
    });
    return TestBed.inject(BatchDraftStateService);
  }

  beforeEach(() => {
    draftStorage.loadRows.mockClear();
    draftStorage.saveRows.mockClear();
    draftStorage.clearRows.mockClear();
    TestBed.resetTestingModule();
  });

  it('loadDraft sets pendingRows from storage and clears loadingDraft', async () => {
    const rows = [makeRow('r1'), makeRow('r2')];
    draftStorage.loadRows.mockResolvedValueOnce(rows as any);

    const service = setup();
    expect(service.loadingDraft()).toBe(false);

    const promise = service.loadDraft('shop-1');
    expect(service.loadingDraft()).toBe(true);

    await promise;

    expect(service.loadingDraft()).toBe(false);
    expect(service.pendingRows()).toHaveLength(2);
    expect(service.pendingRows()[0].clientRowId).toBe('r1');
    expect(draftStorage.loadRows).toHaveBeenCalledWith('shop-1');
  });

  it('loadDraft with empty storage leaves pendingRows empty', async () => {
    draftStorage.loadRows.mockResolvedValueOnce([]);
    const service = setup();

    await service.loadDraft('shop-1');

    expect(service.pendingRows()).toHaveLength(0);
  });

  it('saveDraftRow adds a new row and persists', async () => {
    const service = setup();
    const row = makeRow('r1');

    await service.saveDraftRow(row);

    expect(service.pendingRows()).toHaveLength(1);
    expect(service.pendingRows()[0].clientRowId).toBe('r1');
    expect(draftStorage.saveRows).toHaveBeenCalledWith('shop-1', expect.arrayContaining([row]));
  });

  it('saveDraftRow updates existing row when clientRowId matches', async () => {
    const service = setup();
    const row = makeRow('r1');
    service.pendingRows.set([row]);

    const updated = { ...row, quantity: 5 };
    await service.saveDraftRow(updated);

    expect(service.pendingRows()).toHaveLength(1);
    expect(service.pendingRows()[0].quantity).toBe(5);
  });

  it('removeDraftRow removes row by clientRowId and persists', async () => {
    const service = setup();
    service.pendingRows.set([makeRow('r1'), makeRow('r2')]);

    await service.removeDraftRow('r1');

    expect(service.pendingRows()).toHaveLength(1);
    expect(service.pendingRows()[0].clientRowId).toBe('r2');
    expect(draftStorage.saveRows).toHaveBeenCalledWith('shop-1', [expect.objectContaining({ clientRowId: 'r2' })]);
  });

  it('removeDraftRow clears storage when last row is removed', async () => {
    const service = setup();
    service.pendingRows.set([makeRow('r1')]);

    await service.removeDraftRow('r1');

    expect(service.pendingRows()).toHaveLength(0);
    expect(draftStorage.clearRows).toHaveBeenCalledWith('shop-1');
  });

  it('clearDraft empties pendingRows and clears storage', async () => {
    const service = setup();
    service.pendingRows.set([makeRow('r1'), makeRow('r2')]);

    await service.clearDraft('shop-1');

    expect(service.pendingRows()).toHaveLength(0);
    expect(draftStorage.clearRows).toHaveBeenCalledWith('shop-1');
  });

  it('incrementRowQuantity returns null when barcode not found', async () => {
    const service = setup();
    service.pendingRows.set([makeRow('r1', 'BC-001')]);

    const result = await service.incrementRowQuantity('BC-999');

    expect(result).toBeNull();
    expect(service.pendingRows()[0].quantity).toBe(1);
  });

  it('incrementRowQuantity increments quantity and persists when barcode found', async () => {
    const service = setup();
    service.pendingRows.set([makeRow('r1', 'BC-001')]);

    const result = await service.incrementRowQuantity('BC-001');

    expect(result).not.toBeNull();
    expect(result!.quantity).toBe(2);
    expect(service.pendingRows()[0].quantity).toBe(2);
    expect(draftStorage.saveRows).toHaveBeenCalled();
  });

  it('replaceRows sets pendingRows and persists', async () => {
    const service = setup();
    service.pendingRows.set([makeRow('r1'), makeRow('r2')]);

    const newRows = [makeRow('r3')];
    await service.replaceRows('shop-1', newRows);

    expect(service.pendingRows()).toHaveLength(1);
    expect(service.pendingRows()[0].clientRowId).toBe('r3');
    expect(draftStorage.saveRows).toHaveBeenCalledWith('shop-1', newRows);
  });
});
