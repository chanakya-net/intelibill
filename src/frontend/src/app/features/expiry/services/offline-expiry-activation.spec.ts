import 'fake-indexeddb/auto';
import { beforeEach, describe, expect, it } from 'vitest';
import { OfflineSalesSnapshotIndexedDbService } from '../../../core/storage/offline-sales-snapshot-indexeddb.service';
import { OFFLINE_SALES_SNAPSHOT_DATABASE_NAME } from '../../sales/utils/offline-sales-snapshot.schema';

describe('expiry snapshot activation', () => {
  beforeEach(async () => {
    await new Promise<void>((resolve, reject) => {
      const r = indexedDB.deleteDatabase(OFFLINE_SALES_SNAPSHOT_DATABASE_NAME);
      r.onsuccess = () => resolve();
      r.onerror = () => reject(r.error);
      r.onblocked = () => resolve();
    });
  });
  it('never activates a legacy catalog for future issuance', async () => {
    const db = new OfflineSalesSnapshotIndexedDbService();
    await db.beginAttempt({
      snapshotId: 'legacy',
      shopId: 'shop',
      schemaVersion: 3,
      startedAt: '2026-11-08T12:00:00Z',
    });
    await expect(db.markComplete('legacy', 'shop', '2026-11-08T12:01:00Z')).rejects.toThrow();
    expect(await db.getUsableSnapshotId('shop')).toBeNull();
  });
});
