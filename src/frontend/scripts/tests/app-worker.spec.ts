import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import vm from 'node:vm';

import { describe, expect, it } from 'vitest';

import { isLegacyProtectedCacheName } from '../../src/app/core/service-worker/protected-cache-names';

const frontendRoot = join(import.meta.dir, '..', '..');
const workerSource = readFileSync(join(frontendRoot, 'public', 'app-worker.js'), 'utf8');

const ORIGIN = 'https://shop.test';

/** The cache names a deployed worker can hold: Angular 21's naming for the old config, plus neighbours. */
const CACHE_FIXTURES = [
  'ngsw:/:1:data:api-fresh:cache',
  'ngsw:/:db:1:data:api-fresh:age',
  'ngsw:/:db:1:data:api-fresh:lru',
  'ngsw:/:1:data:ping-no-cache:cache',
  'ngsw:/:db:1:data:ping-no-cache:age',
  'ngsw:/:db:1:data:ping-no-cache:lru',
  'ngsw:/:1:data:offline-sales-api-no-cache:cache',
  'ngsw:/:db:1:data:offline-sales-api-no-cache:age',
  'ngsw:/:db:1:data:offline-sales-api-no-cache:lru',
  'ngsw:/:2:data:api-fresh:cache',
  'ngsw:/:abc123:assets:app:cache',
  'ngsw:/:db:abc123:assets:app:meta',
  'ngsw:/:db:control',
  'ngsw:/:1:data:api-fresh-reports:cache',
  'other-cache',
];

class FakeCache {
  readonly entries = new Map<string, string>();

  async keys(): Promise<{ url: string }[]> {
    return [...this.entries.keys()].map((url) => ({ url }));
  }

  async delete(request: { url: string }): Promise<boolean> {
    return this.entries.delete(request.url);
  }
}

class FakeCacheStorage {
  readonly stores = new Map<string, FakeCache>();

  constructor(names: readonly string[]) {
    for (const name of names) {
      const cache = new FakeCache();
      cache.entries.set(`${ORIGIN}/api/customers`, 'shop-a body');
      this.stores.set(name, cache);
    }
  }

  async keys(): Promise<string[]> {
    return [...this.stores.keys()];
  }

  async open(name: string): Promise<FakeCache> {
    let cache = this.stores.get(name);
    if (!cache) {
      cache = new FakeCache();
      this.stores.set(name, cache);
    }
    return cache;
  }

  async delete(name: string): Promise<boolean> {
    return this.stores.delete(name);
  }
}

interface LoadedWorker {
  readonly steps: string[];
  readonly caches: FakeCacheStorage;
  dispatchFetch(url: string): { stopped: boolean; responded: boolean };
  dispatchActivate(): Promise<void>;
}

function loadWorker(cacheNames: readonly string[] = CACHE_FIXTURES): LoadedWorker {
  const steps: string[] = [];
  const listeners = new Map<string, ((event: unknown) => void)[]>();
  const caches = new FakeCacheStorage(cacheNames);
  const location = new URL(`${ORIGIN}/app-worker.js`);
  const self = {
    location,
    caches,
    addEventListener(type: string, listener: (event: unknown) => void) {
      steps.push(`listen:${type}`);
      listeners.set(type, [...(listeners.get(type) ?? []), listener]);
    },
    importScripts(...urls: string[]) {
      steps.push(`import:${urls.join(',')}`);
    },
  };

  vm.runInNewContext(workerSource, { self, URL, Promise });

  return {
    steps,
    caches,
    dispatchFetch(url: string) {
      const outcome = { stopped: false, responded: false };
      const event = {
        request: { url, method: 'GET' },
        stopImmediatePropagation: () => {
          outcome.stopped = true;
        },
        respondWith: () => {
          outcome.responded = true;
        },
      };
      for (const listener of listeners.get('fetch') ?? []) {
        listener(event);
        if (outcome.stopped) break;
      }
      return outcome;
    },
    async dispatchActivate() {
      const pending: Promise<unknown>[] = [];
      const event = { waitUntil: (promise: Promise<unknown>) => pending.push(promise) };
      for (const listener of listeners.get('activate') ?? []) {
        listener(event);
      }
      await Promise.all(pending);
    },
  };
}

describe('app-worker.js', () => {
  it('listens for fetches before Angular does', () => {
    const worker = loadWorker();

    const fetchListener = worker.steps.indexOf('listen:fetch');
    const angular = worker.steps.indexOf('import:./ngsw-worker.js');
    expect(fetchListener).toBeGreaterThanOrEqual(0);
    expect(angular).toBeGreaterThan(fetchListener);
  });

  it.each([
    `${ORIGIN}/api/customers`,
    `${ORIGIN}/api/sales/123?include=lines`,
    `${ORIGIN}/api/ping?ngsw-bypass=true`,
    `${ORIGIN}/hubs/products/negotiate?negotiateVersion=1`,
  ])('hands %s to the network without Angular', (url) => {
    const outcome = loadWorker().dispatchFetch(url);

    expect(outcome).toEqual({ stopped: true, responded: false });
  });

  it.each([
    `${ORIGIN}/`,
    `${ORIGIN}/sales/new`,
    `${ORIGIN}/main-ABC123.js`,
    `${ORIGIN}/assets/i18n/en-IN.json`,
    `${ORIGIN}/apis/customers`,
    'https://cdn.test/api/customers',
  ])('leaves %s to Angular', (url) => {
    const outcome = loadWorker().dispatchFetch(url);

    expect(outcome).toEqual({ stopped: false, responded: false });
  });

  it('empties and removes exactly the legacy protected caches on activation', async () => {
    const worker = loadWorker();
    const legacy = CACHE_FIXTURES.filter(isLegacyProtectedCacheName);
    const handles = await Promise.all(legacy.map((name) => worker.caches.open(name)));

    await worker.dispatchActivate();

    expect(legacy).toEqual([
      'ngsw:/:1:data:api-fresh:cache',
      'ngsw:/:db:1:data:api-fresh:age',
      'ngsw:/:db:1:data:api-fresh:lru',
      'ngsw:/:1:data:ping-no-cache:cache',
      'ngsw:/:db:1:data:ping-no-cache:age',
      'ngsw:/:db:1:data:ping-no-cache:lru',
      'ngsw:/:1:data:offline-sales-api-no-cache:cache',
      'ngsw:/:db:1:data:offline-sales-api-no-cache:age',
      'ngsw:/:db:1:data:offline-sales-api-no-cache:lru',
      'ngsw:/:2:data:api-fresh:cache',
    ]);
    // A live old worker may still hold a handle to the cache it opened: the entries go first.
    for (const handle of handles) {
      expect(handle.entries.size).toBe(0);
    }
    expect([...worker.caches.stores.keys()].sort()).toEqual(
      [
        'ngsw:/:abc123:assets:app:cache',
        'ngsw:/:db:abc123:assets:app:meta',
        'ngsw:/:db:control',
        'ngsw:/:1:data:api-fresh-reports:cache',
        'other-cache',
      ].sort(),
    );
    expect(worker.caches.stores.get('ngsw:/:abc123:assets:app:cache')?.entries.size).toBe(1);
  });

  it('still activates when the cache storage fails', async () => {
    const worker = loadWorker();
    worker.caches.keys = async () => {
      throw new Error('storage unavailable');
    };

    await expect(worker.dispatchActivate()).resolves.toBeUndefined();
  });
});
