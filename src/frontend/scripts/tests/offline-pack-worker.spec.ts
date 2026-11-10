import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import vm from 'node:vm';

import { Generator, type Config, type Filesystem } from '@angular/service-worker/config';
import { describe, expect, it } from 'vitest';

import {
  OFFLINE_PACK_STATE_CACHE,
  OFFLINE_PACK_STATE_KEY,
} from '../../src/app/core/service-worker/offline-pack-flag';

/**
 * The offline-pack manifest switch in `service-worker/protected-api-worker.js`
 * (docs/frontend-architecture.md § Offline Pack): the wrapper answers Angular's `ngsw.json` request
 * with `ngsw-lite.json` only when the page wrote an explicit "off", and passes everything else
 * through. The second half runs the real wrapper with Angular's real published worker imported
 * behind it, as the build ships them.
 */

const frontendRoot = join(import.meta.dir, '..', '..');
const wrapperSource = readFileSync(join(frontendRoot, 'service-worker', 'protected-api-worker.js'), 'utf8');
const angularWorkerSource = readFileSync(
  join(frontendRoot, 'node_modules', '@angular', 'service-worker', 'ngsw-worker.js'),
  'utf8',
);
const config = JSON.parse(readFileSync(join(frontendRoot, 'ngsw-config.json'), 'utf8')) as Config;

const ORIGIN = 'http://localhost';
const WORKER_URL = `${ORIGIN}/ngsw-worker.js`;
const FLAG_URL = `${ORIGIN}/${OFFLINE_PACK_STATE_KEY}`;

class MemoryCache {
  readonly entries = new Map<string, { body: ArrayBuffer; status: number; headers: [string, string][] }>();

  async match(request: Request | string): Promise<Response | undefined> {
    const stored = this.entries.get(urlOf(request));
    return stored ? new Response(stored.body.slice(0), { status: stored.status, headers: stored.headers }) : undefined;
  }

  async put(request: Request | string, response: Response): Promise<void> {
    this.entries.set(urlOf(request), {
      body: await response.arrayBuffer(),
      status: response.status,
      headers: [...response.headers.entries()],
    });
  }

  async delete(request: Request | string): Promise<boolean> {
    return this.entries.delete(urlOf(request));
  }

  async keys(): Promise<Request[]> {
    return [...this.entries.keys()].map((url) => new Request(url));
  }
}

class MemoryCacheStorage {
  readonly stores = new Map<string, MemoryCache>();
  readonly opened: string[] = [];

  async open(name: string): Promise<MemoryCache> {
    this.opened.push(name);
    let cache = this.stores.get(name);
    if (!cache) {
      cache = new MemoryCache();
      this.stores.set(name, cache);
    }
    return cache;
  }

  async has(name: string): Promise<boolean> {
    return this.stores.has(name);
  }

  async delete(name: string): Promise<boolean> {
    return this.stores.delete(name);
  }

  async keys(): Promise<string[]> {
    return [...this.stores.keys()];
  }

  async match(request: Request | string, options?: { cacheName?: string }): Promise<Response | undefined> {
    if (options?.cacheName) {
      return this.stores.get(options.cacheName)?.match(request);
    }
    for (const cache of this.stores.values()) {
      const hit = await cache.match(request);
      if (hit) return hit;
    }
    return undefined;
  }

  async writeFlag(state: string): Promise<void> {
    const cache = this.stores.get(OFFLINE_PACK_STATE_CACHE) ?? new MemoryCache();
    this.stores.set(OFFLINE_PACK_STATE_CACHE, cache);
    await cache.put(FLAG_URL, new Response(state));
  }

  assetPaths(): string[] {
    return [...this.stores.entries()]
      .filter(([name]) => /:assets:[^:]+:cache$/.test(name))
      .flatMap(([, cache]) => [...cache.entries.keys()].map((url) => new URL(url).pathname))
      .sort();
  }
}

function urlOf(request: Request | string): string {
  return new URL(typeof request === 'string' ? request : request.url, WORKER_URL).href;
}

