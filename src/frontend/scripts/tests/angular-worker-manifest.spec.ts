import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import vm from 'node:vm';

import { Generator, type Config, type Filesystem } from '@angular/service-worker/config';
import { describe, expect, it } from 'vitest';

/**
 * Phase 0 spike B of docs/superpowers/plans/2026-11-10-shared-styles-and-offline-pack.md.
 *
 * Runs Angular's own published worker (`@angular/service-worker/ngsw-worker.js`, the file the build
 * copies to `ngsw-angular-worker.js`) against an in-memory Cache Storage and a `self.fetch` that
 * plays the part the wrapper worker would: it answers `ngsw.json` with either a "full" manifest
 * (compiled from the real `ngsw-config.json` by Angular's own generator) or a "lite" one (the same
 * manifest with no asset groups), and sends everything else to a fake network.
 *
 * It pins what the planned manifest switch relies on: Angular asks for the manifest through the
 * worker global's `fetch`, installs a changed manifest as a new version only once every prefetched
 * file has arrived and matched its hash, keeps the old version otherwise, and tells the page with
 * VERSION_READY / VERSION_INSTALLATION_FAILED. Nothing here changes the product worker.
 */

const frontendRoot = join(import.meta.dir, '..', '..');
const angularWorkerSource = readFileSync(
  join(frontendRoot, 'node_modules', '@angular', 'service-worker', 'ngsw-worker.js'),
  'utf8',
);
const config = JSON.parse(readFileSync(join(frontendRoot, 'ngsw-config.json'), 'utf8')) as Config;

// A localhost scope makes Angular initialise a version immediately instead of on idle.
const ORIGIN = 'http://localhost';
const WORKER_URL = `${ORIGIN}/ngsw-worker.js`;

const BUILT_FILES: Readonly<Record<string, string>> = {
  '/index.html': '<!doctype html><base href="/"><script src="main-ABC123.js"></script>',
  '/index.csr.html': '<!doctype html><base href="/">',
  '/main-ABC123.js': 'main',
  '/chunk-SHELL01.js': 'lazy shell chunk',
  '/chunk-SALES02.js': 'lazy sales chunk',
  '/styles-GHI789.css': 'body{}',
  '/favicon.ico': 'ico',
  '/manifest.webmanifest': '{}',
  '/assets/i18n/en-IN.json': '{}',
  '/fonts/manrope-400.woff2': 'font',
};

