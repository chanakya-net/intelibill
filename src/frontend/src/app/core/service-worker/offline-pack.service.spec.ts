import { TestBed } from '@angular/core/testing';
import { SwUpdate, type VersionEvent } from '@angular/service-worker';
import { Subject } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { OfflinePackStatus, OfflinePackStatusStorage } from '../storage/offline-pack-status.storage';
import { OfflineSalesDeviceSettingsStorage } from '../storage/offline-sales-device-settings.storage';
import { OFFLINE_PACK_STATE_CACHE, angularManifestHash } from './offline-pack-flag';
import {
  OFFLINE_PACK_ENVIRONMENT,
  OFFLINE_PACK_MIN_FREE_BYTES,
  OFFLINE_PACK_READY_TIMEOUT_MS,
  OfflinePackService,
  type OfflinePackEnvironment,
} from './offline-pack.service';

const ORIGIN = 'https://shop.test';

const MANIFEST = {
  configVersion: 1,
  timestamp: 1,
  index: '/index.html',
  appData: { buildId: 'build-1', offlinePack: 'full' },
  assetGroups: [
    { name: 'app', installMode: 'prefetch', updateMode: 'prefetch', urls: ['/index.html', '/main-ABC.js'], patterns: [] },
    { name: 'offline', installMode: 'prefetch', updateMode: 'prefetch', urls: ['/assets/i18n/en-IN.json'], patterns: [] },
    { name: 'assets', installMode: 'lazy', updateMode: 'prefetch', urls: ['/media/icons.ttf'], patterns: [] },
  ],
  dataGroups: [],
  hashTable: {},
  navigationUrls: [],
  navigationRequestStrategy: 'performance',
};

class FakeCache {
  readonly entries = new Map<string, string>();

  async put(request: Request, response: Response): Promise<void> {
    this.entries.set(request.url, await response.text());
  }

  async keys(): Promise<{ url: string }[]> {
    return [...this.entries.keys()].map((url) => ({ url }));
  }
}

class FakeCacheStorage {
  readonly stores = new Map<string, FakeCache>();

  async open(name: string): Promise<FakeCache> {
    let cache = this.stores.get(name);
    if (!cache) {
      cache = new FakeCache();
      this.stores.set(name, cache);
    }
    return cache;
  }

  async has(name: string): Promise<boolean> {
    return this.stores.has(name);
  }

  flag(): string | undefined {
    return [...(this.stores.get(OFFLINE_PACK_STATE_CACHE)?.entries.values() ?? [])][0];
  }

  /** Puts files into the cache Angular's worker would use for `hash`. */
  fill(hash: string, group: string, paths: readonly string[]): void {
    const cache = new FakeCache();
    for (const path of paths) cache.entries.set(`${ORIGIN}${path}`, 'body');
    this.stores.set(`ngsw:/:${hash}:assets:${group}:cache`, cache);
  }
}

interface Harness {
  readonly service: OfflinePackService;
  readonly caches: FakeCacheStorage;
  readonly versions: Subject<VersionEvent>;
  readonly swUpdate: {
    isEnabled: boolean;
    versionUpdates: Subject<VersionEvent>;
    checkForUpdate: ReturnType<typeof vi.fn>;
    activateUpdate: ReturnType<typeof vi.fn>;
  };
  readonly settings: {
    anyShopEnabled: ReturnType<typeof vi.fn>;
    loadPackStatus: ReturnType<typeof vi.fn>;
    savePackStatus: ReturnType<typeof vi.fn>;
  };
  readonly storage: { estimate: ReturnType<typeof vi.fn>; persist: ReturnType<typeof vi.fn>; persisted: ReturnType<typeof vi.fn> };
  readonly clock: { now: number };
  readonly onPoll: { run: () => void };
  driverState: string;
  manifestOk: boolean;
  readonly register: ReturnType<typeof vi.fn>;
}