describe('protected-api-worker.js offline pack switch', () => {
  interface Call {
    readonly args: unknown[];
  }

  function load(options: { lite?: Response; caches?: MemoryCacheStorage | null; brokenCaches?: boolean } = {}) {
    const calls: Call[] = [];
    const caches = options.caches === undefined ? new MemoryCacheStorage() : options.caches;
    const network = async (...args: unknown[]): Promise<Response> => {
      calls.push({ args });
      const url = new URL(typeof args[0] === 'string' ? args[0] : (args[0] as Request).url, WORKER_URL);
      if (url.pathname === '/ngsw-lite.json') return options.lite?.clone() ?? Response.json({ variant: 'lite' });
      if (url.pathname === '/ngsw.json') return Response.json({ variant: 'full' });
      return new Response(`network ${url.pathname}`);
    };
    const storage = options.brokenCaches
      ? {
          match: () => {
            throw new Error('storage unavailable');
          },
        }
      : caches;
    const self: Record<string, unknown> = {
      location: new URL(WORKER_URL),
      caches: storage,
      fetch: network,
      addEventListener() {},
      importScripts() {},
    };
    vm.runInNewContext(wrapperSource, { self, URL, Promise, Request, Response });
    return { calls, caches, fetch: self['fetch'] as (...args: unknown[]) => Promise<Response> };
  }

  const manifestRequest = () => new Request(`${ORIGIN}/ngsw.json?ngsw-cache-bust=0.123`);

  it('answers ngsw.json with the lite manifest, keeping the cache-busting query, when the flag is off', async () => {
    const worker = load();
    await worker.caches!.writeFlag('off');

    const response = await worker.fetch(manifestRequest());

    expect(await response.json()).toEqual({ variant: 'lite' });
    expect(worker.calls.map((call) => call.args[0])).toEqual([`${ORIGIN}/ngsw-lite.json?ngsw-cache-bust=0.123`]);
  });

  it.each([
    ['on', async (caches: MemoryCacheStorage) => caches.writeFlag('on')],
    ['missing', async () => undefined],
    ['unknown', async (caches: MemoryCacheStorage) => caches.writeFlag('maybe')],
  ])('keeps the full manifest when the flag is %s', async (_, write) => {
    const worker = load();
    await write(worker.caches!);
    const request = manifestRequest();

    const response = await worker.fetch(request);

    expect(await response.json()).toEqual({ variant: 'full' });
    // Angular's own request object, untouched.
    expect(worker.calls).toHaveLength(1);
    expect(worker.calls[0].args[0]).toBe(request);
  });

  it('keeps the full manifest when the flag cannot be read', async () => {
    const broken = load({ brokenCaches: true });
    expect(await (await broken.fetch(manifestRequest())).json()).toEqual({ variant: 'full' });

    const none = load({ caches: null });
    expect(await (await none.fetch(manifestRequest())).json()).toEqual({ variant: 'full' });
  });

  it('falls back to the full manifest rather than hand Angular a 404 (which would wipe its caches)', async () => {
    const worker = load({ lite: new Response('missing', { status: 404 }) });
    await worker.caches!.writeFlag('off');

    const response = await worker.fetch(manifestRequest());

    expect(await response.json()).toEqual({ variant: 'full' });
    expect(worker.calls.map((call) => new URL(String((call.args[0] as Request).url ?? call.args[0])).pathname)).toEqual([
      '/ngsw-lite.json',
      '/ngsw.json',
    ]);
  });

  it.each([
    `${ORIGIN}/main-ABC123.js`,
    `${ORIGIN}/assets/i18n/en-IN.json`,
    `${ORIGIN}/ngsw-lite.json`,
    `${ORIGIN}/nested/ngsw.json`,
    `${ORIGIN}/api/customers`,
    `${ORIGIN}/hubs/products/negotiate`,
    'https://cdn.test/ngsw.json',
  ])('passes %s through with the same arguments, whatever the flag', async (url) => {
    const worker = load();
    await worker.caches!.writeFlag('off');
    const init = { headers: { 'x-test': '1' } };

    await worker.fetch(url, init);

    expect(worker.calls).toHaveLength(1);
    expect(worker.calls[0].args).toEqual([url, init]);
    expect(worker.calls[0].args[1]).toBe(init);
  });

  it('only ever reads the flag: it creates no cache and stores nothing', async () => {
    const worker = load();

    await worker.fetch(manifestRequest());
    await worker.fetch(`${ORIGIN}/main-ABC123.js`);

    expect(worker.caches!.opened).toEqual([]);
    expect([...worker.caches!.stores.keys()]).toEqual([]);
  });

  it('leaves a non-GET request for ngsw.json alone', async () => {
    const worker = load();
    await worker.caches!.writeFlag('off');

    await worker.fetch(`${ORIGIN}/ngsw.json`, { method: 'POST' });

    expect(worker.calls[0].args[0]).toBe(`${ORIGIN}/ngsw.json`);
  });
});

// ---------------------------------------------------------------------------------------------
// The wrapper as shipped, with Angular's real worker imported behind it.
// ---------------------------------------------------------------------------------------------