const PREFETCHED = [
  '/favicon.ico',
  '/index.csr.html',
  '/index.html',
  '/manifest.webmanifest',
  '/chunk-SALES02.js',
  '/chunk-SHELL01.js',
  '/main-ABC123.js',
  '/styles-GHI789.css',
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

interface Manifest {
  readonly assetGroups?: readonly { name: string; urls: string[] }[];
  readonly hashTable: Record<string, string>;
  readonly [key: string]: unknown;
}

async function fullManifest(): Promise<Manifest> {
  return (await new Generator(new MemoryFilesystem(), '/').process(config)) as unknown as Manifest;
}

/** What the plan calls `ngsw-lite.json`: the full manifest without asset groups or hashes. */
function liteManifest(full: Manifest): Manifest {
  return { ...full, assetGroups: [], hashTable: {} };
}

/** Cache Storage kept outside the worker so that a restarted worker sees the same storage. */
interface StoredResponse {
  readonly body: ArrayBuffer;
  readonly status: number;
  readonly headers: [string, string][];
}

class MemoryCache {
  readonly entries = new Map<string, StoredResponse>();

  async match(request: Request | string): Promise<Response | undefined> {
    const stored = this.entries.get(urlOf(request));
    return stored
      ? new Response(stored.body.slice(0), { status: stored.status, headers: stored.headers })
      : undefined;
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

  async open(name: string): Promise<MemoryCache> {
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

  async match(request: Request | string): Promise<Response | undefined> {
    for (const cache of this.stores.values()) {
      const hit = await cache.match(request);
      if (hit) return hit;
    }
    return undefined;
  }

  /** URL paths held by the asset caches of any app version. */
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

type Variant = 'full' | 'lite';

interface Network {
  online: boolean;
  /** Which manifest the wrapper's `fetch` answers `ngsw.json` with. */
  variant: Variant;
  /** Paths the server fails with 500 (an interrupted pack download). */
  failing: Set<string>;
  readonly requests: string[];
}

interface Harness {
  readonly network: Network;
  readonly caches: MemoryCacheStorage;
  readonly messages: Map<string, Record<string, unknown>[]>;
  start(): void;
  /** Sends a page message (as SwUpdate does) and waits until the worker settled it. */
  send(clientId: string, action: 'CHECK_FOR_UPDATES' | 'ACTIVATE_UPDATE'): Promise<unknown>;
  /** A fetch made by an open tab; resolves to the body text or the failure status. */
  fetchFrom(clientId: string, path: string): Promise<string>;
  /** Angular's debug page, which reports the driver state. */
  driverState(): Promise<string>;
  closeTab(clientId: string): void;
}

async function createHarness(): Promise<Harness> {
  const full = await fullManifest();
  const lite = liteManifest(full);
  const caches = new MemoryCacheStorage();
  const network: Network = { online: true, variant: 'lite', failing: new Set(), requests: [] };
  const messages = new Map<string, Record<string, unknown>[]>();
  const openTabs = new Set<string>();

  class Client {
    constructor(
      readonly id: string,
      readonly url = `${ORIGIN}/`,
    ) {}

    postMessage(message: Record<string, unknown>): void {
      messages.set(this.id, [...(messages.get(this.id) ?? []), message]);
    }
  }

  const clientFor = (id: string) => {
    openTabs.add(id);
    return new Client(id);
  };

  /** The browser's network, as the wrapper worker would reach it. */
  const networkFetch = async (input: Request | string): Promise<Response> => {
    const url = new URL(typeof input === 'string' ? input : input.url, WORKER_URL);
    network.requests.push(url.pathname + url.search);
    if (!network.online) {
      throw new TypeError('Failed to fetch');
    }
    if (network.failing.has(url.pathname)) {
      return new Response('boom', { status: 500 });
    }
    const body = BUILT_FILES[url.pathname];
    return body === undefined ? new Response('missing', { status: 404 }) : new Response(body);
  };

  /**
   * The switch the plan proposes for the wrapper: installed on `self.fetch` before Angular's
   * script runs, it answers the manifest request and passes everything else on unchanged.
   */
  const wrappedFetch = async (input: Request | string): Promise<Response> => {
    const url = new URL(typeof input === 'string' ? input : input.url, WORKER_URL);
    if (url.origin === ORIGIN && url.pathname === '/ngsw.json') {
      network.requests.push(url.pathname + url.search);
      if (!network.online) throw new TypeError('Failed to fetch');
      return Response.json(network.variant === 'full' ? full : lite);
    }
    return networkFetch(input);
  };

  let listeners = new Map<string, ((event: unknown) => void)[]>();

  /** A worker global resolves relative Request URLs against its own script URL. */
  class WorkerRequest extends Request {
    constructor(input: RequestInfo | URL, init?: RequestInit) {
      super(typeof input === 'string' ? new URL(input, WORKER_URL).href : input, init);
    }
  }

  const dispatch = async (type: string, event: Record<string, unknown>): Promise<unknown> => {
    const pending: Promise<unknown>[] = [];
    let response: Promise<unknown> | undefined;
    const full = {
      ...event,
      waitUntil: (promise: Promise<unknown>) => pending.push(promise),
      respondWith: (promise: Promise<unknown>) => {
        response = promise;
      },
    };
    for (const listener of listeners.get(type) ?? []) listener(full);
    const result = await response;
    // waitUntil work can schedule more waitUntil work (idle tasks); drain it all.
    while (pending.length > 0) {
      await Promise.allSettled(pending.splice(0));
    }
    return result;
  };

  const start = () => {
    listeners = new Map();
    const self = {
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
      fetch: wrappedFetch,
    };
    vm.runInNewContext(angularWorkerSource, {
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
      // Idle tasks run as soon as the current work yields, so each dispatch settles them.
      setTimeout: (callback: () => void) => queueMicrotask(callback),
    });
  };

  return {
    network,
    caches,
    messages,
    start,
    async send(clientId, action) {
      await dispatch('message', { data: { action, nonce: 7 }, source: clientFor(clientId) });
      const reply = (messages.get(clientId) ?? []).findLast((m) => m['type'] === 'OPERATION_COMPLETED');
      return reply?.['result'] ?? reply?.['error'];
    },
    async fetchFrom(clientId, path) {
      openTabs.add(clientId);
      const response = (await dispatch('fetch', {
        request: new WorkerRequest(path),
        clientId,
        resultingClientId: '',
      })) as Response;
      return response.ok ? await response.text() : `status ${response.status}`;
    },
    async driverState() {
      const response = (await dispatch('fetch', {
        request: new WorkerRequest('/ngsw/state'),
        clientId: '',
        resultingClientId: '',
      })) as Response;
      return /Driver state: (\w+)/.exec(await response.text())?.[1] ?? 'unknown';
    },
    closeTab(clientId) {
      openTabs.delete(clientId);
    },
  };
}

function types(harness: Harness, clientId: string): unknown[] {
  return (harness.messages.get(clientId) ?? []).map((message) => message['type']);
}

describe("Angular's worker and a switched manifest (spike B)", () => {
  it('fetches the manifest through the worker global fetch with a cache-busting query', () => {
    // Driver.fetchLatestManifest -> Driver.safeFetch -> this.scope.fetch(req), with scope = self.
    expect(angularWorkerSource).toContain(
      'const res = await this.safeFetch(this.adapter.newRequest("ngsw.json?ngsw-cache-bust=" + Math.random()));',
    );
    expect(angularWorkerSource).toMatch(/async safeFetch\(req\) \{\s+try \{\s+return await this\.scope\.fetch\(req\);/);
    expect(angularWorkerSource).toMatch(/var scope = self;\s+var adapter = new Adapter\(scope\.registration\.scope, self\.caches\);/);
  });

  it('asks for /ngsw.json?ngsw-cache-bust=<random> through self.fetch and installs a lite manifest without prefetching', async () => {
    const harness = await createHarness();
    harness.start();

    expect(await harness.fetchFrom('tab-1', '/main-ABC123.js')).toBe('main');

    expect(harness.network.requests[0]).toMatch(/^\/ngsw\.json\?ngsw-cache-bust=0\.\d+$/);
    // Nothing but the manifest and the page's own request: no background pack download.
    expect(harness.network.requests.slice(1)).toEqual(['/main-ABC123.js']);
    expect(harness.caches.assetPaths()).toEqual([]);

    harness.network.online = false;
    expect(await harness.fetchFrom('tab-1', '/chunk-SALES02.js')).toBe('status 504');
  });

  it('installs the full manifest as a new version once every prefetched file is cached, then reports VERSION_READY', async () => {
    const harness = await createHarness();
    harness.start();
    await harness.fetchFrom('tab-1', '/main-ABC123.js');

    harness.network.variant = 'full';
    harness.network.requests.length = 0;
    expect(await harness.send('tab-1', 'CHECK_FOR_UPDATES')).toBe(true);

    expect(types(harness, 'tab-1')).toEqual(['VERSION_DETECTED', 'VERSION_READY', 'OPERATION_COMPLETED']);
    expect(harness.caches.assetPaths()).toEqual(PREFETCHED);
    expect(harness.network.requests.filter((r) => !r.startsWith('/ngsw.json')).sort()).toEqual(PREFETCHED);

    harness.network.online = false;
    // A new tab gets the latest version and reads every chunk from the pack.
    expect(await harness.fetchFrom('tab-2', '/chunk-SALES02.js')).toBe('lazy sales chunk');
    // The open tab stays on the lite version (network) until it activates the update...
    expect(await harness.fetchFrom('tab-1', '/chunk-SHELL01.js')).toBe('status 504');
    // ...which needs no reload when only the manifest changed: the files are the same build.
    expect(await harness.send('tab-1', 'ACTIVATE_UPDATE')).toBe(true);
    expect(await harness.fetchFrom('tab-1', '/chunk-SHELL01.js')).toBe('lazy shell chunk');
  });

  it('keeps the old version when a prefetched file fails, reports VERSION_INSTALLATION_FAILED and retries on the next check', async () => {
    const harness = await createHarness();
    harness.start();
    await harness.fetchFrom('tab-1', '/main-ABC123.js');

    harness.network.variant = 'full';
    harness.network.failing.add('/chunk-SALES02.js');
    expect(await harness.send('tab-1', 'CHECK_FOR_UPDATES')).toBe(false);
    expect(types(harness, 'tab-1')).toEqual([
      'VERSION_DETECTED',
      'VERSION_INSTALLATION_FAILED',
      'OPERATION_COMPLETED',
    ]);
    // Until this worker instance restarts, tabs it has not seen yet are left to the network.
    expect(await harness.driverState()).toBe('EXISTING_CLIENTS_ONLY');

    // The lite version is still the latest: asking with the lite manifest finds nothing new.
    harness.network.variant = 'lite';
    harness.messages.clear();
    expect(await harness.send('tab-1', 'CHECK_FOR_UPDATES')).toBe(false);
    expect(types(harness, 'tab-1')).toEqual(['NO_NEW_VERSION_DETECTED', 'OPERATION_COMPLETED']);

    // The failed version was never recorded, so the next full check downloads it again.
    harness.network.variant = 'full';
    harness.network.failing.clear();
    harness.messages.clear();
    expect(await harness.send('tab-1', 'CHECK_FOR_UPDATES')).toBe(true);
    expect(types(harness, 'tab-1')).toEqual(['VERSION_DETECTED', 'VERSION_READY', 'OPERATION_COMPLETED']);
    expect(await harness.driverState()).toBe('NORMAL');
  });

  it('switches back to lite and drops the pack when the worker next starts with no tab on the full version', async () => {
    const harness = await createHarness();
    harness.network.variant = 'full';
    harness.start();
    await harness.fetchFrom('tab-1', '/main-ABC123.js');
    expect(harness.caches.assetPaths()).toEqual(PREFETCHED);

    harness.network.variant = 'lite';
    harness.messages.clear();
    expect(await harness.send('tab-1', 'CHECK_FOR_UPDATES')).toBe(true);
    expect(types(harness, 'tab-1')).toEqual(['VERSION_DETECTED', 'VERSION_READY', 'OPERATION_COMPLETED']);
    // Angular only cleans caches when a worker instance initialises; the pack is still there.
    expect(harness.caches.assetPaths()).toEqual(PREFETCHED);

    expect(await harness.send('tab-1', 'ACTIVATE_UPDATE')).toBe(true);
    harness.start(); // the browser stops idle workers; the next event starts a fresh instance
    expect(await harness.fetchFrom('tab-1', '/favicon.ico')).toBe('ico');
    expect(harness.caches.assetPaths()).toEqual([]);
  });
});
