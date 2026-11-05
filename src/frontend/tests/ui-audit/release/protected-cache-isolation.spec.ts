import { existsSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

import { expect, test, type Page } from '@playwright/test';

import { isLegacyProtectedCacheName } from '../../../src/app/core/service-worker/protected-cache-names';
import { buildLegacyManifest } from './legacy-manifest';
import { startReleaseServer, TENANT_PROBE, type ReleaseServer } from './release-server';

/**
 * Protected cache isolation, proved against the built worker rather than a model of it.
 *
 * Probes use raw `fetch` with no bypass header: the worst case, which is how a tab still running an
 * older build asks. Each answer is rendered into the page and read back from the DOM. The old
 * worker's fallback fired after 8 s; delayed answers here take 10 s, so a body that arrives sooner
 * than that came from storage.
 */

const BUILD_ROOT = resolve(__dirname, '../../../dist/INVENTORY/browser');
const LEGACY_TIMEOUT_MS = 8_000;
const SLOW_NETWORK_MS = 10_000;
const LEGACY_WORKER = '/ngsw-worker.js';
const APP_WORKER = '/app-worker.js';

interface QueuedSale {
  readonly key: string;
  readonly shopId: string;
  readonly deviceId: string;
  readonly clientSaleId: string;
  readonly status: 'Pending' | 'NeedsReview';
  readonly payload: { readonly total: number; readonly lines: readonly { readonly sku: string; readonly quantity: number }[] };
}

const UNSYNCED_SALES: readonly QueuedSale[] = [
  {
    key: 'shop-a::device-1::sale-1',
    shopId: 'shop-a',
    deviceId: 'device-1',
    clientSaleId: 'sale-1',
    status: 'Pending',
    payload: { total: 1250, lines: [{ sku: 'RICE-5KG', quantity: 2 }] },
  },
  {
    key: 'shop-a::device-1::sale-2',
    shopId: 'shop-a',
    deviceId: 'device-1',
    clientSaleId: 'sale-2',
    status: 'NeedsReview',
    payload: { total: 480, lines: [{ sku: 'DAL-1KG', quantity: 4 }] },
  },
];

let server: ReleaseServer;

test.beforeAll(() => {
  if (!existsSync(join(BUILD_ROOT, 'app-worker.js')) || !existsSync(join(BUILD_ROOT, 'ngsw.json'))) {
    throw new Error(`No production build at ${BUILD_ROOT}. Run \`bun run build\` in src/frontend first.`);
  }
});

test.beforeEach(async () => {
  server = await startReleaseServer(BUILD_ROOT);
});

test.afterEach(async () => {
  await server.close();
});

async function openHarness(page: Page): Promise<void> {
  await page.goto(`${server.origin}/__harness`);
}

async function registerWorker(page: Page, script: typeof LEGACY_WORKER | typeof APP_WORKER): Promise<void> {
  await page.evaluate(async (url) => {
    // The app registers its worker with updateViaCache 'none'; earlier builds used the default.
    await navigator.serviceWorker.register(url, url.endsWith('app-worker.js') ? { updateViaCache: 'none' } : undefined);
  }, script);
  await waitForController(page, script);
}

async function waitForController(page: Page, script: string): Promise<void> {
  await page.waitForFunction((url) => navigator.serviceWorker.controller?.scriptURL.endsWith(url) ?? false, script, {
    timeout: 30_000,
  });
}

/** Asks for the probe as `tenant` and renders whatever comes back; returns the rendered text. */
async function fetchTenant(page: Page, tenant: string): Promise<string> {
  await page.evaluate(
    async ({ path, who }) => {
      const out = document.querySelector('#out');
      if (!out) throw new Error('harness page missing');
      try {
        const response = await fetch(path, { headers: { Authorization: who } });
        out.textContent = ((await response.json()) as { tenant: string }).tenant;
      } catch {
        out.textContent = 'network-error';
      }
    },
    { path: TENANT_PROBE, who: tenant },
  );
  return (await page.locator('#out').textContent()) ?? '';
}

async function cacheInventory(page: Page): Promise<{ name: string; urls: string[] }[]> {
  return page.evaluate(async () =>
    Promise.all(
      (await caches.keys()).map(async (name) => ({
        name,
        urls: (await (await caches.open(name)).keys()).map((request) => request.url),
      })),
    ),
  );
}

async function legacyProtectedEntries(page: Page): Promise<string[]> {
  return (await cacheInventory(page)).filter((cache) => isLegacyProtectedCacheName(cache.name)).flatMap((cache) => cache.urls);
}

async function storedProbeAnswers(page: Page): Promise<string[]> {
  return (await cacheInventory(page)).flatMap((cache) => cache.urls).filter((url) => url.includes(TENANT_PROBE));
}

/** A tab on the deployed old build: old manifest, old worker script, an answer for Shop-A stored. */
async function warmLegacyTab(page: Page): Promise<void> {
  server.useManifest(await buildLegacyManifest(BUILD_ROOT));
  await openHarness(page);
  await registerWorker(page, LEGACY_WORKER);
  expect(await fetchTenant(page, 'Shop-A')).toBe('Shop-A');
  await expect.poll(() => legacyProtectedEntries(page), { timeout: 15_000 }).toContainEqual(expect.stringContaining(TENANT_PROBE));
}

/** What the first load of this build does in any tab: register the app worker. */
async function deployCurrentBuild(page: Page): Promise<void> {
  server.useManifest(null);
  await openHarness(page);
  await page.evaluate(async () => {
    await navigator.serviceWorker.register('/app-worker.js', { updateViaCache: 'none' });
  });
}

async function timed<T>(work: () => Promise<T>): Promise<{ value: T; elapsedMs: number }> {
  const started = Date.now();
  const value = await work();
  return { value, elapsedMs: Date.now() - started };
}

test('protected_api_not_in_generated_data_groups: the built manifest has no rule over protected paths', () => {
  const manifest = JSON.parse(readFileSync(join(BUILD_ROOT, 'ngsw.json'), 'utf8')) as {
    assetGroups: { urls: string[]; patterns: string[] }[];
    dataGroups?: { name: string; patterns: string[] }[];
    navigationUrls: { positive: boolean; regex: string }[];
  };
  const protectedUrls = ['/api/customers', '/api/sales', '/api/bank-accounts', '/api/anything/else?x=1', '/hubs/products'];

  for (const group of manifest.dataGroups ?? []) {
    for (const pattern of group.patterns) {
      for (const url of protectedUrls) {
        expect(new RegExp(pattern).test(`https://shop.test${url}`), `${group.name} matches ${url}`).toBe(false);
      }
    }
  }
  const listed = manifest.assetGroups.flatMap((group) => group.urls);
  expect(listed.filter((url) => url.startsWith('/api/') || url.startsWith('/hubs/'))).toEqual([]);
  expect(manifest.assetGroups.flatMap((group) => group.patterns)).toEqual([]);
  expect(listed).not.toContain('/app-worker.js');
  expect(listed).toEqual(expect.arrayContaining(['/index.html', '/assets/i18n/en-IN.json']));
  expect(listed.some((url) => /^\/main-[A-Z0-9]+\.js$/.test(url))).toBe(true);
  const excluded = manifest.navigationUrls.filter((rule) => !rule.positive).map((rule) => new RegExp(rule.regex));
  expect(excluded.some((regex) => regex.test('/api/customers'))).toBe(true);
  expect(excluded.some((regex) => regex.test('/hubs/products'))).toBe(true);
});

test('legacy_worker_reproduces_cross_shop_reuse: the harness detects the defect it guards against', async ({ page }) => {
  // Control. The old configuration, with the API already answering no-store: the worker ignores
  // Cache-Control, so the header alone never fixed this.
  await warmLegacyTab(page);
  server.setApiDelay(SLOW_NETWORK_MS);

  const { value, elapsedMs } = await timed(() => fetchTenant(page, 'Shop-B'));

  expect(value).toBe('Shop-A');
  expect(elapsedMs).toBeGreaterThanOrEqual(LEGACY_TIMEOUT_MS - 500);
  expect(elapsedMs).toBeLessThan(SLOW_NETWORK_MS);
});

test('shop_b_timeout_never_returns_shop_a: a slow answer is waited for, never replaced', async ({ page }) => {
  await openHarness(page);
  await registerWorker(page, APP_WORKER);
  expect(await fetchTenant(page, 'Shop-A')).toBe('Shop-A');
  server.setApiDelay(SLOW_NETWORK_MS);

  const { value, elapsedMs } = await timed(() => fetchTenant(page, 'Shop-B'));

  expect(value).toBe('Shop-B');
  await expect(page.locator('#out')).toHaveText('Shop-B');
  expect(elapsedMs).toBeGreaterThanOrEqual(SLOW_NETWORK_MS - 500);
  expect(await storedProbeAnswers(page)).toEqual([]);
});

test('new_user_offline_never_reads_old_user: offline, a protected request fails instead of answering', async ({ page, context }) => {
  await openHarness(page);
  await registerWorker(page, APP_WORKER);
  expect(await fetchTenant(page, 'user-a')).toBe('user-a');

  await context.setOffline(true);
  const answer = await fetchTenant(page, 'user-b');

  expect(answer).toBe('network-error');
  await expect(page.locator('#out')).not.toHaveText('user-a');
  expect(await storedProbeAnswers(page)).toEqual([]);
});

test('old_worker_open_tab_cannot_serve_protected_body: an open tab on the old build is covered once the new build loads anywhere', async ({
  page,
  context,
}) => {
  await warmLegacyTab(page);

  // The new build loads in a second tab while the first stays open on the old one.
  const newTab = await context.newPage();
  await deployCurrentBuild(newTab);
  await waitForController(page, APP_WORKER);
  await expect.poll(() => legacyProtectedEntries(page), { timeout: 15_000 }).toEqual([]);

  // The old tab still runs old code: no bypass header, same URL, another shop, slow network.
  server.setApiDelay(SLOW_NETWORK_MS);
  const { value, elapsedMs } = await timed(() => fetchTenant(page, 'Shop-B'));

  expect(value).toBe('Shop-B');
  await expect(page.locator('#out')).toHaveText('Shop-B');
  expect(elapsedMs).toBeGreaterThanOrEqual(SLOW_NETWORK_MS - 500);
  expect(await legacyProtectedEntries(page)).toEqual([]);
});

test('cleanup_preserves_unsynced_invoice: the purge leaves queued sales exactly as they were', async ({ page, context }) => {
  await warmLegacyTab(page);
  await page.evaluate(async (sales) => {
    const database = await new Promise<IDBDatabase>((resolveOpen, rejectOpen) => {
      const request = indexedDB.open('intelibill-offline-sales-queue', 1);
      request.onupgradeneeded = () => request.result.createObjectStore('queued-sales', { keyPath: 'key' });
      request.onsuccess = () => resolveOpen(request.result);
      request.onerror = () => rejectOpen(request.error);
    });
    await new Promise<void>((resolveWrite, rejectWrite) => {
      const transaction = database.transaction('queued-sales', 'readwrite');
      for (const sale of sales) transaction.objectStore('queued-sales').put(sale);
      transaction.oncomplete = () => resolveWrite();
      transaction.onerror = () => rejectWrite(transaction.error);
    });
    database.close();
  }, UNSYNCED_SALES);

  const newTab = await context.newPage();
  await deployCurrentBuild(newTab);
  await waitForController(page, APP_WORKER);
  await expect.poll(() => legacyProtectedEntries(page), { timeout: 15_000 }).toEqual([]);

  const stored = await newTab.evaluate(async () => {
    const database = await new Promise<IDBDatabase>((resolveOpen, rejectOpen) => {
      const request = indexedDB.open('intelibill-offline-sales-queue', 1);
      request.onsuccess = () => resolveOpen(request.result);
      request.onerror = () => rejectOpen(request.error);
    });
    const rows = await new Promise<unknown[]>((resolveRead, rejectRead) => {
      const request = database.transaction('queued-sales').objectStore('queued-sales').getAll();
      request.onsuccess = () => resolveRead(request.result);
      request.onerror = () => rejectRead(request.error);
    });
    database.close();
    return rows;
  });

  expect(stored).toEqual(UNSYNCED_SALES);
});

test('assets_offline_api_fails_closed: the shell opens offline, a protected request does not', async ({ page, context }) => {
  await openHarness(page);
  await registerWorker(page, APP_WORKER);
  // The app group is prefetched on install; translations are cached the first time they are read.
  await expect
    .poll(async () => (await cacheInventory(page)).flatMap((cache) => cache.urls).some((url) => url.endsWith('/index.html')), {
      timeout: 30_000,
    })
    .toBe(true);
  expect(await page.evaluate(async () => (await fetch('/assets/i18n/en-IN.json')).ok)).toBe(true);

  await context.setOffline(true);
  await page.goto(`${server.origin}/`);

  await expect(page.locator('app-root')).toBeAttached();
  const offline = await page.evaluate(async (probe) => {
    const translations = await fetch('/assets/i18n/en-IN.json').then((response) => response.ok, () => false);
    const api = await fetch(probe, { headers: { Authorization: 'Shop-A' } }).then(
      (response) => `status ${response.status}`,
      () => 'network-error',
    );
    return { translations, api };
  }, TENANT_PROBE);

  expect(offline).toEqual({ translations: true, api: 'network-error' });
});

test('production build sends credentials and the worker bypass to the API only', async ({ page }) => {
  // A remembered session whose access token has expired: startup renews it through HttpClient.
  await openHarness(page);
  await page.evaluate(() => {
    const hour = 60 * 60 * 1000;
    localStorage.setItem(
      'inventory.auth.session.local',
      JSON.stringify({
        accessToken: 'expired-access-token',
        refreshToken: 'refresh-token',
        accessTokenExpiresAt: new Date(Date.now() - hour).toISOString(),
        refreshTokenExpiresAt: new Date(Date.now() + hour).toISOString(),
        rememberMe: true,
        user: { id: 'user-1', email: 'owner@shop.test', phoneNumber: null, firstName: 'Owner', lastName: 'One', language: 'en-IN' },
        activeShopId: 'shop-a',
        shops: [{ shopId: 'shop-a', shopName: 'Shop A', role: 'Owner', isDefault: true, lastUsedAt: null }],
      }),
    );
  });

  await page.goto(`${server.origin}/`);
  await expect.poll(() => server.requests.some((request) => request.path === '/api/auth/token/refresh')).toBe(true);
  await expect(page).toHaveURL(/\/login/);

  const refresh = server.requests.find((request) => request.path === '/api/auth/token/refresh');
  expect(refresh?.workerBypass).toBe('true');
  const translations = server.requests.filter((request) => request.path.startsWith('/assets/i18n/'));
  expect(translations.length).toBeGreaterThan(0);
  expect(translations.map((request) => request.authorization)).toEqual(translations.map(() => null));
  expect(translations.map((request) => request.workerBypass)).toEqual(translations.map(() => null));
});
