import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SwUpdate, type VersionEvent } from '@angular/service-worker';
import { Subject } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../auth/auth.service';
import { REFRESH_LOCK_MANAGER, type RefreshLockManager } from '../auth/refresh-coordinator.service';
import { ShopContextService, type LeaveReadiness } from '../context/shop-context.service';
import { AppReloadService } from '../services/app-reload.service';
import { NetworkStatusService } from '../services/network-status.service';
import { OfflineSalesDeviceSettingsStorage } from '../storage/offline-sales-device-settings.storage';
import { OfflineSalesSnapshotIndexedDbService } from '../storage/offline-sales-snapshot-indexeddb.service';
import { OfflineSalesSnapshotSyncService } from '../../features/sales/services/offline-sales-snapshot-sync.service';
import { OfflinePackService } from './offline-pack.service';
import {
  OfflinePackUpdateService,
  SNAPSHOT_REFRESH_AGE_MS,
  SNAPSHOT_RETRY_BASE_MS,
  UPDATE_CHECK_INTERVAL_MS,
  UPDATE_SCHEDULE_ENVIRONMENT,
  UPDATE_TRIGGER_MIN_GAP_MS,
} from './offline-pack-update.service';

const CYCLE_LOCK = 'intelibill.tab-task:offline-pack-update';

function ready(currentBuild: string, latestBuild: string): VersionEvent {
  return {
    type: 'VERSION_READY',
    currentVersion: { hash: `h-${currentBuild}`, appData: { buildId: currentBuild } },
    latestVersion: { hash: `h-${latestBuild}-2`, appData: { buildId: latestBuild } },
  };
}

function setup(options: { swEnabled?: boolean; withSwUpdate?: boolean } = {}) {
  const versions = new Subject<VersionEvent>();
  const unrecoverable = new Subject<unknown>();
  const swUpdate = {
    isEnabled: options.swEnabled ?? true,
    versionUpdates: versions,
    unrecoverable,
    checkForUpdate: vi.fn(async () => false),
    activateUpdate: vi.fn(async () => true),
  };
  const pack = {
    reconcile: vi.fn(async () => undefined),
    checkForUpdate: vi.fn(async () => false as boolean | null),
    deviceNeedsPack: vi.fn(() => false),
    refreshStatus: vi.fn(async () => null),
    markIncomplete: vi.fn(),
  };
  const canReachApi = signal(true);
  const network = { canReachApi };
  const leave = { value: { ok: true } as LeaveReadiness };
  const shopContext = { prepareToLeave: vi.fn(async () => leave.value) };
  const appReload = { reloadPage: vi.fn(), restartAtAppRoot: vi.fn() };
  const session = signal<{ activeShopId: string } | null>({ activeShopId: 'shop-1' });
  const settings = {
    current: { enabled: true, lastCompleteSnapshotAt: new Date().toISOString() as string | null },
    loadSettings: vi.fn(() => settings.current),
    updateSettings: vi.fn((_shopId: string, update: (value: unknown) => unknown) => update(settings.current)),
  };
  const snapshotSync = { syncForShop: vi.fn(async () => undefined) };
  const snapshotDb = { getUsableSnapshotInfo: vi.fn(async () => ({ snapshotId: 's-2', completedAt: '2026-11-10T12:00:00.000Z' })) };

  const listeners = { window: new Map<string, () => void>(), document: new Map<string, () => void>() };
  const timers: { callback: () => void; ms: number }[] = [];
  const doc = {
    visibilityState: 'visible' as DocumentVisibilityState,
    addEventListener: (type: string, listener: () => void) => listeners.document.set(type, listener),
    removeEventListener: vi.fn(),
  };
  const clock = { now: Date.now() };
  /** localStorage shared by this browser's tabs. */
  const shared = new Map<string, string>();
  const schedule = {
    window: {
      addEventListener: (type: string, listener: () => void) => listeners.window.set(type, listener),
      removeEventListener: vi.fn(),
    },
    document: doc,
    setInterval: (callback: () => void, ms: number) => timers.push({ callback, ms }),
    clearInterval: vi.fn(),
    now: () => clock.now,
    storage: {
      getItem: (key: string) => shared.get(key) ?? null,
      setItem: (key: string, value: string) => void shared.set(key, value),
    },
  };
  /** Web Locks across tabs: one holder per name, the rest wait in order. */
  const tails = new Map<string, Promise<unknown>>();
  const locks: RefreshLockManager & { names: string[] } = {
    names: [],
    request(name, _options, callback) {
      locks.names.push(name);
      const run = (tails.get(name) ?? Promise.resolve()).then(() => callback());
      tails.set(name, run.catch(() => undefined));
      return run;
    },
  };

  const providers: unknown[] = [
    { provide: OfflinePackService, useValue: pack },
    { provide: NetworkStatusService, useValue: network },
    { provide: ShopContextService, useValue: shopContext },
    { provide: AppReloadService, useValue: appReload },
    { provide: AuthService, useValue: { session } },
    { provide: OfflineSalesDeviceSettingsStorage, useValue: settings },
    { provide: OfflineSalesSnapshotSyncService, useValue: snapshotSync },
    { provide: OfflineSalesSnapshotIndexedDbService, useValue: snapshotDb },
    { provide: UPDATE_SCHEDULE_ENVIRONMENT, useValue: schedule },
    { provide: REFRESH_LOCK_MANAGER, useValue: locks },
  ];
  if (options.withSwUpdate !== false) providers.push({ provide: SwUpdate, useValue: swUpdate });
  TestBed.configureTestingModule({ providers: providers as never[] });

  return {
    service: TestBed.inject(OfflinePackUpdateService),
    swUpdate,
    versions,
    unrecoverable,
    pack,
    canReachApi,
    leave,
    shopContext,
    appReload,
    settings,
    snapshotSync,
    listeners,
    timers,
    doc,
    clock,
    shared,
    locks,
  };
}

