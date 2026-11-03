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
  const ownerA = { userId: 'user-1', shopId: 'shop-a' };
  const ownerB = { userId: 'user-1', shopId: 'shop-b' };
  let service: SalesCartIndexedDbService;
  let db: FakeCartDatabase;

  beforeEach(() => {
    service = new SalesCartIndexedDbService();
    db = new FakeCartDatabase();
    db.install();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('returns persisted cart rows when every row has inventoryBatchId', async () => {
    const cartRow = makeRow('batch-1');
    db.seed({ ...recordFor(ownerA), items: [cartRow] });

    await expect(service.loadCart(ownerA, 60_000)).resolves.toEqual([cartRow]);
    expect(db.openedVersion).toBe(2);
    expect(db.createdStores).toEqual(['sales-cart-by-owner']);
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
    db.seed({ ...recordFor(ownerA), items: [legacyRow] });

    const result = await service.loadCart(ownerA, 60_000);
    expect(result).toHaveLength(1);
    expect(result[0].inventoryBatchId).toBe('batch-1');
    expect(result[0].clientLineKey).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
    );
    expect(result[0].itemDiscountType).toBe(0);
    expect(result[0].itemDiscountValue).toBe(0);
  });

  it('drops legacy cart rows without inventoryBatchId and clears storage', async () => {
    const { inventoryBatchId: _dropped, ...rowWithoutBatch } = makeRow('batch-1');
    db.seed({ ...recordFor(ownerA), items: [rowWithoutBatch] });

    await expect(service.loadCart(ownerA, 60_000)).resolves.toEqual([]);
    expect(db.has('user-1|shop-a')).toBe(false);
  });

  it('a_b_nonempty_drafts_restore_independently: each shop keeps its own goods and services', async () => {
    const service1 = { ...makeService('svc-a') };
    await service.saveCart(ownerA, [makeRow('batch-a')], [service1]);
    await service.saveCart(ownerB, [makeRow('batch-b')], []);

    await expect(service.loadCart(ownerA, 60_000)).resolves.toEqual([makeRow('batch-a')]);
    await expect(service.loadServiceCart(ownerA, 60_000)).resolves.toEqual([service1]);
    await expect(service.loadCart(ownerB, 60_000)).resolves.toEqual([makeRow('batch-b')]);
    await expect(service.loadServiceCart(ownerB, 60_000)).resolves.toEqual([]);
  });

  it('logout_clears_memory_not_ownership: another user in the same shop cannot read the draft', async () => {
    await service.saveCart(ownerA, [makeRow('batch-a')]);

    await expect(service.loadCart({ userId: 'user-2', shopId: 'shop-a' }, 60_000)).resolves.toEqual([]);
    await expect(service.loadCart(ownerA, 60_000)).resolves.toEqual([makeRow('batch-a')]);
  });

  it('ignores a record whose owner does not match the key it was read under', async () => {
    db.seed({ ...recordFor(ownerA), userId: 'user-2', items: [makeRow('batch-a')] });

    await expect(service.loadCart(ownerA, 60_000)).resolves.toEqual([]);
  });

  it('clears only the given owner', async () => {
    await service.saveCart(ownerA, [makeRow('batch-a')]);
    await service.saveCart(ownerB, [makeRow('batch-b')]);

    await service.clearCart(ownerA);

    expect(db.has('user-1|shop-a')).toBe(false);
    expect(db.has('user-1|shop-b')).toBe(true);
  });

  it('never reads or writes without a complete owner', async () => {
    await service.saveCart({ userId: '', shopId: 'shop-a' }, [makeRow('batch-a')]);

    await expect(service.loadCart({ userId: 'user-1', shopId: '' }, 60_000)).resolves.toEqual([]);
    expect(db.size()).toBe(0);
  });

  function recordFor(owner: { userId: string; shopId: string }) {
    return {
      ownerKey: `${owner.userId}|${owner.shopId}`,
      userId: owner.userId,
      shopId: owner.shopId,
      items: [] as unknown[],
      serviceItems: [] as unknown[],
      updatedAt: new Date().toISOString(),
    };
  }

  function makeRow(inventoryBatchId: string): SalesCartDraftItem {
    return {
      clientLineKey: `clk-${inventoryBatchId}`,
      barcode: 'barcode-1',
      itemName: 'Oreo',
      batchNumber: 'B-01',
      inventoryBatchId,
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
  }

  function makeService(serviceId: string) {
    return {
      kind: 'service' as const,
      clientLineKey: `clk-${serviceId}`,
      serviceId,
      serviceName: 'Repair',
      serviceCode: 'R1',
      quantity: 1,
      unitPrice: 100,
      taxRatePercent: 18,
      taxIncluded: false,
      hsnCode: null,
    };
  }
});

/** A single-store IndexedDB stand-in: requests settle on a microtask, the way the browser's do. */
class FakeCartDatabase {
  openedVersion: number | null = null;
  readonly createdStores: string[] = [];
  private readonly rows = new Map<string, Record<string, unknown>>();

  seed(record: Record<string, unknown> & { ownerKey: string }): void {
    this.rows.set(record.ownerKey, structuredClone(record));
  }

  has(key: string): boolean {
    return this.rows.has(key);
  }

  size(): number {
    return this.rows.size;
  }

  install(): void {
    const stores = new Set<string>();
    const database = {
      objectStoreNames: { contains: (name: string) => stores.has(name) },
      createObjectStore: (name: string) => {
        stores.add(name);
        this.createdStores.push(name);
      },
      transaction: () => ({
        objectStore: () => ({
          get: (key: string) => this.settle(() => this.rows.get(key)),
          put: (value: Record<string, unknown> & { ownerKey: string }) =>
            this.settle(() => {
              this.rows.set(value.ownerKey, structuredClone(value));
            }),
          delete: (key: string) => this.settle(() => this.rows.delete(key)),
        }),
      }),
    };

    vi.stubGlobal('indexedDB', {
      open: vi.fn((_name: string, version: number) => {
        const request = {
          result: database as unknown as IDBDatabase,
          error: null,
          onupgradeneeded: null as null | (() => void),
          onsuccess: null as null | (() => void),
          onerror: null as null | (() => void),
        };
        queueMicrotask(() => {
          if (this.openedVersion === null) {
            request.onupgradeneeded?.();
          }
          this.openedVersion = version;
          request.onsuccess?.();
        });
        return request;
      }),
    });
  }

  private settle(work: () => unknown) {
    const request = {
      result: undefined as unknown,
      error: null,
      onsuccess: null as null | (() => void),
      onerror: null as null | (() => void),
    };
    queueMicrotask(() => {
      request.result = work();
      request.onsuccess?.();
    });
    return request;
  }
}