const BUILT_FILES: Readonly<Record<string, string>> = {
  '/index.html': '<!doctype html><base href="/"><script src="main-ABC123.js"></script>',
  '/index.csr.html': '<!doctype html><base href="/">',
  '/main-ABC123.js': 'main',
  '/chunk-SALES02.js': 'lazy sales chunk',
  '/styles-GHI789.css': 'body{}',
  '/favicon.ico': 'ico',
  '/manifest.webmanifest': '{}',
  '/assets/i18n/en-IN.json': '{}',
  '/fonts/manrope-400.woff2': 'font',
  '/icons/icon-192x192.png': 'png',
};

const PACK = [
  '/favicon.ico',
  '/index.csr.html',
  '/index.html',
  '/manifest.webmanifest',
  '/main-ABC123.js',
  '/chunk-SALES02.js',
  '/styles-GHI789.css',
  '/assets/i18n/en-IN.json',
  '/fonts/manrope-400.woff2',
  '/icons/icon-192x192.png',
].sort();

class MemoryFilesystem implements Filesystem {
  async list(dir: string): Promise<string[]> {
    const prefix = dir.endsWith('/') ? dir : `${dir}/`;
    return Object.keys(BUILT_FILES).filter((file) => file.startsWith(prefix));
  }

  async read(file: string): Promise<string> {
    return BUILT_FILES[file] ?? '';
  }

  async hash(file: string): Promise<string> {
    return createHash('sha1').update(BUILT_FILES[file] ?? '').digest('hex');
  }

  async write(): Promise<void> {}
}

/** What `scripts/finalize-service-worker.mjs` publishes for the build above. */
async function manifests(): Promise<{ full: object; lite: object }> {
  const generated = (await new Generator(new MemoryFilesystem(), '/').process(config)) as Record<string, unknown>;
  const appData = { buildId: 'build-1' };
  return {
    full: { ...generated, appData: { ...appData, offlinePack: 'full' } },
    lite: {
      ...generated,
      appData: { ...appData, offlinePack: 'lite' },
      assetGroups: [],
      hashTable: {},
      navigationRequestStrategy: 'freshness',
    },
  };
}

async function shippedWorker() {
  const { full, lite } = await manifests();
  const caches = new MemoryCacheStorage();
  const requests: string[] = [];
  const messages = new Map<string, Record<string, unknown>[]>();
  const openTabs = new Set<string>();
  const network = { online: true };

  class Client {
    constructor(
      readonly id: string,
      readonly url = `${ORIGIN}/`,
    ) {}

    postMessage(message: Record<string, unknown>): void {
      messages.set(this.id, [...(messages.get(this.id) ?? []), message]);
    }
  }

  class WorkerRequest extends Request {
    constructor(input: RequestInfo | URL, init?: RequestInit) {
      super(typeof input === 'string' ? new URL(input, WORKER_URL).href : input, init);
    }
  }

  /** The server: both manifests and the built files. */
  const serverFetch = async (input: Request | string): Promise<Response> => {
    const url = new URL(typeof input === 'string' ? input : input.url, WORKER_URL);
    requests.push(url.pathname);
    if (!network.online) throw new TypeError('Failed to fetch');
    if (url.pathname === '/ngsw.json') return Response.json(full);
    if (url.pathname === '/ngsw-lite.json') return Response.json(lite);
    const body = BUILT_FILES[url.pathname];
    return body === undefined ? new Response('missing', { status: 404 }) : new Response(body);
  };

  let listeners = new Map<string, ((event: unknown) => void)[]>();

  const dispatch = async (type: string, event: Record<string, unknown>): Promise<unknown> => {
    const pending: Promise<unknown>[] = [];
    let response: Promise<unknown> | undefined;
    let stopped = false;
    const full = {
      ...event,
      waitUntil: (promise: Promise<unknown>) => pending.push(promise),
      respondWith: (promise: Promise<unknown>) => {
        response = promise;
      },
      stopImmediatePropagation: () => {
        stopped = true;
      },
    };
    for (const listener of listeners.get(type) ?? []) {
      listener(full);
      if (stopped) break;
    }
    const result = await response;
    while (pending.length > 0) {
      await Promise.allSettled(pending.splice(0));
    }
    return result;
  };

  const start = () => {
    listeners = new Map();
    const self: Record<string, unknown> = {
      location: new URL(WORKER_URL),
      caches,
      registration: { scope: `${ORIGIN}/`, active: null, unregister: async () => true },
      clients: {
        claim: async () => undefined,
        get: async (id: string) => (openTabs.has(id) ? new Client(id) : undefined),
        matchAll: async () => [...openTabs].map((id) => new Client(id)),
      },
      skipWaiting: async () => undefined,
      addEventListener(type: string, listener: (event: unknown) => void) {
        listeners.set(type, [...(listeners.get(type) ?? []), listener]);
      },
      fetch: serverFetch,
    };
    const context = vm.createContext({
      self,
      Client,
      Request: WorkerRequest,
      Response,
      Headers,
      URL,
      Promise,
      Date,
      TextEncoder,
      TextDecoder,
      console: { log() {}, warn() {}, error() {} },
      setTimeout: (callback: () => void) => queueMicrotask(callback),
    });
    self['importScripts'] = (script: string) => {
      expect(script).toBe('./ngsw-angular-worker.js');
      vm.runInContext(angularWorkerSource, context);
    };
    vm.runInContext(wrapperSource, context);
  };

  return {
    caches,
    requests,
    network,
    start,
    messages,
    async send(clientId: string, action: 'CHECK_FOR_UPDATES' | 'ACTIVATE_UPDATE'): Promise<unknown> {
      openTabs.add(clientId);
      await dispatch('message', { data: { action, nonce: 7 }, source: new Client(clientId) });
      const reply = (messages.get(clientId) ?? []).findLast((m) => m['type'] === 'OPERATION_COMPLETED');
      return reply?.['result'];
    },
    async fetchFrom(clientId: string, path: string): Promise<string> {
      openTabs.add(clientId);
      const response = (await dispatch('fetch', {
        request: new WorkerRequest(path),
        clientId,
        resultingClientId: '',
      })) as Response | undefined;
      if (!response) return 'not handled';
      return response.ok ? await response.text() : `status ${response.status}`;
    },
    types(clientId: string): unknown[] {
      return (messages.get(clientId) ?? []).map((message) => message['type']);
    },
  };
}

