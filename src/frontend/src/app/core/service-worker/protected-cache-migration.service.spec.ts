import { TestBed } from '@angular/core/testing';
import { SwRegistrationOptions } from '@angular/service-worker';
import { afterEach, describe, expect, it, vi } from 'vitest';

import {
  PROTECTED_CACHE_ENVIRONMENT,
  ProtectedCacheEnvironment,
  ProtectedCacheMigrationService,
  WORKER_HANDOVER_TIMEOUT_MS,
  WORKER_QUERY_TIMEOUT_MS,
  WORKER_UPDATE_TIMEOUT_MS,
} from './protected-cache-migration.service';
import { PROTECTED_WORKER_PROTOCOL, PROTECTED_WORKER_QUERY } from './service-worker-registration';

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
  keysError: DOMException | null = null;

  constructor(names: readonly string[]) {
    for (const name of names) {
      const cache = new FakeCache();
      cache.entries.set('https://shop.test/api/customers', `body for ${name}`);
      this.stores.set(name, cache);
    }
  }

  async keys(): Promise<string[]> {
    if (this.keysError) {
      throw this.keysError;
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

/** A worker at `ngsw-worker.js`: Intelibill's answers the isolation query, an earlier one stays silent. */
class FakeWorker extends EventTarget {
  readonly queries: unknown[] = [];

  constructor(
    private readonly isProtected: boolean,
    public state: ServiceWorkerState = 'activated',
  ) {
    super();
  }

  postMessage(message: { action?: string }, transfer: MessagePort[]): void {
    this.queries.push(message);
    if (this.isProtected && message.action === PROTECTED_WORKER_QUERY) {
      transfer[0].postMessage({ protectedApiIsolation: PROTECTED_WORKER_PROTOCOL });
    }
  }

  moveTo(state: ServiceWorkerState): void {
    this.state = state;
    this.dispatchEvent(new Event('statechange'));
  }
}

class FakeRegistration {
  installing: FakeWorker | null = null;
  waiting: FakeWorker | null = null;
  readonly update = vi.fn<() => Promise<void>>();

  constructor(public active: FakeWorker | null) {}

  /** What the browser does when the script at the URL changed: install, skip waiting, activate. */
  deliver(next: FakeWorker): void {
    this.installing = next;
    queueMicrotask(() => {
      this.installing = null;
      this.active = next;
      next.moveTo('activated');
    });
  }
}

class FakeContainer extends EventTarget {
  readonly getRegistration = vi.fn<() => Promise<FakeRegistration | undefined>>();

  constructor(registration: FakeRegistration | undefined) {
    super();
    this.getRegistration.mockResolvedValue(registration);
  }
}

function createService(environment: Partial<ProtectedCacheEnvironment>, workerEnabled = true): ProtectedCacheMigrationService {
  TestBed.configureTestingModule({
    providers: [
      { provide: PROTECTED_CACHE_ENVIRONMENT, useValue: { caches: null, serviceWorker: null, ...environment } },
      { provide: SwRegistrationOptions, useValue: { enabled: workerEnabled } },
    ],
  });
  return TestBed.inject(ProtectedCacheMigrationService);
}

function asCaches(storage: FakeCacheStorage): CacheStorage {
  return storage as unknown as CacheStorage;
}

function asContainer(container: FakeContainer): ServiceWorkerContainer {
  return container as unknown as ServiceWorkerContainer;
}

describe('ProtectedCacheMigrationService', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
    TestBed.resetTestingModule();
  });

  describe('stored answers', () => {
    it('no_cache_storage_is_safe: a browser without CacheStorage cannot have stored anything', async () => {
      await expect(createService({ caches: null }).ensureSafeVersion()).resolves.toBe('safe');
    });

    it('purges_only_legacy_entries: protected answers go, everything else stays', async () => {
      const storage = new FakeCacheStorage([...LEGACY, ...KEPT]);
      const handles = LEGACY.map((name) => storage.stores.get(name)!);

      await expect(createService({ caches: asCaches(storage) }).ensureSafeVersion()).resolves.toBe('safe');

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
      const service = createService({ caches: asCaches(new FakeCacheStorage([...LEGACY, ...KEPT])) });

      await service.ensureSafeVersion();
      await service.purgeLegacyCaches();

      expect(indexedDb.open).not.toHaveBeenCalled();
      expect(indexedDb.deleteDatabase).not.toHaveBeenCalled();
      expect(indexedDb.databases).not.toHaveBeenCalled();
    });

    it('accepts an empty legacy cache recreated by an older app version still open elsewhere', async () => {
      const storage = new FakeCacheStorage([]);
      await storage.open('ngsw:/:1:data:api-fresh:cache');

      await expect(createService({ caches: asCaches(storage) }).purgeLegacyCaches()).resolves.toBe(true);
    });

    it('treats storage the browser denies this origin as holding nothing a worker could read', async () => {
      const storage = new FakeCacheStorage(LEGACY);
      storage.keysError = new DOMException('denied', 'SecurityError');

      await expect(createService({ caches: asCaches(storage) }).ensureSafeVersion()).resolves.toBe('safe');
    });

    it('keys_throws_is_blocked: a failing CacheStorage is reported, not ignored', async () => {
      const storage = new FakeCacheStorage(LEGACY);
      storage.keysError = new DOMException('broken', 'UnknownError');

      await expect(createService({ caches: asCaches(storage) }).ensureSafeVersion()).resolves.toBe('blocked');
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

  describe('the worker serving this origin', () => {
    it('leaves Intelibill’s worker alone', async () => {
      const registration = new FakeRegistration(new FakeWorker(true));
      const service = createService({
        caches: asCaches(new FakeCacheStorage(LEGACY)),
        serviceWorker: asContainer(new FakeContainer(registration)),
      });

      await expect(service.ensureSafeVersion()).resolves.toBe('safe');
      expect(registration.update).not.toHaveBeenCalled();
    });

    it('has nothing to replace before any worker is registered', async () => {
      const container = new FakeContainer(undefined);
      const service = createService({ caches: asCaches(new FakeCacheStorage([])), serviceWorker: asContainer(container) });

      await expect(service.ensureSafeVersion()).resolves.toBe('safe');
    });

    it('earlier_worker_is_replaced_now: fetches the current script at once and purges what it stored meanwhile', async () => {
      const storage = new FakeCacheStorage(LEGACY);
      const earlier = new FakeWorker(false);
      const registration = new FakeRegistration(earlier);
      registration.update.mockImplementation(async () => {
        // The earlier worker served an older tab once more before it was replaced.
        (await storage.open('ngsw:/:1:data:api-fresh:cache')).entries.set('https://shop.test/api/sales', 'late body');
        registration.deliver(new FakeWorker(true, 'installing'));
      });
      const service = createService({
        caches: asCaches(storage),
        serviceWorker: asContainer(new FakeContainer(registration)),
      });

      await expect(service.ensureSafeVersion()).resolves.toBe('safe');

      expect(earlier.queries).toEqual([{ action: PROTECTED_WORKER_QUERY }]);
      expect(registration.update).toHaveBeenCalledTimes(1);
      expect([...storage.stores.keys()]).toEqual([]);
    });

    it('update_rejects_is_reload_required: offline, or the script is missing', async () => {
      vi.useFakeTimers();
      const registration = new FakeRegistration(new FakeWorker(false));
      registration.update.mockRejectedValue(new TypeError('Failed to fetch'));
      const service = createService({
        caches: asCaches(new FakeCacheStorage([])),
        serviceWorker: asContainer(new FakeContainer(registration)),
      });

      const result = service.ensureSafeVersion();
      await vi.advanceTimersByTimeAsync(WORKER_QUERY_TIMEOUT_MS);

      await expect(result).resolves.toBe('reloadRequired');
    });

    it('is reload required when the update check never settles, and stays free to check again', async () => {
      vi.useFakeTimers();
      const registration = new FakeRegistration(new FakeWorker(false));
      registration.update.mockReturnValue(new Promise(() => undefined));
      const service = createService({
        caches: asCaches(new FakeCacheStorage([])),
        serviceWorker: asContainer(new FakeContainer(registration)),
      });

      const result = service.ensureSafeVersion();
      await vi.advanceTimersByTimeAsync(WORKER_QUERY_TIMEOUT_MS + WORKER_UPDATE_TIMEOUT_MS);

      await expect(result).resolves.toBe('reloadRequired');
    });

    it('is reload required when the server still serves the earlier script', async () => {
      vi.useFakeTimers();
      const registration = new FakeRegistration(new FakeWorker(false));
      registration.update.mockResolvedValue(undefined);
      const service = createService({
        caches: asCaches(new FakeCacheStorage([])),
        serviceWorker: asContainer(new FakeContainer(registration)),
      });

      const result = service.ensureSafeVersion();
      await vi.advanceTimersByTimeAsync(WORKER_QUERY_TIMEOUT_MS * 2);

      await expect(result).resolves.toBe('reloadRequired');
    });

    it('handover_timeout_is_reload_required: a replacement that never activates', async () => {
      vi.useFakeTimers();
      const registration = new FakeRegistration(new FakeWorker(false));
      registration.update.mockImplementation(async () => {
        registration.installing = new FakeWorker(true, 'installing');
      });
      const service = createService({
        caches: asCaches(new FakeCacheStorage([])),
        serviceWorker: asContainer(new FakeContainer(registration)),
      });

      const result = service.ensureSafeVersion();
      let settled = false;
      void result.then(() => (settled = true));
      await vi.advanceTimersByTimeAsync(WORKER_QUERY_TIMEOUT_MS + WORKER_HANDOVER_TIMEOUT_MS - 1);
      expect(settled).toBe(false);
      await vi.advanceTimersByTimeAsync(1);

      await expect(result).resolves.toBe('reloadRequired');
    });

    it('is reload required when the replacement fails to install', async () => {
      vi.useFakeTimers();
      const registration = new FakeRegistration(new FakeWorker(false));
      registration.update.mockImplementation(async () => {
        const incoming = new FakeWorker(true, 'installing');
        registration.installing = incoming;
        queueMicrotask(() => incoming.moveTo('redundant'));
      });
      const service = createService({
        caches: asCaches(new FakeCacheStorage([])),
        serviceWorker: asContainer(new FakeContainer(registration)),
      });

      const result = service.ensureSafeVersion();
      await vi.advanceTimersByTimeAsync(WORKER_QUERY_TIMEOUT_MS);

      await expect(result).resolves.toBe('reloadRequired');
    });

    it('reads a missing enabled flag the way Angular does: as on', async () => {
      TestBed.configureTestingModule({
        providers: [
          {
            provide: PROTECTED_CACHE_ENVIRONMENT,
            useValue: {
              caches: asCaches(new FakeCacheStorage([])),
              serviceWorker: asContainer(new FakeContainer(new FakeRegistration(new FakeWorker(true)))),
            },
          },
          { provide: SwRegistrationOptions, useValue: {} },
        ],
      });
      const container = TestBed.inject(PROTECTED_CACHE_ENVIRONMENT).serviceWorker as unknown as FakeContainer;

      await expect(TestBed.inject(ProtectedCacheMigrationService).ensureSafeVersion()).resolves.toBe('safe');
      expect(container.getRegistration).toHaveBeenCalledTimes(1);
    });

    it('leaves a worker alone where this build registers none (development)', async () => {
      const container = new FakeContainer(new FakeRegistration(new FakeWorker(false)));
      const service = createService({ caches: asCaches(new FakeCacheStorage([])), serviceWorker: asContainer(container) }, false);

      await expect(service.ensureSafeVersion()).resolves.toBe('safe');
      expect(container.getRegistration).not.toHaveBeenCalled();
    });
  });

  describe('watchWorkerChanges', () => {
    it('reports each change of controller until unsubscribed', () => {
      const container = new FakeContainer(undefined);
      const listener = vi.fn();
      const stop = createService({ serviceWorker: asContainer(container) }).watchWorkerChanges(listener);

      container.dispatchEvent(new Event('controllerchange'));
      stop();
      container.dispatchEvent(new Event('controllerchange'));

      expect(listener).toHaveBeenCalledTimes(1);
    });

    it('watches nothing where this build registers no worker', () => {
      const container = new FakeContainer(undefined);
      const listener = vi.fn();
      createService({ serviceWorker: asContainer(container) }, false).watchWorkerChanges(listener);

      container.dispatchEvent(new Event('controllerchange'));

      expect(listener).not.toHaveBeenCalled();
    });
  });
});
