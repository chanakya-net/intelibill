import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { OfflineSalesQueueIndexedDbService } from './offline-sales-queue-indexeddb.service';

describe('OfflineSalesQueueIndexedDbService', () => {
  let service: OfflineSalesQueueIndexedDbService;

  beforeEach(() => {
    stubIndexedDb();
    service = new OfflineSalesQueueIndexedDbService();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('stores pending sales and reads them in sold-time order', async () => {
    await service.savePendingSale(makeInput('shop-1', 'device-1', 'sale-2', '2026-05-22T10:02:00.000Z'));
    await service.savePendingSale(makeInput('shop-1', 'device-1', 'sale-1', '2026-05-22T10:01:00.000Z'));

    const pending = await service.getPendingSales('shop-1', 'device-1');
    expect(pending.map((item) => item.clientSaleId)).toEqual(['sale-1', 'sale-2']);
  });

  it('resolves save only after the IndexedDB transaction completes', async () => {
    const transactions = stubIndexedDb({ autoCompleteTransactions: false });
    service = new OfflineSalesQueueIndexedDbService();
    let resolved = false;

    const savePromise = service
      .savePendingSale(makeInput('shop-1', 'device-1', 'sale-1', '2026-05-22T10:01:00.000Z'))
      .then(() => {
        resolved = true;
      });
    await flushMicrotasks();

    expect(resolved).toBe(false);
    transactions[0].oncomplete?.();
    await savePromise;
    expect(resolved).toBe(true);
  });

  it('isolates by shop and device', async () => {
    await service.savePendingSale(makeInput('shop-1', 'device-1', 'sale-1', '2026-05-22T10:01:00.000Z'));
    await service.savePendingSale(makeInput('shop-2', 'device-1', 'sale-2', '2026-05-22T10:02:00.000Z'));

    const pending = await service.getPendingSales('shop-1', 'device-1');
    expect(pending).toHaveLength(1);
    expect(pending[0].clientSaleId).toBe('sale-1');
  });

  it('loads retryable sales from pending and failed records only in sold-time order', async () => {
    await service.savePendingSale(makeInput('shop-1', 'device-1', 'failed-late', '2026-05-22T10:03:00.000Z'));
    await service.applySyncResult('shop-1', 'device-1', 'failed-late', { status: 'Failed', errorCode: 'X' });
    await service.savePendingSale(makeInput('shop-1', 'device-1', 'pending-early', '2026-05-22T10:01:00.000Z'));
    await service.savePendingSale(makeInput('shop-1', 'device-1', 'needs-review', '2026-05-22T10:02:00.000Z'));
    await service.applySyncResult('shop-1', 'device-1', 'needs-review', { status: 'NeedsReview', errorCode: 'REVIEW' });

    const retryable = await service.getRetryableSales('shop-1', 'device-1');

    expect(retryable.map((item) => item.clientSaleId)).toEqual(['pending-early', 'failed-late']);
  });

  it('updates sync status preserving client sale id and idempotency key', async () => {
    await service.savePendingSale(makeInput('shop-1', 'device-1', 'sale-1', '2026-05-22T10:01:00.000Z'));

    const syncing = await service.markSyncInProgress('shop-1', 'device-1', 'sale-1');
    const synced = await service.applySyncResult('shop-1', 'device-1', 'sale-1', {
      status: 'Synced',
      serverSaleId: 'server-1',
    });

    expect(syncing?.status).toBe('Syncing');
    expect(synced?.status).toBe('Synced');
    expect(synced?.clientSaleId).toBe('sale-1');
    expect(synced?.idempotencyKey).toBe('idem-sale-1');
  });

  it('cleans synced records older than 3 days and keeps unresolved statuses', async () => {
    await service.savePendingSale(makeInput('shop-1', 'device-1', 'old-synced', '2026-05-18T10:00:00.000Z'));
    await service.applySyncResult('shop-1', 'device-1', 'old-synced', { status: 'Synced' });

    await service.savePendingSale(makeInput('shop-1', 'device-1', 'old-failed', '2026-05-18T10:00:00.000Z'));
    await service.applySyncResult('shop-1', 'device-1', 'old-failed', { status: 'Failed', errorCode: 'X', errorMessage: 'Y' });
    await service.savePendingSale(makeInput('shop-1', 'device-1', 'old-warning', '2026-05-18T10:00:00.000Z'));
    await service.applySyncResult('shop-1', 'device-1', 'old-warning', { status: 'SyncedWithWarnings', warnings: ['needs-review'] });

    const removed = await service.deleteOldSyncedRecords(-1);

    expect(removed).toBe(1);

    const counts = await service.getStatusCounts('shop-1', 'device-1');
    expect(counts.Synced).toBe(0);
    expect(counts.SyncedWithWarnings).toBe(1);
    expect(counts.Failed).toBe(1);

  });

  it('no-ops cleanup and status reads when IndexedDB is unavailable', async () => {
    vi.unstubAllGlobals();
    service = new OfflineSalesQueueIndexedDbService();

    await expect(service.deleteOldSyncedRecords()).resolves.toBe(0);
    await expect(service.getStatusCounts('shop-1', 'device-1')).resolves.toEqual({
      Pending: 0,
      Syncing: 0,
      Synced: 0,
      SyncedWithWarnings: 0,
      NeedsReview: 0,
      Failed: 0,
    });
  });

  function makeInput(shopId: string, deviceId: string, clientSaleId: string, soldAt: string) {
    return {
      shopId,
      deviceId,
      clientSaleId,
      idempotencyKey: `idem-${clientSaleId}`,
      invoiceNumber: `INV-${clientSaleId}`,
      soldAt,
      payload: {
        clientSaleId,
        idempotencyKey: `idem-${clientSaleId}`,
        shopId,
        deviceId,
        invoiceNumber: `INV-${clientSaleId}`,
        soldAt,
        pricing: {
          lines: [],
          totals: {
            totalBeforeDiscount: 0,
            totalDiscount: 0,
            totalTax: 0,
            grandTotal: 0,
            paidAmount: 0,
            dueAmount: 0,
          },
        },
        paymentMethod: 1,
        customerId: null,
        customerName: null,
        customerPhone: null,
      },
    };
  }

  async function flushMicrotasks(): Promise<void> {
    for (let i = 0; i < 10; i++) {
      await Promise.resolve();
    }
  }

  function stubIndexedDb(options: { readonly autoCompleteTransactions: boolean } = { autoCompleteTransactions: true }) {
    const storesByName = new Map<string, Map<string, unknown>>();
    const transactions: Array<{
      readonly objectStore: () => unknown;
      oncomplete: null | (() => void);
      onerror: null | (() => void);
      onabort: null | (() => void);
      error: unknown;
    }> = [];

    const database = {
      objectStoreNames: {
        contains: vi.fn((storeName: string) => storesByName.has(storeName)),
      },
      createObjectStore: vi.fn((storeName: string) => {
        storesByName.set(storeName, new Map());
      }),
      transaction: vi.fn((storeName: string) => {
        const tx = {
          objectStore: () => {
            const store = storesByName.get(storeName) ?? new Map<string, unknown>();
            storesByName.set(storeName, store);
            return {
              get: (key: string) => createRequest(() => store.get(key)),
              getAll: () => createRequest(() => Array.from(store.values())),
              put: (value: { key: string }) =>
                createRequest(() => {
                  store.set(value.key, value);
                  return value;
                }),
              delete: (key: string) =>
                createRequest(() => {
                  store.delete(key);
                  return undefined;
                }),
            };
          },
          oncomplete: null as null | (() => void),
          onerror: null as null | (() => void),
          onabort: null as null | (() => void),
          error: null as unknown,
        };
        transactions.push(tx);
        if (options.autoCompleteTransactions) {
          queueMicrotask(() => tx.oncomplete?.());
        }
        return tx;
      }),
    };

    vi.stubGlobal('indexedDB', {
      open: vi.fn(() => {
        const openRequest = {
          result: database as unknown as IDBDatabase,
          error: null,
          onupgradeneeded: null as null | (() => void),
          onsuccess: null as null | (() => void),
          onerror: null as null | (() => void),
        };
        queueMicrotask(() => openRequest.onupgradeneeded?.());
        queueMicrotask(() => openRequest.onsuccess?.());
        return openRequest;
      }),
    });

    return transactions;
  }

  function createRequest<T>(resolveValue: () => T) {
    const request = {
      result: undefined as T | undefined,
      error: null as unknown,
      onsuccess: null as null | (() => void),
      onerror: null as null | (() => void),
    };

    queueMicrotask(() => {
      try {
        request.result = resolveValue();
        request.onsuccess?.();
      } catch (error) {
        request.error = error;
        request.onerror?.();
      }
    });

    return request;
  }
});
