import { IDBFactory } from 'fake-indexeddb';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { OfflineQueueWriteRefusedError, OfflineSalesQueueIndexedDbService } from './offline-sales-queue-indexeddb.service';

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

  it('keeps the review case and arithmetic differences of a held sale', async () => {
    await service.savePendingSale(makeInput('shop-1', 'device-1', 'sale-1', '2026-05-22T10:01:00.000Z'));
    const mismatch = { path: 'totalAmount', supplied: 1, expected: 118, code: 'amount_mismatch' };

    const held = await service.applySyncResult('shop-1', 'device-1', 'sale-1', {
      status: 'NeedsReview',
      errorCode: 'Sale.OfflineArithmeticMismatch',
      reviewCaseId: 'case-1',
      mismatches: [mismatch],
    });
    const synced = await service.applySyncResult('shop-1', 'device-1', 'sale-1', { status: 'Synced', serverSaleId: 'server-1' });

    expect(held?.reviewCaseId).toBe('case-1');
    expect(held?.mismatches).toEqual([mismatch]);
    expect((await service.getAllByShopDevice('shop-1', 'device-1'))[0].status).toBe('Synced');
    expect(synced?.reviewCaseId).toBeNull();
    expect(synced?.mismatches).toEqual([]);
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

  it('deleteOldSyncedRecords keeps unsynced records of any age', async () => {
    const ancient = '2000-01-01T00:00:00.000Z';
    await service.savePendingSale(makeInput('shop-1', 'device-1', 'pending', ancient));
    await service.savePendingSale(makeInput('shop-1', 'device-1', 'syncing', ancient));
    await service.markSyncInProgress('shop-1', 'device-1', 'syncing');
    await service.savePendingSale(makeInput('shop-1', 'device-1', 'review', ancient));
    await service.applySyncResult('shop-1', 'device-1', 'review', { status: 'NeedsReview', errorCode: 'X' });
    await service.savePendingSale(makeInput('shop-1', 'device-1', 'failed', ancient));
    await service.applySyncResult('shop-1', 'device-1', 'failed', { status: 'Failed', errorCode: 'X' });
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2030-01-01T00:00:00.000Z'));
    try {
      expect(await service.deleteOldSyncedRecords()).toBe(0);
    } finally {
      vi.useRealTimers();
    }

    const all = await service.getAllByShopDevice('shop-1', 'device-1');
    expect(all.map((record) => record.clientSaleId).sort()).toEqual(['failed', 'pending', 'review', 'syncing']);
  });

  it('records survive shop switch', async () => {
    await service.savePendingSale(makeInput('shop-1', 'device-1', 'sale-1', '2026-05-22T10:01:00.000Z'));

    await service.getPendingSales('shop-2', 'device-1');
    await service.getStatusCounts('shop-2', 'device-1');
    await service.deleteOldSyncedRecords();

    expect((await service.getPendingSales('shop-1', 'device-1')).map((record) => record.clientSaleId)).toEqual(['sale-1']);
  });

  it('no-ops cleanup and status reads when IndexedDB is unavailable', async () => {
    vi.stubGlobal('indexedDB', undefined);
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

  describe('write guard (IndexedDB transactions)', () => {
    const input = () => makeInput('shop-1', 'device-1', 'sale-guarded', new Date().toISOString());

    it('asks after the database opened and stores nothing when refused', async () => {
      vi.stubGlobal('indexedDB', new IDBFactory());
      const guarded = new OfflineSalesQueueIndexedDbService();
      const stillAllowed = vi.fn(() => false);

      await expect(guarded.savePendingSale(input(), { stillAllowed })).rejects.toBeInstanceOf(OfflineQueueWriteRefusedError);

      expect(stillAllowed).toHaveBeenCalledTimes(1);
      expect(await guarded.getQueuedSale('shop-1', 'device-1', 'sale-guarded')).toBeNull();
    });

    it('stores the row when allowed and resolves only once it is durable', async () => {
      vi.stubGlobal('indexedDB', new IDBFactory());
      const guarded = new OfflineSalesQueueIndexedDbService();

      const record = await guarded.savePendingSale(input(), { stillAllowed: () => true });

      expect(record.status).toBe('Pending');
      expect((await guarded.getQueuedSale('shop-1', 'device-1', 'sale-guarded'))?.status).toBe('Pending');
    });

    it('stores nothing when the scope was already detached', async () => {
      vi.stubGlobal('indexedDB', new IDBFactory());
      const guarded = new OfflineSalesQueueIndexedDbService();
      const detached = new AbortController();
      detached.abort();

      await expect(
        guarded.savePendingSale(input(), { stillAllowed: () => true, signal: detached.signal }),
      ).rejects.toBeInstanceOf(OfflineQueueWriteRefusedError);
      expect(await guarded.getQueuedSale('shop-1', 'device-1', 'sale-guarded')).toBeNull();
    });

    it('aborts the open transaction on detach and leaves no row', async () => {
      vi.stubGlobal('indexedDB', new IDBFactory());
      const guarded = new OfflineSalesQueueIndexedDbService();
      const detached = new AbortController();

      const saving = guarded.savePendingSale(input(), {
        // Detach lands in the same task as the insert, while the transaction can still abort.
        stillAllowed: () => { queueMicrotask(() => detached.abort()); return true; },
        signal: detached.signal,
      });

      await expect(saving).rejects.toBeInstanceOf(OfflineQueueWriteRefusedError);
      expect(await guarded.getQueuedSale('shop-1', 'device-1', 'sale-guarded')).toBeNull();
    });

    it('leaves an existing row of any status byte-identical when a new write is refused', async () => {
      vi.stubGlobal('indexedDB', new IDBFactory());
      const guarded = new OfflineSalesQueueIndexedDbService();
      const kept = await guarded.savePendingSale(makeInput('shop-1', 'device-1', 'sale-kept', new Date().toISOString()));

      await expect(guarded.savePendingSale(input(), { stillAllowed: () => false })).rejects.toBeInstanceOf(OfflineQueueWriteRefusedError);

      expect(await guarded.getQueuedSale('shop-1', 'device-1', 'sale-kept')).toEqual(kept);
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
          calculationVersion: 1,
          lines: [],
          totals: {
            subtotalBeforeDiscount: 0,
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
        issuedByUserId: 'user-1',
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
        let pendingRequests = 0;
        let completed = false;
        const scheduleComplete = () => {
          if (!options.autoCompleteTransactions || completed) return;
          queueMicrotask(() => {
            if (pendingRequests === 0 && !completed) {
              completed = true;
              tx.oncomplete?.();
            }
          });
        };
        const trackRequest = <T>(resolveValue: () => T) => {
          pendingRequests += 1;
          return createRequest(resolveValue, () => {
            pendingRequests -= 1;
            if (pendingRequests === 0) scheduleComplete();
          });
        };
        const tx = {
          objectStore: () => {
            const store = storesByName.get(storeName) ?? new Map<string, unknown>();
            storesByName.set(storeName, store);
            return {
              get: (key: string) => trackRequest(() => store.get(key)),
              getAll: () => trackRequest(() => Array.from(store.values())),
              put: (value: { key: string }) =>
                trackRequest(() => {
                  store.set(value.key, value);
                  return value;
                }),
              delete: (key: string) =>
                trackRequest(() => {
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
        scheduleComplete();
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

  function createRequest<T>(resolveValue: () => T, onSettled?: () => void) {
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
      } finally {
        onSettled?.();
      }
    });

    return request;
  }
});
