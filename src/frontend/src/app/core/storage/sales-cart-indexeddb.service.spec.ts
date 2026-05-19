import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { migrateLegacyCartItem, SalesCartIndexedDbService, type SalesCartDraftItem } from './sales-cart-indexeddb.service';

describe('migrateLegacyCartItem', () => {
  it('returns null for null input', () => {
    expect(migrateLegacyCartItem(null)).toBeNull();
  });

  it('returns null for non-object input', () => {
    expect(migrateLegacyCartItem('string')).toBeNull();
    expect(migrateLegacyCartItem(42)).toBeNull();
  });

  it('returns null when inventoryBatchId is missing', () => {
    expect(migrateLegacyCartItem({ barcode: 'BC-1', itemName: 'Oreo' })).toBeNull();
  });

  it('preserves existing clientLineKey when present', () => {
    const item = {
      inventoryBatchId: 'batch-1',
      clientLineKey: 'existing-key',
      barcode: 'BC-1',
      itemName: 'Oreo',
      batchNumber: 'B-01',
      quantity: 2,
      availableQuantity: 10,
      salesPrice: 50,
      mrp: 60,
      taxRatePercent: 18,
      taxIncluded: true,
      costPrice: 40,
      itemDiscountType: 0,
      itemDiscountValue: 0,
      hsnCode: '0902',
    };
    const result = migrateLegacyCartItem(item);
    expect(result).not.toBeNull();
    expect(result!.clientLineKey).toBe('existing-key');
  });

  it('generates a UUID clientLineKey when missing', () => {
    const item = {
      inventoryBatchId: 'batch-1',
      barcode: 'BC-1',
      itemName: 'Oreo',
      batchNumber: 'B-01',
      quantity: 1,
      availableQuantity: 5,
      salesPrice: 50,
      mrp: 60,
      taxRatePercent: 18,
      taxIncluded: false,
      costPrice: 0,
      hsnCode: null,
    };
    const result = migrateLegacyCartItem(item);
    expect(result).not.toBeNull();
    expect(result!.clientLineKey).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
    );
  });

  it('defaults itemDiscountType and itemDiscountValue to 0 when missing', () => {
    const item = {
      inventoryBatchId: 'batch-1',
      barcode: 'BC-1',
      itemName: 'Oreo',
      batchNumber: 'B-01',
      quantity: 1,
      availableQuantity: 5,
      salesPrice: 50,
      mrp: 60,
      taxRatePercent: 0,
      taxIncluded: false,
      costPrice: 0,
      hsnCode: null,
    };
    const result = migrateLegacyCartItem(item);
    expect(result).not.toBeNull();
    expect(result!.itemDiscountType).toBe(0);
    expect(result!.itemDiscountValue).toBe(0);
  });

  it('defaults hsnCode to null when missing', () => {
    const item = {
      inventoryBatchId: 'batch-1',
      barcode: 'BC-1',
      itemName: 'Oreo',
      batchNumber: 'B-01',
      quantity: 1,
      availableQuantity: 5,
      salesPrice: 50,
      mrp: 60,
      taxRatePercent: 0,
      taxIncluded: false,
      costPrice: 0,
    };
    const result = migrateLegacyCartItem(item);
    expect(result).not.toBeNull();
    expect(result!.hsnCode).toBeNull();
  });
});

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
      clientLineKey: 'clk-1',
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
      itemDiscountType: 0,
      itemDiscountValue: 0,
      hsnCode: '0902',
    };

    stubIndexedDb({
      shopId: 'shop-1',
      items: [cartRow],
      updatedAt: new Date().toISOString(),
    });

    await expect(service.loadCart('shop-1', 60_000)).resolves.toEqual([cartRow]);
  });

  it('migrates legacy rows missing clientLineKey/discount fields by generating values', async () => {
    const legacyRow = {
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
      items: [legacyRow],
      updatedAt: new Date().toISOString(),
    });

    const result = await service.loadCart('shop-1', 60_000);
    expect(result).toHaveLength(1);
    expect(result[0].inventoryBatchId).toBe('batch-1');
    expect(result[0].clientLineKey).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
    );
    expect(result[0].itemDiscountType).toBe(0);
    expect(result[0].itemDiscountValue).toBe(0);
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