function setup(options: { swEnabled?: boolean; withSwUpdate?: boolean; controlled?: boolean } = {}): Harness {
  const caches = new FakeCacheStorage();
  const versions = new Subject<VersionEvent>();
  const swUpdate = {
    isEnabled: options.swEnabled ?? true,
    versionUpdates: versions,
    unrecoverable: new Subject(),
    checkForUpdate: vi.fn(async () => false),
    activateUpdate: vi.fn(async () => true),
  };
  const settings = {
    anyShopEnabled: vi.fn(() => false),
    loadPackStatus: vi.fn((): OfflinePackStatus | null => null),
    savePackStatus: vi.fn(),
  };
  const storage = {
    estimate: vi.fn(async () => ({ quota: 1_000_000_000, usage: 1_000 })),
    persist: vi.fn(async () => true),
    persisted: vi.fn(async () => false),
  };
  const clock = { now: Date.parse('2026-11-10T10:00:00Z') };
  const onPoll = { run: () => undefined as void };
  const controllerListeners: (() => void)[] = [];
  // The worker takes control as it activates (Angular's worker claims its clients).
  const register = vi.fn(async () => {
    setTimeout(() => controllerListeners.forEach((listener) => listener()), 0);
    return {};
  });
  const harness = { driverState: 'NORMAL', manifestOk: true } as Harness;

  const environment: OfflinePackEnvironment = {
    caches: caches as unknown as CacheStorage,
    serviceWorker: {
      controller: options.controlled === false ? null : ({} as ServiceWorker),
      getRegistration: async () => ({ scope: `${ORIGIN}/` }) as ServiceWorkerRegistration,
      register,
      addEventListener: (_type: string, listener: () => void) => controllerListeners.push(listener),
      removeEventListener: () => undefined,
    } as unknown as ServiceWorkerContainer,
    storage: storage as unknown as StorageManager,
    fetch: async (input: string) => {
      const url = new URL(input);
      if (url.pathname === '/ngsw.json') {
        expect(url.searchParams.get('ngsw-bypass')).toBe('true');
        return harness.manifestOk ? Response.json(MANIFEST) : new Response('down', { status: 503 });
      }
      if (url.pathname === '/ngsw/state') {
        return new Response(`NGSW Debug Info:\n\nDriver version: 21\nDriver state: ${harness.driverState} ((nominal))`);
      }
      return new Response('missing', { status: 404 });
    },
    baseUrl: () => `${ORIGIN}/`,
    now: () => clock.now,
    delay: async (ms) => {
      clock.now += ms;
      onPoll.run();
    },
  };

  const providers: unknown[] = [
    { provide: OFFLINE_PACK_ENVIRONMENT, useValue: environment },
    { provide: OfflineSalesDeviceSettingsStorage, useValue: settings },
    { provide: OfflinePackStatusStorage, useValue: settings },
  ];
  if (options.withSwUpdate !== false) {
    providers.push({ provide: SwUpdate, useValue: swUpdate });
  }
  TestBed.configureTestingModule({ providers: providers as never[] });

  return Object.assign(harness, {
    service: TestBed.inject(OfflinePackService),
    caches,
    versions,
    swUpdate,
    settings,
    storage,
    clock,
    onPoll,
    register,
  });
}

async function fullHash(): Promise<string> {
  return angularManifestHash(MANIFEST);
}