describe('the shipped worker (wrapper + Angular) and the offline pack flag', () => {
  it('installs the lite manifest on a device with the flag off: nothing is downloaded in the background', async () => {
    const worker = await shippedWorker();
    await worker.caches.writeFlag('off');
    worker.start();

    expect(await worker.fetchFrom('tab-1', '/main-ABC123.js')).toBe('main');

    expect(worker.requests).toEqual(['/ngsw-lite.json', '/main-ABC123.js']);
    expect(worker.caches.assetPaths()).toEqual([]);
  });

  it('installs the full pack when no flag was ever written (a device enabled before this release)', async () => {
    const worker = await shippedWorker();
    worker.start();

    await worker.fetchFrom('tab-1', '/main-ABC123.js');

    expect(worker.requests).not.toContain('/ngsw-lite.json');
    expect(worker.caches.assetPaths()).toEqual(PACK);
    worker.network.online = false;
    expect(await worker.fetchFrom('tab-2', '/chunk-SALES02.js')).toBe('lazy sales chunk');
  });

  it('switches off -> on with an update check, all or nothing, and reports VERSION_READY', async () => {
    const worker = await shippedWorker();
    await worker.caches.writeFlag('off');
    worker.start();
    await worker.fetchFrom('tab-1', '/main-ABC123.js');

    await worker.caches.writeFlag('on');
    expect(await worker.send('tab-1', 'CHECK_FOR_UPDATES')).toBe(true);

    expect(worker.types('tab-1')).toEqual(['VERSION_DETECTED', 'VERSION_READY', 'OPERATION_COMPLETED']);
    expect(worker.caches.assetPaths()).toEqual(PACK);
    expect(await worker.send('tab-1', 'ACTIVATE_UPDATE')).toBe(true);
    worker.network.online = false;
    expect(await worker.fetchFrom('tab-1', '/chunk-SALES02.js')).toBe('lazy sales chunk');
  });

  it('switches on -> off and drops the pack once a fresh worker starts, keeping the flag', async () => {
    const worker = await shippedWorker();
    await worker.caches.writeFlag('on');
    worker.start();
    await worker.fetchFrom('tab-1', '/main-ABC123.js');
    expect(worker.caches.assetPaths()).toEqual(PACK);

    await worker.caches.writeFlag('off');
    expect(await worker.send('tab-1', 'CHECK_FOR_UPDATES')).toBe(true);
    expect(await worker.send('tab-1', 'ACTIVATE_UPDATE')).toBe(true);
    worker.start();
    await worker.fetchFrom('tab-1', '/favicon.ico');

    expect(worker.caches.assetPaths()).toEqual([]);
    expect(await worker.caches.has(OFFLINE_PACK_STATE_CACHE)).toBe(true);
  });

  it('still hands protected requests to the network untouched', async () => {
    const worker = await shippedWorker();
    await worker.caches.writeFlag('on');
    worker.start();
    await worker.fetchFrom('tab-1', '/main-ABC123.js');

    expect(await worker.fetchFrom('tab-1', '/api/customers')).toBe('not handled');
    expect(worker.requests).not.toContain('/api/customers');
  });
});
