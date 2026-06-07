import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi, beforeEach } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { PurchaseOrderDraftIndexedDbService, type PurchaseOrderDraftRecord } from '../../../core/storage/purchase-order-draft-indexeddb.service';
import type { PurchaseOrderDetail } from './purchase-order.service';
import { PurchaseOrderDraftStateService } from './purchase-order-draft-state.service';

describe('PurchaseOrderDraftStateService', () => {
  const session = signal<{ activeShopId: string } | null>({ activeShopId: 'shop-1' });
  const records = new Map<string, PurchaseOrderDraftRecord>();
  const storage = {
    loadDraft: vi.fn(async (shopId: string) => records.get(shopId) ?? null),
    saveDraft: vi.fn(async (record: PurchaseOrderDraftRecord) => {
      records.set(record.shopId, record);
    }),
    clearDraft: vi.fn(async (shopId: string) => {
      records.delete(shopId);
    }),
  };

  let service: PurchaseOrderDraftStateService;

  beforeEach(() => {
    records.clear();
    storage.loadDraft.mockClear();
    storage.saveDraft.mockClear();
    storage.clearDraft.mockClear();
    session.set({ activeShopId: 'shop-1' });
    TestBed.configureTestingModule({
      providers: [
        PurchaseOrderDraftStateService,
        { provide: AuthService, useValue: { session } },
        { provide: PurchaseOrderDraftIndexedDbService, useValue: storage },
      ],
    });
    service = TestBed.inject(PurchaseOrderDraftStateService);
  });

  it('merges duplicate catalog item rows by item identity', async () => {
    await service.addOrMergeLine('shop-1', { itemId: 'item-1', description: 'Widget', expectedQuantity: 2, unitCost: 10 });
    const result = await service.addOrMergeLine('shop-1', { itemId: 'item-1', description: 'Renamed Widget', expectedQuantity: 3, unitCost: 12 });

    expect(result).toBe('merged');
    expect(service.lines()).toEqual([{ itemId: 'item-1', description: 'Widget', expectedQuantity: 5, unitCost: 12 }]);
  });

  it('does not merge different item ids that share a display name', async () => {
    await service.addOrMergeLine('shop-1', { itemId: 'item-1', description: 'Widget', expectedQuantity: 2, unitCost: 10 });
    const result = await service.addOrMergeLine('shop-1', { itemId: 'item-2', description: 'Widget', expectedQuantity: 1, unitCost: 11 });

    expect(result).toBe('added');
    expect(service.lines()).toHaveLength(2);
  });

  it('shapes payload with header fields and item-backed lines', async () => {
    await service.updateHeader('shop-1', {
      supplier: { id: 'supplier-1', name: 'Supplier A' },
      orderDate: '2026-06-01',
      expectedDeliveryDate: '2026-06-03',
      supplierReferenceNumber: 'SUP-7',
      notes: 'Draft notes',
    });
    await service.addOrMergeLine('shop-1', { itemId: 'item-1', description: 'Widget', expectedQuantity: 2, unitCost: 10 });

    expect(service.toPayload()).toEqual({
      supplierId: 'supplier-1',
      orderDate: '2026-06-01',
      expectedDeliveryDate: '2026-06-03',
      supplierReferenceNumber: 'SUP-7',
      notes: 'Draft notes',
      lines: [{ itemId: 'item-1', description: 'Widget', expectedQuantity: 2, unitCost: 10 }],
    });
  });

  it('resets in-memory draft on active shop change', async () => {
    records.set('shop-2', {
      shopId: 'shop-2',
      purchaseOrderId: null,
      supplier: null,
      orderDate: null,
      expectedDeliveryDate: null,
      supplierReferenceNumber: null,
      notes: 'Other shop',
      lines: [{ itemId: 'item-2', description: 'Other', expectedQuantity: 1, unitCost: 5 }],
      updatedAt: '2026-06-01T00:00:00Z',
    });
    await service.addOrMergeLine('shop-1', { itemId: 'item-1', description: 'Widget', expectedQuantity: 2, unitCost: 10 });

    session.set({ activeShopId: 'shop-2' });
    await new Promise((resolve) => setTimeout(resolve));

    expect(service.header().notes).toBeNull();
    expect(service.lines()).toEqual([]);
  });

  it('clears a saved draft when it belongs to a different purchase order than the edit route', async () => {
    records.set('shop-1', {
      shopId: 'shop-1',
      purchaseOrderId: 'po-2',
      supplier: null,
      orderDate: null,
      expectedDeliveryDate: null,
      supplierReferenceNumber: null,
      notes: 'Wrong draft',
      lines: [{ itemId: 'item-2', description: 'Other', expectedQuantity: 1, unitCost: 5 }],
      updatedAt: '2026-06-01T00:00:00Z',
    });

    const result = await service.loadDraft('shop-1', 'po-1');

    expect(result).toBeNull();
    expect(service.header().purchaseOrderId).toBeNull();
    expect(service.lines()).toEqual([]);
    expect(storage.clearDraft).toHaveBeenCalledWith('shop-1');
  });

  it('does not persist server hydration as a restored local draft', async () => {
    const firstServerDraft = makeDetail('po-1', 'server v1');
    const secondServerDraft = makeDetail('po-1', 'server v2');

    service.replaceFromServer(firstServerDraft);

    expect(storage.saveDraft).not.toHaveBeenCalled();
    expect(service.hasRestoredLocalDraft()).toBe(false);

    service.replaceFromServer(secondServerDraft);

    expect(service.header().notes).toBe('server v2');
    expect(records.has('shop-1')).toBe(false);
  });
});

function makeDetail(purchaseOrderId: string, notes: string): PurchaseOrderDetail {
  return {
    purchaseOrderId,
    purchaseOrderNumber: 'PO-2026-000001',
    status: 'Draft',
    supplierId: null,
    orderDate: null,
    expectedDeliveryDate: null,
    supplierReferenceNumber: null,
    notes,
    lines: [],
    expectedTotal: 0,
    createdAt: '2026-06-01T00:00:00Z',
  };
}
