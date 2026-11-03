import type { OfflineQueuedSaleRecord } from '../services/offline-sale-core.types';

export interface DeviceHandoffAssessment {
  readonly unsyncedCount: number;
  readonly uncoveredCount: number;
  readonly safeToRetire: boolean;
}

/**
 * A browser may be retired only when the server has received every invoice on it or the last verified
 * support export already carries those still unsynced.
 */
export function assessDeviceHandoff(
  records: readonly OfflineQueuedSaleRecord[],
  lastExport: { readonly clientSaleIds: readonly string[] } | null,
): DeviceHandoffAssessment {
  const unsynced = records.filter((record) => record.status !== 'Synced' && record.status !== 'SyncedWithWarnings');
  const covered = new Set(lastExport?.clientSaleIds ?? []);
  const uncoveredCount = unsynced.filter((record) => !covered.has(record.clientSaleId)).length;
  return { unsyncedCount: unsynced.length, uncoveredCount, safeToRetire: uncoveredCount === 0 };
}
