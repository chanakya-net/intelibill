import 'fake-indexeddb/auto';
import { IDBFactory } from 'fake-indexeddb';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { OfflineSalesSnapshotIndexedDbService } from '../../../core/storage/offline-sales-snapshot-indexeddb.service';
import {
  hasOfflineExpiryAuthority,
  type OfflineSellableBatchSnapshot,
} from '../../sales/utils/offline-sales-snapshot.schema';

describe('expiry snapshot activation', () => {
  beforeEach(() => {
    // Other snapshot tests can retain connections. A blocked deletion leaves subsequent opens
    // queued, so each test owns its database factory instead of deleting shared databases.
    vi.stubGlobal('indexedDB', new IDBFactory());
  });
  afterEach(() => vi.unstubAllGlobals());
  const complete = {
    batchId: 'batch',
    itemId: 'item',
    itemName: 'Milk',
    barcode: 'MILK',
    uom: 'kg',
    batchNumber: 'LOT',
    quantity: 3,
    costPrice: 1,
    mrp: 2,
    salesPrice: 2,
    taxRatePercent: 0,
    taxIncluded: false,
    purchaseTaxIncluded: false,
    expiryDate: null,
    manufacturingDate: null,
    expiryEvidenceId: 'proof',
    effectivePolicy: {
      version: 2,
      mode: 'Block',
      expiryRequired: false,
      sellThroughExpiryDate: true,
      warningDays: 7,
      overrideAllowed: false,
    },
    physicalQuantity: 3,
    quarantinedQuantity: 0,
    availableQuantity: 3,
    batchVersion: 8,
    expiryCapturedAt: '2026-11-08T12:00:00Z',
    expiryEvidenceExpiresAt: '2026-11-09T12:00:00Z',
  } as OfflineSellableBatchSnapshot;
  for (const [field, value] of [
    ['expiryDate', undefined],
    ['manufacturingDate', undefined],
    ['expiryDate', '2026-02-30'],
    ['manufacturingDate', 'not-a-date'],
    ['expiryDate', '2026-11-09T00:00:00Z'],
  ] as const) {
    it(`refuses activation with incomplete date authority ${field}=${value}`, async () => {
      const db = new OfflineSalesSnapshotIndexedDbService();
      const batch = { ...complete, [field]: value };
      if (value === undefined) delete batch[field];
      const id = crypto.randomUUID();
      await db.beginAttempt({
        snapshotId: id,
        shopId: id,
        schemaVersion: 4,
        businessTimeZoneId: 'Asia/Kolkata',
        catalogVersion: 3,
        startedAt: '2026-11-08T12:00:00Z',
      });
      await db.writeBatch(id, id, batch);
      await expect(db.markComplete(id, id, '2026-11-08T12:01:00Z')).rejects.toThrow();
      expect(hasOfflineExpiryAuthority(batch, 3)).toBe(false);
      expect(await db.getUsableSnapshotId(id)).toBeNull();
    });
  }
  it('activates explicit null decision dates as complete known undated facts', async () => {
    const db = new OfflineSalesSnapshotIndexedDbService();
    const id = crypto.randomUUID();
    await db.beginAttempt({
      snapshotId: id,
      shopId: id,
      schemaVersion: 4,
      businessTimeZoneId: 'Asia/Kolkata',
      catalogVersion: 3,
      startedAt: '2026-11-08T12:00:00Z',
    });
    await db.writeBatch(id, id, complete);
    expect(hasOfflineExpiryAuthority(complete, 3)).toBe(true);
    await db.markComplete(id, id, '2026-11-08T12:01:00Z');
    expect(await db.getUsableSnapshotId(id)).toBe(id);
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
