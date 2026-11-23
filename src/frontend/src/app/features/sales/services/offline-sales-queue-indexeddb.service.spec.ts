import { createHash } from 'node:crypto';
import { IDBFactory } from 'fake-indexeddb';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import vectors from '../../../../../../../tests/contracts/licensing/p05-06-legacy-queue-vectors.json';
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

  describe('legacy v1 queue compatibility (P05-06)', () => {
    function sha256(text: string): string {
      return createHash('sha256').update(text, 'utf8').digest('hex');
    }

    async function seedRawV1Rows(rows: readonly Record<string, unknown>[]): Promise<void> {
      const db = await new Promise<IDBDatabase>((resolve, reject) => {
        const open = indexedDB.open('intelibill-offline-sales-queue', 1);
        open.onupgradeneeded = () => {
          const database = open.result;
          if (!database.objectStoreNames.contains('queued-sales')) {
            database.createObjectStore('queued-sales', { keyPath: 'key' });
          }
        };
        open.onsuccess = () => resolve(open.result);
        open.onerror = () => reject(open.error);
      });
      await new Promise<void>((resolve, reject) => {
        const tx = db.transaction('queued-sales', 'readwrite');
        const store = tx.objectStore('queued-sales');
        for (const row of rows) store.put(row);
        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error);
      });
      db.close();
    }

    async function readRawV1Row(key: string): Promise<Record<string, unknown> | undefined> {
      const db = await new Promise<IDBDatabase>((resolve, reject) => {
        const open = indexedDB.open('intelibill-offline-sales-queue', 1);
        open.onsuccess = () => resolve(open.result);
        open.onerror = () => reject(open.error);
      });
      const raw = await new Promise<Record<string, unknown> | undefined>((resolve, reject) => {
        const tx = db.transaction('queued-sales', 'readonly');
        const req = tx.objectStore('queued-sales').get(key);
        req.onsuccess = () => resolve(req.result as Record<string, unknown> | undefined);
        req.onerror = () => reject(req.error);
      });
      db.close();
      return raw;
    }

    it('retains issued bytes through a legacy queue delivery cycle', async () => {
      vi.stubGlobal('indexedDB', new IDBFactory());
      const compat = new OfflineSalesQueueIndexedDbService();
      const { shopId, deviceId } = vectors.scope;

      const raw7ff1 = JSON.parse(vectors.unversionedPreDocument.queueRecordJson) as Record<string, unknown>;
      const raw671d = JSON.parse(vectors.v1FrozenDocument.queueRecordJson) as Record<string, unknown>;
      // Simulate an even older stored row whose optional transport metadata fields were absent.
      const { warnings: _w, syncAttempts: _a, syncAttemptCount: _c, lastSyncAttemptAt: _l, ...minimal7ff1 } = raw7ff1;

      await seedRawV1Rows([minimal7ff1, raw671d]);

      // 1. Reads perform no upgrade writes and keep payload bytes identical.
      const txModes: string[] = [];
      const origOpen = indexedDB.open.bind(indexedDB);
      const openSpy = vi.spyOn(indexedDB, 'open').mockImplementation((name: string, version?: number) => {
        const req = origOpen(name, version);
        req.addEventListener('success', () => {
          const db = req.result;
          const origTx = db.transaction.bind(db);
          db.transaction = ((storeNames: string | string[], mode?: IDBTransactionMode) => {
            txModes.push(mode ?? 'readonly');
            return origTx(storeNames, mode);
          }) as typeof db.transaction;
        });
        return req;
      });

      const byKey7ff1 = await compat.getQueuedSale(shopId, deviceId, 'legacy-7ff1-0001');
      const byKey671d = await compat.getQueuedSale(shopId, deviceId, 'legacy-671d-0002');
      const listed = await compat.getAllByShopDevice(shopId, deviceId);
      const retryable = await compat.getRetryableSales(shopId, deviceId);

      expect(txModes).toEqual(['readonly', 'readonly', 'readonly', 'readonly']);
      txModes.length = 0;

      expect(byKey7ff1).not.toBeNull();
      expect(byKey671d).not.toBeNull();
      expect(listed.map((r) => r.clientSaleId).sort()).toEqual(['legacy-671d-0002', 'legacy-7ff1-0001']);
      expect(retryable.map((r) => r.clientSaleId)).toEqual(['legacy-7ff1-0001', 'legacy-671d-0002']);

      // Read model normalizes missing transport metadata without writing back to IDB.
      expect(byKey7ff1!.warnings).toEqual([]);
      expect(byKey7ff1!.syncAttempts).toEqual([]);
      expect(byKey7ff1!.syncAttemptCount).toBe(0);
      expect(JSON.stringify(byKey7ff1!.payload)).toBe(vectors.unversionedPreDocument.payloadJson);
      expect(sha256(JSON.stringify(byKey7ff1!.payload))).toBe(vectors.unversionedPreDocument.payloadSha256);
      expect(JSON.stringify(byKey671d!.payload)).toBe(vectors.v1FrozenDocument.payloadJson);
      expect(sha256(JSON.stringify(byKey671d!.payload))).toBe(vectors.v1FrozenDocument.payloadSha256);

      // Verify zero upgrade writes occurred in IndexedDB on read.
      const rawAfterRead = await readRawV1Row(raw7ff1['key'] as string);
      expect('syncAttempts' in (rawAfterRead ?? {})).toBe(false);
      expect(JSON.stringify(rawAfterRead?.['payload'])).toBe(vectors.unversionedPreDocument.payloadJson);
      txModes.length = 0;

      // 2. Delivery-state transitions (Pending -> Syncing -> NeedsReview) each run in one readwrite
      //    transaction and keep payload bytes, property order, key, idempotencyKey, invoiceNumber and soldAt untouched.
      const syncing7ff1 = await compat.markSyncInProgress(shopId, deviceId, 'legacy-7ff1-0001');
      expect(txModes).toEqual(['readwrite']);
      txModes.length = 0;
      openSpy.mockRestore();
      expect(syncing7ff1?.status).toBe('Syncing');
      expect(syncing7ff1?.syncAttemptCount).toBe(1);
      expect(JSON.stringify(syncing7ff1?.payload)).toBe(vectors.unversionedPreDocument.payloadJson);

      // An unresolved Syncing legacy row survives cleanup of any age.
      expect(await compat.deleteOldSyncedRecords(-1)).toBe(0);

      const held7ff1 = await compat.applySyncResult(shopId, deviceId, 'legacy-7ff1-0001', {
        status: 'NeedsReview',
        errorCode: 'OfflineLicenceHistory.EvidenceMissing',
        errorMessage: 'Historical evidence required',
        reviewCaseId: 'case-legacy-7ff1',
      });
      expect(held7ff1?.status).toBe('NeedsReview');
      expect(held7ff1?.syncAttempts).toHaveLength(1);
      expect(held7ff1?.reviewCaseId).toBe('case-legacy-7ff1');
      expect(JSON.stringify(held7ff1?.payload)).toBe(vectors.unversionedPreDocument.payloadJson);
      expect(sha256(JSON.stringify(held7ff1?.payload))).toBe(vectors.unversionedPreDocument.payloadSha256);

      const syncing671d = await compat.markSyncInProgress(shopId, deviceId, 'legacy-671d-0002');
      expect(JSON.stringify(syncing671d?.payload)).toBe(vectors.v1FrozenDocument.payloadJson);
      const held671d = await compat.applySyncResult(shopId, deviceId, 'legacy-671d-0002', {
        status: 'NeedsReview',
        errorCode: 'OfflineLicenceHistory.EvidenceMissing',
        reviewCaseId: 'case-legacy-671d',
      });
      expect(JSON.stringify(held671d?.payload)).toBe(vectors.v1FrozenDocument.payloadJson);
      expect(sha256(JSON.stringify(held671d?.payload))).toBe(vectors.v1FrozenDocument.payloadSha256);

      // Re-reading raw IndexedDB rows confirms frozen payload bytes and identity metadata never drifted.
      const rawFinal7ff1 = await readRawV1Row(raw7ff1['key'] as string);
      const rawFinal671d = await readRawV1Row(raw671d['key'] as string);
      expect(JSON.stringify(rawFinal7ff1?.['payload'])).toBe(vectors.unversionedPreDocument.payloadJson);
      expect(JSON.stringify(rawFinal671d?.['payload'])).toBe(vectors.v1FrozenDocument.payloadJson);
      expect(rawFinal7ff1?.['idempotencyKey']).toBe(vectors.unversionedPreDocument.expectedServerIdempotencyKey);
      expect(rawFinal671d?.['idempotencyKey']).toBe(vectors.v1FrozenDocument.expectedServerIdempotencyKey);
      expect(rawFinal7ff1?.['invoiceNumber']).toBe('A/2627/000001');
      expect(rawFinal671d?.['invoiceNumber']).toBe('A/2627/000002');
      expect('licenceEvidence' in (rawFinal7ff1?.['payload'] as Record<string, unknown>)).toBe(false);
      expect('document' in (rawFinal7ff1?.['payload'] as Record<string, unknown>)).toBe(false);
      expect('licenceEvidence' in (rawFinal671d?.['payload'] as Record<string, unknown>)).toBe(false);
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
