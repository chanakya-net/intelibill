import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';

import {
  CONTROLLER_HANDOVER_TIMEOUT_MS,
  PROTECTED_CACHE_ENVIRONMENT,
  ProtectedCacheEnvironment,
  ProtectedCacheMigrationService,
} from './protected-cache-migration.service';

const LEGACY = [
  'ngsw:/:1:data:api-fresh:cache',
  'ngsw:/:db:1:data:api-fresh:age',
  'ngsw:/:db:1:data:api-fresh:lru',
  'ngsw:/:1:data:offline-sales-api-no-cache:cache',
];
const KEPT = ['ngsw:/:abc123:assets:app:cache', 'ngsw:/:db:control', 'intelibill-unrelated'];

class FakeCache {
  readonly entries = new Map<string, string>();
  failDelete = false;

  async keys(): Promise<Request[]> {
    return [...this.entries.keys()].map((url) => ({ url }) as Request);
  }

  async delete(request: Request): Promise<boolean> {
    if (this.failDelete) {
      throw new DOMException('quota', 'UnknownError');
    }
    return this.entries.delete(request.url);
  }
}

class FakeCacheStorage {
  readonly stores = new Map<string, FakeCache>();
  failKeys = false;

  constructor(names: readonly string[]) {
    for (const name of names) {
      const cache = new FakeCache();
      cache.entries.set('https://shop.test/api/customers', `body for ${name}`);
      this.stores.set(name, cache);
    }
  }

  async keys(): Promise<string[]> {
    if (this.failKeys) {
      throw new DOMException('storage unavailable', 'SecurityError');
    }
    return [...this.stores.keys()];
  }

  async open(name: string): Promise<FakeCache> {
    const existing = this.stores.get(name);
    if (existing) {
      return existing;
    }
    const created = new FakeCache();
    this.stores.set(name, created);
    return created;
  }

  async delete(name: string): Promise<boolean> {
    return this.stores.delete(name);
  }
}

class FakeServiceWorkerContainer extends EventTarget {
  controller: { scriptURL: string } | null;
  readonly register = vi.fn<(script: string, options?: RegistrationOptions) => Promise<unknown>>();

  constructor(scriptUrl: string | null) {
    super();
    this.controller = scriptUrl ? { scriptURL: scriptUrl } : null;
  }

  takeOver(scriptUrl: string): void {
    this.controller = { scriptURL: scriptUrl };
    this.dispatchEvent(new Event('controllerchange'));
  }
}

const LEGACY_WORKER = 'https://shop.test/ngsw-worker.js';
const APP_WORKER = 'https://shop.test/app-worker.js';

function createService(environment: Partial<ProtectedCacheEnvironment>): ProtectedCacheMigrationService {
  TestBed.configureTestingModule({
    providers: [
      {
        provide: PROTECTED_CACHE_ENVIRONMENT,
        useValue: { caches: null, serviceWorker: null, ...environment },
      },
    ],
  });
  return TestBed.inject(ProtectedCacheMigrationService);
}

function asCaches(storage: FakeCacheStorage): CacheStorage {
  return storage as unknown as CacheStorage;
}

function asContainer(container: FakeServiceWorkerContainer): ServiceWorkerContainer {
  return container as unknown as ServiceWorkerContainer;
}