describe('OfflinePackService', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('skips the pack where no service worker runs (development builds), touching nothing', async () => {
    for (const harness of [setup({ withSwUpdate: false }), (TestBed.resetTestingModule(), setup({ swEnabled: false }))]) {
      expect(harness.service.isAvailable()).toBe(false);
      expect(await harness.service.prepare()).toEqual({ ok: true, skipped: true });
      expect(harness.caches.stores.size).toBe(0);
      expect(harness.swUpdate.checkForUpdate).not.toHaveBeenCalled();
    }
  });

  it('writes an explicit off on a device with no shop enabled, and on when one is', async () => {
    const harness = setup();

    await harness.service.reconcile();
    expect(harness.caches.flag()).toBe('off');
    expect(harness.settings.savePackStatus).toHaveBeenCalledWith(null);

    harness.settings.anyShopEnabled.mockReturnValue(true);
    await harness.service.reconcile();
    expect(harness.caches.flag()).toBe('on');
    expect([...harness.caches.stores.keys()]).toEqual([OFFLINE_PACK_STATE_CACHE]);
  });

  it('refuses to start when the device is short of free storage, before writing the flag', async () => {
    const harness = setup();
    harness.storage.estimate.mockResolvedValue({ quota: OFFLINE_PACK_MIN_FREE_BYTES, usage: 1 });

    expect(await harness.service.prepare()).toEqual({ ok: false, failure: 'STORAGE_LOW' });
    expect(harness.caches.flag()).toBeUndefined();
  });

  it('succeeds only once every prefetched file is in the worker caches (a first install sends no VERSION_READY)', async () => {
    const harness = setup();
    const hash = await fullHash();
    let polls = 0;
    harness.onPoll.run = () => {
      polls += 1;
      if (polls === 1) harness.caches.fill(hash, 'app', ['/index.html', '/main-ABC.js']);
      if (polls === 3) harness.caches.fill(hash, 'offline', ['/assets/i18n/en-IN.json']);
    };

    const result = await harness.service.prepare();

    expect(result).toEqual({
      ok: true,
      skipped: false,
      status: {
        state: 'ready',
        buildId: 'build-1',
        manifestHash: hash,
        persisted: true,
        lastCheckedAt: '2026-11-10T10:00:03.000Z',
        lastUpdatedAt: '2026-11-10T10:00:03.000Z',
      },
    });
    expect(polls).toBe(3);
    expect(harness.caches.flag()).toBe('on');
    expect(harness.storage.persist).toHaveBeenCalled();
    expect(harness.swUpdate.checkForUpdate).toHaveBeenCalled();
    // The lazy group is not part of the proof: its files arrive only when asked for.
    expect(harness.service.status()?.state).toBe('ready');
    expect(harness.settings.savePackStatus).toHaveBeenLastCalledWith(result.ok && !result.skipped ? result.status : null);
  });

  it('activates the full version in this tab when only the pack choice changed (same build)', async () => {
    const harness = setup();
    const hash = await fullHash();
    harness.onPoll.run = () => {
      harness.versions.next({
        type: 'VERSION_READY',
        currentVersion: { hash: 'lite-hash', appData: { buildId: 'build-1', offlinePack: 'lite' } },
        latestVersion: { hash, appData: { buildId: 'build-1', offlinePack: 'full' } },
      });
      harness.caches.fill(hash, 'app', ['/index.html', '/main-ABC.js']);
      harness.caches.fill(hash, 'offline', ['/assets/i18n/en-IN.json']);
    };

    expect((await harness.service.prepare()).ok).toBe(true);
    expect(harness.swUpdate.activateUpdate).toHaveBeenCalledTimes(1);
  });

  it('leaves a different build to the update control, which reloads', async () => {
    const harness = setup();
    const hash = await fullHash();
    harness.onPoll.run = () => {
      harness.versions.next({
        type: 'VERSION_READY',
        currentVersion: { hash: 'old', appData: { buildId: 'build-0', offlinePack: 'lite' } },
        latestVersion: { hash, appData: { buildId: 'build-1', offlinePack: 'full' } },
      });
      harness.caches.fill(hash, 'app', ['/index.html', '/main-ABC.js']);
      harness.caches.fill(hash, 'offline', ['/assets/i18n/en-IN.json']);
    };

    expect((await harness.service.prepare()).ok).toBe(true);
    expect(harness.swUpdate.activateUpdate).not.toHaveBeenCalled();
  });

  it('fails at once when the worker reports the full version failed to install', async () => {
    const harness = setup();
    const hash = await fullHash();
    harness.onPoll.run = () =>
      harness.versions.next({ type: 'VERSION_INSTALLATION_FAILED', version: { hash }, error: 'chunk 500' });

    expect(await harness.service.prepare()).toEqual({ ok: false, failure: 'INSTALL_FAILED' });
    expect(harness.clock.now - Date.parse('2026-11-10T10:00:00Z')).toBeLessThan(OFFLINE_PACK_READY_TIMEOUT_MS);
  });

  it('gives up after the time limit when the pack never completes', async () => {
    const harness = setup();
    const hash = await fullHash();
    harness.caches.fill(hash, 'app', ['/index.html']);

    expect(await harness.service.prepare()).toEqual({ ok: false, failure: 'TIMEOUT' });
  });

  it('fails when the deployed manifest cannot be fetched', async () => {
    const harness = setup();
    harness.manifestOk = false;

    expect(await harness.service.prepare()).toEqual({ ok: false, failure: 'UNREACHABLE' });
  });

  it('registers the worker itself when enabling comes before the app registered it', async () => {
    const harness = setup({ controlled: false });
    const hash = await fullHash();
    harness.caches.fill(hash, 'app', ['/index.html', '/main-ABC.js']);
    harness.caches.fill(hash, 'offline', ['/assets/i18n/en-IN.json']);

    expect((await harness.service.prepare()).ok).toBe(true);
    expect(harness.register).toHaveBeenCalledWith(`${ORIGIN}/ngsw-worker.js`);
  });

  it('does not count a pack the worker is not serving after a failed install as complete', async () => {
    const harness = setup();
    const hash = await fullHash();
    harness.caches.fill(hash, 'app', ['/index.html', '/main-ABC.js']);
    harness.caches.fill(hash, 'offline', ['/assets/i18n/en-IN.json']);
    harness.driverState = 'EXISTING_CLIENTS_ONLY';

    const verification = await harness.service.verify();

    expect(verification?.complete).toBe(false);
    expect(verification?.missing).toEqual([]);
  });

  it('records a pack found incomplete on a later check, and leaves the last state when offline', async () => {
    const harness = setup();
    harness.settings.anyShopEnabled.mockReturnValue(true);
    const hash = await fullHash();
    harness.caches.fill(hash, 'app', ['/index.html']);

    expect((await harness.service.refreshStatus())?.state).toBe('incomplete');

    harness.manifestOk = false;
    expect((await harness.service.refreshStatus())?.state).toBe('incomplete');
    expect(harness.settings.savePackStatus).toHaveBeenCalledTimes(1);
  });

  it('keeps lastUpdatedAt until a different build is verified', async () => {
    const harness = setup();
    harness.settings.anyShopEnabled.mockReturnValue(true);
    const hash = await fullHash();
    harness.caches.fill(hash, 'app', ['/index.html', '/main-ABC.js']);
    harness.caches.fill(hash, 'offline', ['/assets/i18n/en-IN.json']);

    const first = await harness.service.refreshStatus();
    harness.clock.now += 60_000;
    const second = await harness.service.refreshStatus();

    expect(second?.lastUpdatedAt).toBe(first?.lastUpdatedAt);
    expect(second?.lastCheckedAt).not.toBe(first?.lastCheckedAt);
  });

  it('releases the flag only when no other shop on the device needs the pack', async () => {
    const harness = setup();
    harness.settings.anyShopEnabled.mockReturnValue(true);

    await harness.service.release();
    expect(harness.caches.flag()).toBeUndefined();
    expect(harness.swUpdate.checkForUpdate).not.toHaveBeenCalled();

    harness.settings.anyShopEnabled.mockReturnValue(false);
    await harness.service.release();
    expect(harness.caches.flag()).toBe('off');
    expect(harness.settings.savePackStatus).toHaveBeenCalledWith(null);
    expect(harness.swUpdate.checkForUpdate).toHaveBeenCalledTimes(1);
  });
});
