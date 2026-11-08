import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { OfflineSalesSnapshotIndexedDbService } from '../../../core/storage/offline-sales-snapshot-indexeddb.service';
import { OfflineSalesSnapshotSyncService } from './offline-sales-snapshot-sync.service';

describe('OfflineSalesSnapshotSyncService', () => {
  const shopId = 'shop-1';

  let snapshotDb: OfflineSalesSnapshotIndexedDbService;
  let service: OfflineSalesSnapshotSyncService;

  beforeEach(() => {
    snapshotDb = new OfflineSalesSnapshotIndexedDbService();
    service = new OfflineSalesSnapshotSyncService(
      { getAccessToken: () => 'token' } as any,
      snapshotDb,
      { scope: () => ({ userId: 'user-1', shopId, epoch: 1 }), epoch: () => 1, isCurrent: () => true } as any
    );

    stubIndexedDb();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('does not promote usable snapshot without complete record', async () => {
    await snapshotDb.beginAttempt({
      snapshotId: 'previous',
      shopId,
      schemaVersion: 4, businessTimeZoneId: 'Asia/Kolkata', catalogVersion: 1,
      startedAt: new Date(0).toISOString(),
    });
    await snapshotDb.markComplete('previous', shopId, new Date(0).toISOString());
    expect(await snapshotDb.getUsableSnapshotId(shopId)).toBe('previous');

    const ndjson =
      [
        JSON.stringify({
          type: 'metadata',
          metadata: { snapshotId: 'attempt-1', shopId, schemaVersion: 4, businessTimeZoneId: 'Asia/Kolkata', catalogVersion: 1, startedAt: new Date().toISOString() },
        }),
        JSON.stringify({
          type: 'batch',
          batch: {
            expiryEvidenceId: 'proof-1', effectivePolicy: { version: 1, mode: 'Ignore', expiryRequired: false, sellThroughExpiryDate: true, warningDays: 7, overrideAllowed: false },
            physicalQuantity: 1, quarantinedQuantity: 0, availableQuantity: 1, batchVersion: 8,
            expiryCapturedAt: '2026-11-08T12:00:00Z', expiryEvidenceExpiresAt: '2026-11-15T12:00:00Z',
            batchId: 'b1',
            itemId: 'i1',
            itemName: 'Item',
            barcode: 'BC',
            uom: 'kg',
            hsnCode: null,
            batchNumber: 'B-1',
            quantity: 1,
            costPrice: 1,
            mrp: 1,
            salesPrice: 1,
            taxRatePercent: 0,
            taxIncluded: false,
            purchaseTaxIncluded: false,
            expiryDate: null,
          },
        }),
      ].join('\n') + '\n';

    stubFetch(ndjson);

    await service.syncForShop(shopId);

    expect(await snapshotDb.getUsableSnapshotId(shopId)).toBe('previous');
  });

  it('promotes usable snapshot only after persisting complete record', async () => {
    expect(await snapshotDb.getUsableSnapshotId(shopId)).toBeNull();

    const completedAt = new Date().toISOString();
    const ndjson =
      [
        JSON.stringify({
          type: 'metadata',
          metadata: { snapshotId: 'attempt-2', shopId, schemaVersion: 4, businessTimeZoneId: 'Asia/Kolkata', catalogVersion: 1, startedAt: new Date().toISOString() },
        }),
        JSON.stringify({
          type: 'complete',
          complete: { snapshotId: 'attempt-2', completedAt, batchCount: 0, customerCount: 0, discountRuleCount: 0, activeLeaseCount: 0, serviceCount: 0 },
        }),
      ].join('\n') + '\n';

    stubFetch(ndjson);

    await service.syncForShop(shopId);

    expect(await snapshotDb.getUsableSnapshotId(shopId)).toBe('attempt-2');
  });

  it('treats an error record as terminal and keeps its code and trace id', async () => {
    await snapshotDb.beginAttempt({
      snapshotId: 'previous',
      shopId,
      schemaVersion: 4, businessTimeZoneId: 'Asia/Kolkata', catalogVersion: 1,
      startedAt: new Date(0).toISOString(),
    });
    await snapshotDb.markComplete('previous', shopId, new Date(0).toISOString());
    const markFailed = vi.spyOn(snapshotDb, 'markFailed');

    const ndjson =
      [
        JSON.stringify({
          type: 'metadata',
          metadata: { snapshotId: 'attempt-err', shopId, schemaVersion: 4, businessTimeZoneId: 'Asia/Kolkata', catalogVersion: 1, startedAt: new Date().toISOString() },
        }),
        JSON.stringify({
          type: 'error',
          error: {
            snapshotId: 'attempt-err',
            code: 'OfflineSnapshot.StreamFailed',
            message: 'The offline snapshot could not be completed. Try again later.',
            traceId: '0af7651916cd43dd8448eb211c80319c',
          },
        }),
        // Nothing after an error may make the attempt usable.
        JSON.stringify({
          type: 'complete',
          complete: { snapshotId: 'attempt-err', completedAt: new Date().toISOString() },
        }),
      ].join('\n') + '\n';

    stubFetch(ndjson);

    await service.syncForShop(shopId);

    expect(await snapshotDb.getUsableSnapshotId(shopId)).toBe('previous');
    expect(markFailed).toHaveBeenCalledTimes(1);
    expect(markFailed).toHaveBeenCalledWith(
      'attempt-err',
      shopId,
      'OfflineSnapshot.StreamFailed',
      'The offline snapshot could not be completed. Try again later.',
      '0af7651916cd43dd8448eb211c80319c',
    );
  });

  it('persists service records from the snapshot stream', async () => {
    expect(await snapshotDb.getUsableSnapshotId(shopId)).toBeNull();

    const completedAt = new Date().toISOString();
    const ndjson =
      [
        JSON.stringify({
          type: 'metadata',
          metadata: { snapshotId: 'attempt-svc', shopId, schemaVersion: 4, businessTimeZoneId: 'Asia/Kolkata', catalogVersion: 1, startedAt: new Date().toISOString() },
        }),
        JSON.stringify({
          type: 'service',
          service: {
            serviceId: 'svc-1',
            code: 'SVC001',
            name: 'Consulting',
            price: 500,
            taxRatePercent: 18,
            taxIncluded: false,
            hsnCode: '998311',
          },
        }),
        JSON.stringify({
          type: 'complete',
          complete: { snapshotId: 'attempt-svc', completedAt, batchCount: 0, customerCount: 0, discountRuleCount: 0, activeLeaseCount: 0, serviceCount: 1 },
        }),
      ].join('\n') + '\n';

    stubFetch(ndjson);

    await service.syncForShop(shopId);

    expect(await snapshotDb.getUsableSnapshotId(shopId)).toBe('attempt-svc');
    // Service record was processed successfully — snapshot was promoted
  });

  it('fetches the snapshot stream with no-store caching to bypass service worker API cache', async () => {
    const completedAt = new Date().toISOString();
    const ndjson =
      [
        JSON.stringify({
          type: 'metadata',
          metadata: { snapshotId: 'attempt-3', shopId, schemaVersion: 4, businessTimeZoneId: 'Asia/Kolkata', catalogVersion: 1, startedAt: new Date().toISOString() },
        }),
        JSON.stringify({
          type: 'complete',
          complete: { snapshotId: 'attempt-3', completedAt, batchCount: 0, customerCount: 0, discountRuleCount: 0, activeLeaseCount: 0, serviceCount: 0 },
        }),
      ].join('\n') + '\n';

    const fetchSpy = stubFetch(ndjson);

    await service.syncForShop(shopId);

    expect(fetchSpy).toHaveBeenCalledWith(
      expect.any(String),
      expect.objectContaining({ cache: 'no-store' }),
    );
    expect(fetchSpy).toHaveBeenCalledWith(
      expect.stringContaining('ngsw-bypass=true'),
      expect.any(Object),
    );
  });

  it('refuses a complete marker whose advertised batch count exceeds received rows', async () => {
    stubFetch([
      JSON.stringify({ type: 'metadata', metadata: { snapshotId: 'count-mismatch', shopId, schemaVersion: 4, businessTimeZoneId: 'Asia/Kolkata', catalogVersion: 1, startedAt: new Date().toISOString() } }),
      JSON.stringify({ type: 'complete', complete: { snapshotId: 'count-mismatch', completedAt: new Date().toISOString(), batchCount: 1, customerCount: 0, discountRuleCount: 0, activeLeaseCount: 0, serviceCount: 0 } }),
    ].join('\n') + '\n');
    await service.syncForShop(shopId);
    expect(await snapshotDb.getUsableSnapshotId(shopId)).toBeNull();
  });

  function stubFetch(bodyText: string): ReturnType<typeof vi.fn> {
    const encoder = new TextEncoder();
    const bytes = encoder.encode(bodyText);

    const stream = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(bytes);
        controller.close();
      },
    });

    const fetchSpy = vi.fn(async () => ({ ok: true, body: stream }));
    vi.stubGlobal('fetch', fetchSpy as unknown as typeof fetch);
    return fetchSpy;
  }

  function stubIndexedDb(): void {
    const stores = new Map<string, Map<string, unknown>>();

    function getStore(storeName: string): Map<string, unknown> {
      const existing = stores.get(storeName);
      if (existing) return existing;
      const created = new Map<string, unknown>();
      stores.set(storeName, created);
      return created;
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

    const database = {
      objectStoreNames: {
        contains: vi.fn((name: string) => stores.has(name)),
      },
      createObjectStore: vi.fn((name: string) => {
        getStore(name);
        return undefined;
      }),
      transaction: vi.fn((storeNameOrNames: string | string[], _mode: IDBTransactionMode) => {
        const names = Array.isArray(storeNameOrNames) ? storeNameOrNames : [storeNameOrNames];
        const transaction = {
          error: null as unknown,
          oncomplete: null as null | (() => void),
          onerror: null as null | (() => void),
          objectStore: (storeName: string) => {
            if (!names.includes(storeName)) throw new Error('Store not in transaction.');
            const store = getStore(storeName);
            return {
              get: (key: string) => createRequest(() => store.get(key)),
              getAll: () => createRequest(() => [...store.values()]),
              put: (value: { snapshotId?: string; shopId?: string; key?: string }) =>
                createRequest(() => {
                  const keyValue = value.key ?? value.snapshotId ?? value.shopId;
                  if (!keyValue) throw new Error('Missing key.');
                  store.set(keyValue, value);
                  return value;
                }),
            };
          },
        };

        queueMicrotask(() => transaction.oncomplete?.());
        return transaction;
      }),
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
        queueMicrotask(() => openRequest.onupgradeneeded?.());
        queueMicrotask(() => openRequest.onsuccess?.());
        return openRequest;
      }),
    });
  }
});
