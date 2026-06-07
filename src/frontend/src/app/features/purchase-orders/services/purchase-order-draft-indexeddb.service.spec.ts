import { TestBed } from '@angular/core/testing';
import { PurchaseOrderDraftIndexedDbService, type PurchaseOrderDraftRecord } from './purchase-order-draft-indexeddb.service';
import { vi } from 'vitest';

describe('PurchaseOrderDraftIndexedDbService', () => {
  let service: PurchaseOrderDraftIndexedDbService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [PurchaseOrderDraftIndexedDbService]
    });
    service = TestBed.inject(PurchaseOrderDraftIndexedDbService);
    stubIndexedDb();
  });

  it('loads and saves drafts correctly', async () => {
    const record: PurchaseOrderDraftRecord = {
      shopId: 'shop1',
      purchaseOrderId: 'po1',
      supplier: { id: 's1', name: 'Supplier A' },
      orderDate: '2026-06-01',
      expectedDeliveryDate: null,
      supplierReferenceNumber: null,
      notes: 'Notes here',
      lines: [{ itemId: 'item-1', description: 'Line A', expectedQuantity: 5, unitCost: 10 }],
      updatedAt: ''
    };

    await service.saveDraft(record);
    const loaded = await service.loadDraft('shop1');
    expect(loaded).toBeDefined();
    expect(loaded?.notes).toBe('Notes here');

    await service.clearDraft('shop1');
    const cleared = await service.loadDraft('shop1');
    expect(cleared).toBeNull();
  });

  function stubIndexedDb(): void {
    const store = new Map<string, unknown>();
    const objectStoreMock = {
      get: vi.fn((key: string) => {
        const req = {
          onsuccess: (): void => {},
          onerror: (): void => {},
          result: store.get(key) || null,
          error: null as unknown
        };
        setTimeout(() => {
          if (req.onsuccess) req.onsuccess();
        });
        return req;
      }),
      put: vi.fn((value: PurchaseOrderDraftRecord) => {
        store.set(value.shopId, value);
        const req = {
          onsuccess: (): void => {},
          onerror: (): void => {},
          error: null as unknown
        };
        setTimeout(() => {
          if (req.onsuccess) req.onsuccess();
        });
        return req;
      }),
      delete: vi.fn((key: string) => {
        store.delete(key);
        const req = {
          onsuccess: (): void => {},
          onerror: (): void => {},
          error: null as unknown
        };
        setTimeout(() => {
          if (req.onsuccess) req.onsuccess();
        });
        return req;
      })
    };

    const transactionMock = {
      objectStore: vi.fn(() => objectStoreMock)
    };

    const dbMock = {
      transaction: vi.fn(() => transactionMock),
      objectStoreNames: {
        contains: vi.fn(() => true)
      }
    };

    vi.stubGlobal('indexedDB', {
      open: vi.fn(() => {
        const req = {
          onsuccess: (): void => {},
          onerror: (): void => {},
          onupgradeneeded: (): void => {},
          result: dbMock,
          error: null as unknown
        };
        setTimeout(() => {
          if (req.onsuccess) req.onsuccess();
        });
        return req;
      })
    });
  }
});