describe('ProtectedCacheMigrationService', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
    TestBed.resetTestingModule();
  });

  it('no_cache_storage_is_safe: a browser without CacheStorage cannot have stored anything', async () => {
    const service = createService({ caches: null });

    await expect(service.ensureSafeVersion()).resolves.toBe('safe');
  });

  it('purges_only_legacy_entries: protected answers go, everything else stays', async () => {
    const storage = new FakeCacheStorage([...LEGACY, ...KEPT]);
    const handles = LEGACY.map((name) => storage.stores.get(name)!);
    const service = createService({ caches: asCaches(storage) });

    await expect(service.ensureSafeVersion()).resolves.toBe('safe');

    expect([...storage.stores.keys()].sort()).toEqual([...KEPT].sort());
    for (const handle of handles) {
      expect(handle.entries.size).toBe(0);
    }
    for (const name of KEPT) {
      expect(storage.stores.get(name)!.entries.size).toBe(1);
    }
  });

  it('cleanup_preserves_unsynced_invoice: no IndexedDB database is opened or deleted', async () => {
    const indexedDb = { open: vi.fn(), deleteDatabase: vi.fn(), databases: vi.fn() };
    vi.stubGlobal('indexedDB', indexedDb);
    const storage = new FakeCacheStorage([...LEGACY, ...KEPT]);
    const service = createService({ caches: asCaches(storage) });

    await service.ensureSafeVersion();
    await service.purgeLegacyCaches();

    expect(indexedDb.open).not.toHaveBeenCalled();
    expect(indexedDb.deleteDatabase).not.toHaveBeenCalled();
    expect(indexedDb.databases).not.toHaveBeenCalled();
  });

  it('accepts an empty legacy cache recreated by an older app version still open elsewhere', async () => {
    const storage = new FakeCacheStorage([]);
    await storage.open('ngsw:/:1:data:api-fresh:cache');
    const service = createService({ caches: asCaches(storage) });

    await expect(service.purgeLegacyCaches()).resolves.toBe(true);
  });

  it.each([null, APP_WORKER])('uncontrolled_or_app_worker_is_safe (controller %s)', async (controller) => {
    const container = new FakeServiceWorkerContainer(controller);
    const service = createService({ caches: asCaches(new FakeCacheStorage(LEGACY)), serviceWorker: asContainer(container) });

    await expect(service.ensureSafeVersion()).resolves.toBe('safe');
    expect(container.register).not.toHaveBeenCalled();
  });

  it('legacy_controller_hands_over: registers the app worker now and purges again once it controls', async () => {
    const storage = new FakeCacheStorage(LEGACY);
    const container = new FakeServiceWorkerContainer(LEGACY_WORKER);
    container.register.mockImplementation(async () => {
      // The older worker served an older tab once more before the new one took over.
      (await storage.open('ngsw:/:1:data:api-fresh:cache')).entries.set('https://shop.test/api/sales', 'late body');
      queueMicrotask(() => container.takeOver(APP_WORKER));
      return {};
    });
    const service = createService({ caches: asCaches(storage), serviceWorker: asContainer(container) });

    await expect(service.ensureSafeVersion()).resolves.toBe('safe');

    expect(container.register).toHaveBeenCalledWith('app-worker.js', { updateViaCache: 'none' });
    expect([...storage.stores.keys()]).toEqual([]);
  });

  it('ignores a controller change that is not the app worker', async () => {
    vi.useFakeTimers();
    const container = new FakeServiceWorkerContainer(LEGACY_WORKER);
    container.register.mockResolvedValue({});
    const service = createService({ caches: asCaches(new FakeCacheStorage([])), serviceWorker: asContainer(container) });

    const result = service.ensureSafeVersion();
    await vi.advanceTimersByTimeAsync(10);
    container.takeOver('https://shop.test/ngsw-worker.js?v=2');
    await vi.advanceTimersByTimeAsync(CONTROLLER_HANDOVER_TIMEOUT_MS);

    await expect(result).resolves.toBe('reloadRequired');
  });

  it('legacy_controller_timeout_is_reload_required: an older worker that never hands over', async () => {
    vi.useFakeTimers();
    const container = new FakeServiceWorkerContainer(LEGACY_WORKER);
    container.register.mockResolvedValue({});
    const service = createService({ caches: asCaches(new FakeCacheStorage(LEGACY)), serviceWorker: asContainer(container) });

    const result = service.ensureSafeVersion();
    await vi.advanceTimersByTimeAsync(CONTROLLER_HANDOVER_TIMEOUT_MS - 1);
    let settled = false;
    void result.then(() => (settled = true));
    await vi.advanceTimersByTimeAsync(0);
    expect(settled).toBe(false);
    await vi.advanceTimersByTimeAsync(1);

    await expect(result).resolves.toBe('reloadRequired');
  });

  it('register_rejects_is_reload_required: offline or missing worker script', async () => {
    const container = new FakeServiceWorkerContainer(LEGACY_WORKER);
    container.register.mockRejectedValue(new TypeError('Failed to fetch'));
    const service = createService({ caches: asCaches(new FakeCacheStorage(LEGACY)), serviceWorker: asContainer(container) });

    await expect(service.ensureSafeVersion()).resolves.toBe('reloadRequired');
  });

  it('keys_throws_is_blocked: unavailable CacheStorage is reported, not ignored', async () => {
    const storage = new FakeCacheStorage(LEGACY);
    storage.failKeys = true;
    const service = createService({ caches: asCaches(storage) });

    await expect(service.ensureSafeVersion()).resolves.toBe('blocked');
  });

  it('delete_throws_midway_is_blocked_then_retry_succeeds: interrupted cleanup is repeatable', async () => {
    const storage = new FakeCacheStorage([...LEGACY, ...KEPT]);
    storage.stores.get('ngsw:/:db:1:data:api-fresh:age')!.failDelete = true;
    const service = createService({ caches: asCaches(storage) });

    await expect(service.ensureSafeVersion()).resolves.toBe('blocked');
    expect(storage.stores.has('ngsw:/:db:1:data:api-fresh:age')).toBe(true);
    for (const name of KEPT) {
      expect(storage.stores.has(name)).toBe(true);
    }

    storage.stores.get('ngsw:/:db:1:data:api-fresh:age')!.failDelete = false;

    await expect(service.ensureSafeVersion()).resolves.toBe('safe');
    expect([...storage.stores.keys()].sort()).toEqual([...KEPT].sort());
  });
});
