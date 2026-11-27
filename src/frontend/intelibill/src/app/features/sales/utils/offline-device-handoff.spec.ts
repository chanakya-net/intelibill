import { describe, expect, it } from 'vitest';

import type { OfflineQueuedSaleRecord, OfflineSaleQueueStatus } from '../services/offline-sale-core.types';
import { assessDeviceHandoff } from './offline-device-handoff';

function rec(clientSaleId: string, status: OfflineSaleQueueStatus): OfflineQueuedSaleRecord {
  return { clientSaleId, status } as OfflineQueuedSaleRecord;
}

describe('assessDeviceHandoff', () => {
  it('not safe while unsynced invoices exist', () => {
    const result = assessDeviceHandoff([rec('a', 'Pending'), rec('b', 'Synced'), rec('c', 'NeedsReview')], null);
    expect(result).toEqual({ unsyncedCount: 2, uncoveredCount: 2, safeToRetire: false });
  });

  it('safe after server receipt', () => {
    const result = assessDeviceHandoff([rec('a', 'Synced'), rec('b', 'SyncedWithWarnings')], null);
    expect(result).toEqual({ unsyncedCount: 0, uncoveredCount: 0, safeToRetire: true });
  });

  it('safe when the last export covers every unsynced invoice', () => {
    const records = [rec('a', 'Pending'), rec('b', 'Failed'), rec('c', 'Syncing')];
    expect(assessDeviceHandoff(records, { clientSaleIds: ['a', 'b', 'c', 'zz'] }).safeToRetire).toBe(true);
  });

  it('not safe when an invoice was queued after the last export', () => {
    const result = assessDeviceHandoff([rec('a', 'Pending'), rec('new', 'Pending')], { clientSaleIds: ['a'] });
    expect(result).toEqual({ unsyncedCount: 2, uncoveredCount: 1, safeToRetire: false });
  });
});
