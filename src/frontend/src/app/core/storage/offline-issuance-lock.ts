import { Injectable } from '@angular/core';

/**
 * One exclusive Web Lock per shop serializes everything that decides or changes offline issuing authority on this
 * device: taking an invoice number and writing its queue row, replacing a lease, attaching a permit, promoting a
 * snapshot. It is acquired outside any IndexedDB transaction and never nested.
 */
const lockName = (shopId: string) => `intelibill:offline-issuance:${shopId}`;

function lockManager(): LockManager | null {
  return typeof navigator !== 'undefined' && navigator.locks && typeof navigator.locks.request === 'function'
    ? navigator.locks
    : null;
}

export type ExclusiveResult<T> = { readonly acquired: true; readonly value: T } | { readonly acquired: false };

/**
 * For short authority writes: waits its turn behind an issuance in flight. Without Web Locks the write still
 * happens, so enabling, snapshots and the offline pack keep working; issuance itself refuses instead (below).
 */
export async function underOfflineAuthorityLock<T>(shopId: string, work: () => Promise<T>): Promise<T> {
  const locks = lockManager();
  if (!locks || !shopId) return await work();
  return (await locks.request(lockName(shopId), { mode: 'exclusive' }, work)) as T;
}

@Injectable({ providedIn: 'root' })
export class OfflineIssuanceLock {
  /** Runs the issuance critical section alone. Answers `acquired: false` when safe serialization is unavailable. */
  async runExclusive<T>(shopId: string, work: () => Promise<T>): Promise<ExclusiveResult<T>> {
    const locks = lockManager();
    if (!locks || !shopId) return { acquired: false };
    const value = (await locks.request(lockName(shopId), { mode: 'exclusive' }, work)) as T;
    return { acquired: true, value };
  }
}
