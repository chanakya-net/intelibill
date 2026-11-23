import { IDBFactory } from 'fake-indexeddb';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { OfflinePermitClockIndexedDbService } from './offline-permit-clock-indexeddb.service';

describe('OfflinePermitClockIndexedDbService', () => {
  const SERVER = Date.parse('2026-11-22T11:59:30.123Z');
  const owner = { shopId: 'shop', deviceId: 'device', userId: 'user', sessionId: 'session' };
  const observation = { ...owner, permitId: 'permit-1', serverObservedAt: '2026-11-22T11:59:30.123456Z', serverObservedMs: SERVER };

  beforeEach(() => {
    vi.stubGlobal('indexedDB', new IDBFactory());
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(SERVER + 1000);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('needs an online verification before the first evaluation', async () => {
    expect(await new OfflinePermitClockIndexedDbService().evaluate(owner, 'permit-1')).toBe('unverified');
  });

  it('persists a rollback even though the caller refuses the sale, and keeps it after the clock is restored', async () => {
    const tab = new OfflinePermitClockIndexedDbService();
    expect(await tab.rebase(observation)).toBe(true);
    vi.setSystemTime(SERVER + 3_600_000);
    expect(await tab.evaluate(owner, 'permit-1')).toBe('consistent');

    vi.setSystemTime(SERVER + 60_000);
    expect(await tab.evaluate(owner, 'permit-1')).toBe('blocked');

    vi.setSystemTime(SERVER + 3_700_000);
    expect(await tab.evaluate(owner, 'permit-1')).toBe('blocked');
    // A sibling tab and a reloaded document read the same stored latch.
    expect(await new OfflinePermitClockIndexedDbService().evaluate(owner, 'permit-1')).toBe('blocked');
  });

  it('shares the wall high-water between sibling tabs', async () => {
    const first = new OfflinePermitClockIndexedDbService();
    const second = new OfflinePermitClockIndexedDbService();
    await first.rebase(observation);
    vi.setSystemTime(SERVER + 3_600_000);
    expect(await first.evaluate(owner, 'permit-1')).toBe('consistent');

    vi.setSystemTime(SERVER + 60_000);
    expect(await second.evaluate(owner, 'permit-1')).toBe('blocked');
  });

  it('ignores a replayed observation and releases only on a later one', async () => {
    const tab = new OfflinePermitClockIndexedDbService();
    await tab.rebase(observation);
    vi.setSystemTime(SERVER + 3_600_000);
    await tab.evaluate(owner, 'permit-1');
    vi.setSystemTime(SERVER + 60_000);
    await tab.evaluate(owner, 'permit-1');

    expect(await tab.rebase(observation)).toBe(false);
    expect(await tab.evaluate(owner, 'permit-1')).toBe('blocked');

    vi.setSystemTime(SERVER + 3_800_000);
    const later = { ...observation, permitId: 'permit-2', serverObservedMs: SERVER + 3_799_000, serverObservedAt: '2026-11-22T13:02:49.123456Z' };
    expect(await tab.rebase(later)).toBe(true);
    expect(await tab.evaluate(owner, 'permit-2')).toBe('consistent');
    expect(await tab.evaluate(owner, 'permit-1')).toBe('unverified');
  });

  it('keeps the lease record of the same store untouched', async () => {
    const tab = new OfflinePermitClockIndexedDbService();
    await tab.rebase(observation);
    const keys = await new Promise<IDBValidKey[]>((resolve, reject) => {
      const open = indexedDB.open('intelibill-invoice-leases', 1);
      open.onsuccess = () => {
        const request = open.result.transaction('invoice-leases', 'readonly').objectStore('invoice-leases').getAllKeys();
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
      };
    });
    expect(keys).toEqual(['clock::shop::device::user']);
  });
});
