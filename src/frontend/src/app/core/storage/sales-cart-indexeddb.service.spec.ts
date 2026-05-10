import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { SalesCartIndexedDbService, type SalesCartDraftItem } from './sales-cart-indexeddb.service';

describe('SalesCartIndexedDbService', () => {
  let service: SalesCartIndexedDbService;

  beforeEach(() => {
    service = new SalesCartIndexedDbService();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('returns persisted cart rows when every row has inventoryBatchId', async () => {
    const cartRow: SalesCartDraftItem = {
      barcode: 'barcode-1',
      itemName: 'Oreo',
      batchNumber: 'B-01',
      inventoryBatchId: 'batch-1',
      quantity: 1,
      availableQuantity: 10,
      salesPrice: 50,
      mrp: 60,
      taxRatePercent: 18,
      taxIncluded: true,
      costPrice: 40,
    };

    stubIndexedDb({
      shopId: 'shop-1',
      items: [cartRow],
      updatedAt: new Date().toISOString(),
    });

    await expect(service.loadCart('shop-1', 60_000)).resolves.toEqual([cartRow]);
  });

  it('drops legacy cart rows without inventoryBatchId and clears storage', async () => {
    const legacyCartRow = {
      barcode: 'barcode-1',
      itemName: 'Oreo',
      batchNumber: 'B-01',
      quantity: 1,
      availableQuantity: 10,
      salesPrice: 50,
      mrp: 60,
      taxRatePercent: 18,
      taxIncluded: true,
      costPrice: 40,
    };

    const deleteSpy = vi.fn();
    stubIndexedDb({
      shopId: 'shop-1',
      items: [legacyCartRow],
      updatedAt: new Date().toISOString(),
    }, deleteSpy);

    await expect(service.loadCart('shop-1', 60_000)).resolves.toEqual([]);
    expect(deleteSpy).toHaveBeenCalledTimes(1);
  });

  function stubIndexedDb(record: unknown, deleteSpy = vi.fn()): void {
    const getRequest = {
      result: record,
      error: null,
      onsuccess: null as null | (() => void),
      onerror: null as null | (() => void),
    };

    const deleteRequest = {
      result: undefined,
      error: null,
      onsuccess: null as null | (() => void),
      onerror: null as null | (() => void),
    };

    const database = {
      objectStoreNames: {
        contains: vi.fn(() => true),
      },
      transaction: vi.fn((_storeName: string, _mode: IDBTransactionMode) => ({
        objectStore: () => ({
          get: () => {
            queueMicrotask(() => getRequest.onsuccess?.());
            return getRequest;
          },
          delete: () => {
            deleteSpy();
            queueMicrotask(() => deleteRequest.onsuccess?.());
            return deleteRequest;
          },
        }),
      })),
    };

    const openRequest = {
      result: database as unknown as IDBDatabase,
      error: null,
      onupgradeneeded: null as null | (() => void),
      onsuccess: null as null | (() => void),
      onerror: null as null | (() => void),
    };

    vi.stubGlobal('indexedDB', {
      open: vi.fn(() => {
        queueMicrotask(() => openRequest.onsuccess?.());
        return openRequest;
      }),
    });
  }
});