/** Lets a finished cycle release its one-at-a-time slot. */
const settled = () => new Promise((resolve) => setTimeout(resolve, 0));

describe('OfflinePackUpdateService', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('does nothing without a service worker (development builds)', () => {
    for (const options of [{ withSwUpdate: false }, { swEnabled: false }]) {
      const harness = setup(options);
      harness.service.start();
      expect(harness.pack.reconcile).not.toHaveBeenCalled();
      expect(harness.timers).toEqual([]);
      TestBed.resetTestingModule();
    }
  });

  it('writes the flag, then checks for a new version, without making start wait', async () => {
    const harness = setup();
    let finishReconcile!: () => void;
    harness.pack.reconcile.mockReturnValue(new Promise<undefined>((resolve) => (finishReconcile = () => resolve(undefined))));

    harness.service.start();
    harness.service.start();
    expect(harness.pack.reconcile).toHaveBeenCalledTimes(1);
    expect(harness.pack.checkForUpdate).not.toHaveBeenCalled();

    finishReconcile();
    await vi.waitFor(() => expect(harness.pack.checkForUpdate).toHaveBeenCalledTimes(1));
  });

  it('checks every 30 minutes only while visible with the API reachable, and on online and visibility', async () => {
    const harness = setup();
    harness.service.start();
    await vi.waitFor(() => expect(harness.pack.checkForUpdate).toHaveBeenCalledTimes(1));
    expect(harness.timers.map((timer) => timer.ms)).toEqual([UPDATE_CHECK_INTERVAL_MS]);

    harness.doc.visibilityState = 'hidden';
    harness.timers[0].callback();
    harness.doc.visibilityState = 'visible';
    harness.canReachApi.set(false);
    harness.timers[0].callback();
    expect(harness.pack.checkForUpdate).toHaveBeenCalledTimes(1);

    harness.canReachApi.set(true);
    await harness.service.runCycle();
    harness.timers[0].callback();
    await vi.waitFor(() => expect(harness.pack.checkForUpdate).toHaveBeenCalledTimes(3));

    await settled();
    harness.clock.now += UPDATE_TRIGGER_MIN_GAP_MS;
    harness.listeners.window.get('online')?.();
    await vi.waitFor(() => expect(harness.pack.checkForUpdate).toHaveBeenCalledTimes(4));

    await settled();
    harness.clock.now += UPDATE_TRIGGER_MIN_GAP_MS;
    harness.doc.visibilityState = 'hidden';
    harness.listeners.document.get('visibilitychange')?.();
    harness.doc.visibilityState = 'visible';
    harness.listeners.document.get('visibilitychange')?.();
    await vi.waitFor(() => expect(harness.pack.checkForUpdate).toHaveBeenCalledTimes(5));
  });

  it('holds focus and online triggers to a minimum gap after any cycle on the device, but not the timer or a request', async () => {
    const harness = setup();
    harness.service.start();
    await vi.waitFor(() => expect(harness.pack.checkForUpdate).toHaveBeenCalledTimes(1));
    await settled();

    harness.listeners.window.get('online')?.();
    harness.listeners.document.get('visibilitychange')?.();
    await harness.service.runCycle('focus');
    expect(harness.pack.checkForUpdate).toHaveBeenCalledTimes(1);

    harness.timers[0].callback();
    await vi.waitFor(() => expect(harness.pack.checkForUpdate).toHaveBeenCalledTimes(2));
    await settled();
    await harness.service.runCycle();
    expect(harness.pack.checkForUpdate).toHaveBeenCalledTimes(3);

    harness.clock.now += UPDATE_TRIGGER_MIN_GAP_MS - 1;
    await harness.service.runCycle('online');
    expect(harness.pack.checkForUpdate).toHaveBeenCalledTimes(3);
    harness.clock.now += 1;
    await harness.service.runCycle('online');
    expect(harness.pack.checkForUpdate).toHaveBeenCalledTimes(4);
  });

  it("runs one tab at a time: a tab that waited for another tab's cycle skips its own focus check", async () => {
    const harness = setup();
    harness.clock.now += UPDATE_TRIGGER_MIN_GAP_MS;
    let releaseOtherTab!: () => void;
    // Another tab holds the cycle lock.
    void harness.locks.request(CYCLE_LOCK, { mode: 'exclusive' }, () => new Promise<void>((resolve) => (releaseOtherTab = resolve)));

    const waiting = harness.service.runCycle('focus');
    await settled();
    expect(harness.pack.checkForUpdate).not.toHaveBeenCalled();
    expect(harness.locks.names).toEqual([CYCLE_LOCK, CYCLE_LOCK]);

    // The other tab records its cycle, then lets go.
    harness.shared.set('intelibill.offlineSales.offlinePackCycle.v1', JSON.stringify({ lastRunAt: harness.clock.now }));
    releaseOtherTab();
    await waiting;

    expect(harness.pack.checkForUpdate).not.toHaveBeenCalled();
  });

  it('never runs two checks at once', async () => {
    const harness = setup();
    let finish!: () => void;
    harness.pack.checkForUpdate.mockReturnValue(new Promise((resolve) => (finish = () => resolve(false))));

    const first = harness.service.runCycle();
    const second = harness.service.runCycle();
    expect(second).toBe(first);
    finish();
    await first;

    expect(harness.pack.checkForUpdate).toHaveBeenCalledTimes(1);
  });

  it('offers a new build without applying it, and activates a pack-only switch of the same build at once', () => {
    const harness = setup();
    harness.service.start();

    harness.versions.next(ready('build-1', 'build-1'));
    expect(harness.swUpdate.activateUpdate).toHaveBeenCalledTimes(1);
    expect(harness.service.updateReady()).toBe(false);

    harness.versions.next(ready('build-1', 'build-2'));
    expect(harness.service.updateReady()).toBe(true);
    expect(harness.swUpdate.activateUpdate).toHaveBeenCalledTimes(1);
    expect(harness.appReload.reloadPage).not.toHaveBeenCalled();
  });

  it('tells the person to reload when the version is unrecoverable', () => {
    const harness = setup();
    harness.service.start();

    harness.unrecoverable.next({ type: 'UNRECOVERABLE_STATE', reason: 'missing chunk' });

    expect(harness.service.unrecoverable()).toBe(true);
  });

  it('applies an update only after every draft is saved, then reloads', async () => {
    const harness = setup();

    await harness.service.applyUpdate();

    expect(harness.shopContext.prepareToLeave).toHaveBeenCalledTimes(1);
    expect(harness.swUpdate.activateUpdate).toHaveBeenCalledTimes(1);
    expect(harness.appReload.reloadPage).toHaveBeenCalledTimes(1);
    expect(harness.shopContext.prepareToLeave.mock.invocationCallOrder[0]).toBeLessThan(
      harness.swUpdate.activateUpdate.mock.invocationCallOrder[0],
    );
  });

  it('stays on the page with a message while a write is in flight or a draft could not be saved', async () => {
    const harness = setup();
    harness.leave.value = { ok: false, reason: 'mutation-in-flight' };

    await harness.service.applyUpdate();

    expect(harness.service.applyErrorKey()).toBe('shell.appUpdate.busy');
    expect(harness.swUpdate.activateUpdate).not.toHaveBeenCalled();
    expect(harness.appReload.reloadPage).not.toHaveBeenCalled();
    expect(harness.service.applying()).toBe(false);
  });

  it('leaves the pack and the snapshot alone on a device without offline billing', async () => {
    const harness = setup();

    await harness.service.runCycle();

    expect(harness.pack.refreshStatus).not.toHaveBeenCalled();
    expect(harness.snapshotSync.syncForShop).not.toHaveBeenCalled();
  });

  it('re-verifies the pack on an enabled device, refreshing a snapshot only once it is old', async () => {
    const harness = setup();
    harness.pack.deviceNeedsPack.mockReturnValue(true);

    await harness.service.runCycle();
    expect(harness.pack.refreshStatus).toHaveBeenCalledTimes(1);
    expect(harness.snapshotSync.syncForShop).not.toHaveBeenCalled();

    harness.settings.current = {
      enabled: true,
      lastCompleteSnapshotAt: new Date(Date.now() - SNAPSHOT_REFRESH_AGE_MS - 1).toISOString(),
    };
    await harness.service.runCycle();
    expect(harness.snapshotSync.syncForShop).toHaveBeenCalledWith('shop-1');
    expect(harness.settings.updateSettings).toHaveBeenCalledTimes(1);
    expect(harness.settings.updateSettings.mock.results[0].value).toEqual({
      enabled: true,
      lastCompleteSnapshotAt: '2026-11-10T12:00:00.000Z',
    });

    harness.canReachApi.set(false);
    harness.settings.current = { enabled: true, lastCompleteSnapshotAt: null };
    await harness.service.runCycle();
    expect(harness.snapshotSync.syncForShop).toHaveBeenCalledTimes(1);
  });

  it('backs off after a snapshot refresh fails, doubling the wait, and resets once one succeeds', async () => {
    const harness = setup();
    harness.pack.deviceNeedsPack.mockReturnValue(true);
    harness.settings.current = { enabled: true, lastCompleteSnapshotAt: null };
    harness.snapshotSync.syncForShop.mockRejectedValue(new Error('stream failed'));

    await harness.service.runCycle();
    expect(harness.snapshotSync.syncForShop).toHaveBeenCalledTimes(1);

    await harness.service.runCycle();
    harness.clock.now += SNAPSHOT_RETRY_BASE_MS - 1;
    await harness.service.runCycle();
    expect(harness.snapshotSync.syncForShop).toHaveBeenCalledTimes(1);

    harness.clock.now += 1;
    await harness.service.runCycle();
    expect(harness.snapshotSync.syncForShop).toHaveBeenCalledTimes(2);

    // Second failure in a row: twice the wait.
    harness.clock.now += SNAPSHOT_RETRY_BASE_MS;
    await harness.service.runCycle();
    expect(harness.snapshotSync.syncForShop).toHaveBeenCalledTimes(2);
    harness.clock.now += SNAPSHOT_RETRY_BASE_MS;
    harness.snapshotSync.syncForShop.mockResolvedValue(undefined);
    await harness.service.runCycle();
    expect(harness.snapshotSync.syncForShop).toHaveBeenCalledTimes(3);
    expect(harness.settings.updateSettings).toHaveBeenCalledTimes(1);

    // Succeeded: a stale snapshot later is refreshed without any wait.
    harness.settings.current = { enabled: true, lastCompleteSnapshotAt: null };
    await harness.service.runCycle();
    expect(harness.snapshotSync.syncForShop).toHaveBeenCalledTimes(4);
  });

  it('does not judge the pack while the worker has not finished its check (a new build may still be downloading)', async () => {
    const harness = setup();
    harness.pack.deviceNeedsPack.mockReturnValue(true);
    harness.pack.checkForUpdate.mockResolvedValue(null);

    await harness.service.runCycle();

    expect(harness.pack.refreshStatus).not.toHaveBeenCalled();
  });

  it('marks the pack incomplete when an install fails on an enabled device', () => {
    const harness = setup();
    harness.service.start();
    harness.pack.deviceNeedsPack.mockReturnValue(true);

    harness.versions.next({ type: 'VERSION_INSTALLATION_FAILED', version: { hash: 'h' }, error: 'boom' });

    expect(harness.pack.markIncomplete).toHaveBeenCalledTimes(1);
  });
});
