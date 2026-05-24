import { describe, expect, it } from 'vitest';

import {
  OFFLINE_SALES_SNAPSHOT_ATTEMPTS_STORE,
  OFFLINE_SALES_SNAPSHOT_BATCHES_STORE,
  OFFLINE_SALES_SNAPSHOT_CUSTOMERS_STORE,
  OFFLINE_SALES_SNAPSHOT_DISCOUNT_RULES_STORE,
  OFFLINE_SALES_SNAPSHOT_SHOP_POINTERS_STORE,
  OFFLINE_SALES_SNAPSHOT_ACTIVE_LEASES_STORE,
  OFFLINE_SALES_SNAPSHOT_STORE_CONFIGS,
  configureOfflineSalesSnapshotSchema,
  isOfflineSalesSnapshotAttemptStatus,
  isOfflineSalesSnapshotAttemptRecord,
  isOfflineSalesSnapshotMetadata,
  isOfflineSalesSnapshotShopPointerRecord,
} from './offline-sales-snapshot.schema';

describe('offline-sales-snapshot.schema', () => {
  it('identifies valid and invalid metadata objects', () => {
    expect(isOfflineSalesSnapshotMetadata({
      snapshotId: 'snap-1',
      shopId: 'shop-1',
      schemaVersion: 1,
      startedAt: '2026-05-24T10:00:00.000Z',
    })).toBe(true);

    expect(isOfflineSalesSnapshotMetadata({ snapshotId: '', shopId: 'shop-1', schemaVersion: 1, startedAt: 'x' })).toBe(false);
  });

  it('guards attempt status and attempt records', () => {
    expect(isOfflineSalesSnapshotAttemptStatus('complete')).toBe(true);
    expect(isOfflineSalesSnapshotAttemptStatus('unknown')).toBe(false);

    expect(isOfflineSalesSnapshotAttemptRecord({
      snapshotId: 'snap-1',
      shopId: 'shop-1',
      schemaVersion: 1,
      startedAt: '2026-05-24T10:00:00.000Z',
      status: 'complete',
    })).toBe(true);

    expect(isOfflineSalesSnapshotAttemptRecord({
      snapshotId: 'snap-1',
      shopId: '',
      schemaVersion: 1,
      startedAt: '2026-05-24T10:00:00.000Z',
      status: 'complete',
    })).toBe(false);
  });

  it('guards pointer records from plain objects', () => {
    expect(isOfflineSalesSnapshotShopPointerRecord({ shopId: 'shop-1', lastAttemptStatus: 'failed' })).toBe(true);
    expect(isOfflineSalesSnapshotShopPointerRecord({ shopId: '  ', lastAttemptStatus: 'failed' })).toBe(false);
  });

  it('creates object stores for missing snapshot schema stores', () => {
    const storeOrder: Array<{ name: string; keyPath: string }> = [];
    const openStores = new Set<string>([OFFLINE_SALES_SNAPSHOT_ATTEMPTS_STORE]);

    const database = {
      objectStoreNames: {
        contains: (name: string) => openStores.has(name),
      },
      createObjectStore: (name: string, options: { readonly keyPath: string }) => {
        storeOrder.push({ name, keyPath: options.keyPath });
      },
    } as unknown as IDBDatabase;

    configureOfflineSalesSnapshotSchema(database);

    const missing = OFFLINE_SALES_SNAPSHOT_STORE_CONFIGS
      .filter((store) => ![...openStores].includes(store.name))
      .map((store) => store.name);

    const observed = storeOrder.map((store) => store.name);
    for (const store of missing) {
      expect(observed).toContain(store);
    }

    for (const created of storeOrder) {
      const config = OFFLINE_SALES_SNAPSHOT_STORE_CONFIGS.find((store) => store.name === created.name);
      expect(created.keyPath).toBe(config?.keyPath);
    }

    expect(storeOrder).toEqual(
      expect.arrayContaining([
        { name: OFFLINE_SALES_SNAPSHOT_CUSTOMERS_STORE, keyPath: 'key' },
        { name: OFFLINE_SALES_SNAPSHOT_BATCHES_STORE, keyPath: 'key' },
        { name: OFFLINE_SALES_SNAPSHOT_DISCOUNT_RULES_STORE, keyPath: 'key' },
        { name: OFFLINE_SALES_SNAPSHOT_SHOP_POINTERS_STORE, keyPath: 'shopId' },
        { name: OFFLINE_SALES_SNAPSHOT_ACTIVE_LEASES_STORE, keyPath: 'key' },
      ]),
    );
  });
});
