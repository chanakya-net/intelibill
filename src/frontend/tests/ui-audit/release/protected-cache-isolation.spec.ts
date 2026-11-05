import { existsSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

import { expect, test, type BrowserContext, type Page } from '@playwright/test';

import { isLegacyProtectedCacheName } from '../../../src/app/core/service-worker/protected-cache-names';
import {
  PROTECTED_WORKER_PROTOCOL,
  PROTECTED_WORKER_QUERY,
} from '../../../src/app/core/service-worker/service-worker-registration';
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
const HANDOVER_MS = 30_000;

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
  const worker = join(BUILD_ROOT, 'ngsw-worker.js');
  if (!existsSync(worker) || !existsSync(join(BUILD_ROOT, 'ngsw-angular-worker.js'))) {
    throw new Error(`No finalized production build at ${BUILD_ROOT}. Run \`bun run build\` in src/frontend first.`);
  }
  if (!readFileSync(worker, 'utf8').startsWith('/* intelibill:protected-api-worker */')) {
    throw new Error(`${worker} is not the wrapper: run \`bun run build\` (it includes build:worker).`);
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

/** Registers `/ngsw-worker.js` the way every build does; the server decides which worker that is. */
async function registerWorker(page: Page): Promise<void> {
  await page.evaluate(async () => {
    await navigator.serviceWorker.register('/ngsw-worker.js');
  });
  await page.waitForFunction(() => navigator.serviceWorker.controller !== null, undefined, { timeout: HANDOVER_MS });
}

/** Whether the worker controlling `page` is Intelibill's: only it answers the isolation query. */
async function controlledByProtectedWorker(page: Page): Promise<boolean> {
  return page.evaluate(
    ({ query, protocol }) =>
      new Promise<boolean>((resolveQuery) => {
        const controller = navigator.serviceWorker.controller;
        if (!controller) {
          resolveQuery(false);
          return;
        }
        const channel = new MessageChannel();
        const timer = setTimeout(() => resolveQuery(false), 1_000);
        channel.port1.onmessage = (event: MessageEvent<{ protectedApiIsolation?: number }>) => {
          clearTimeout(timer);
          resolveQuery(event.data?.protectedApiIsolation === protocol);
        };
        controller.postMessage({ action: query }, [channel.port2]);
      }),
    { query: PROTECTED_WORKER_QUERY, protocol: PROTECTED_WORKER_PROTOCOL },
  );
}

async function waitForProtectedWorker(page: Page): Promise<void> {
  await expect.poll(() => controlledByProtectedWorker(page), { timeout: HANDOVER_MS }).toBe(true);
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

/** A tab on the deployed old build: old worker, old manifest, an answer for Shop-A stored. */
async function warmLegacyTab(page: Page): Promise<object> {
  const manifest = await buildLegacyManifest(BUILD_ROOT);
  server.useLegacyDeployment(manifest);
  await openHarness(page);
  await registerWorker(page);
  expect(await controlledByProtectedWorker(page)).toBe(false);
  expect(await fetchTenant(page, 'Shop-A')).toBe('Shop-A');
  await expect.poll(() => legacyProtectedEntries(page), { timeout: 15_000 }).toContainEqual(expect.stringContaining(TENANT_PROBE));
  return manifest;
}

/** Deploys this build and opens the built app in a new tab, as the first visit after a release does. */
async function openThisBuild(context: BrowserContext): Promise<Page> {
  server.useCurrentDeployment();
  const appTab = await context.newPage();
  await appTab.goto(`${server.origin}/`);
  await expect(appTab.locator('app-root')).toBeAttached();
  return appTab;
}

async function expectSlowShopBAnswered(page: Page): Promise<void> {
  server.setApiDelay(SLOW_NETWORK_MS);
  const started = Date.now();

  expect(await fetchTenant(page, 'Shop-B')).toBe('Shop-B');

  await expect(page.locator('#out')).toHaveText('Shop-B');
  expect(Date.now() - started).toBeGreaterThanOrEqual(SLOW_NETWORK_MS - 500);
  server.setApiDelay(0);
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
  expect(listed).not.toEqual(expect.arrayContaining(['/ngsw-worker.js']));
  expect(listed).not.toEqual(expect.arrayContaining(['/ngsw-angular-worker.js']));
  expect(listed).toEqual(expect.arrayContaining(['/index.html', '/assets/i18n/en-IN.json']));
  expect(listed.some((url) => /^\/main-[A-Z0-9]+\.js$/.test(url))).toBe(true);
  const excluded = manifest.navigationUrls.filter((rule) => !rule.positive).map((rule) => new RegExp(rule.regex));
  expect(excluded.some((regex) => regex.test('/api/customers'))).toBe(true);
  expect(excluded.some((regex) => regex.test('/hubs/products'))).toBe(true);
});

test('legacy_worker_reproduces_cross_shop_reuse: the harness detects the defect it guards against', async ({ page }) => {
  // Control. The old deployment, with the API already answering no-store: the worker ignores
  // Cache-Control, so the header alone never fixed this.
  await warmLegacyTab(page);
  server.setApiDelay(SLOW_NETWORK_MS);
  const started = Date.now();

  expect(await fetchTenant(page, 'Shop-B')).toBe('Shop-A');

  const elapsed = Date.now() - started;
  expect(elapsed).toBeGreaterThanOrEqual(LEGACY_TIMEOUT_MS - 500);
  expect(elapsed).toBeLessThan(SLOW_NETWORK_MS);
});

test('shop_b_timeout_never_returns_shop_a: a slow answer is waited for, never replaced', async ({ page }) => {
  await openHarness(page);
  await registerWorker(page);
  expect(await controlledByProtectedWorker(page)).toBe(true);
  expect(await fetchTenant(page, 'Shop-A')).toBe('Shop-A');

  await expectSlowShopBAnswered(page);

  expect(await storedProbeAnswers(page)).toEqual([]);
});

test('new_user_offline_never_reads_old_user: offline, a protected request fails instead of answering', async ({ page, context }) => {
  await openHarness(page);
  await registerWorker(page);
  expect(await fetchTenant(page, 'user-a')).toBe('user-a');

  await context.setOffline(true);

  expect(await fetchTenant(page, 'user-b')).toBe('network-error');
  await expect(page.locator('#out')).not.toHaveText('user-a');
  expect(await storedProbeAnswers(page)).toEqual([]);
});

test('old_worker_open_tab_cannot_serve_protected_body: once this build loads, the old worker is replaced under an open old tab', async ({
  page,
  context,
}) => {
  await warmLegacyTab(page);

  // The first page of this build loads in another tab; the old tab stays open on old code. That
  // navigation's own update check, or the app's migration, installs the wrapper: whichever is first.
  await openThisBuild(context);
  await waitForProtectedWorker(page);
  await expect.poll(() => legacyProtectedEntries(page), { timeout: 15_000 }).toEqual([]);

  // Old code: no bypass header, same URL, another shop, slow network.
  await expectSlowShopBAnswered(page);
  expect(await legacyProtectedEntries(page)).toEqual([]);
});

test('lone_old_tab_is_covered_by_the_browser_update_check: no page of this build needed', async ({ page, context }) => {
  await warmLegacyTab(page);
  server.useCurrentDeployment();

  // Any navigation in scope makes the browser check the registered script, which is now the wrapper.
  const blank = await context.newPage();
  await blank.goto(`${server.origin}/__harness`);

  await waitForProtectedWorker(page);
  await expectSlowShopBAnswered(page);
});

test('old_code_registration_cannot_reinstate_the_old_worker: re-registering the shared URL fetches nothing', async ({
  page,
  context,
}) => {
  const legacyManifest = await warmLegacyTab(page);
  await openThisBuild(context);
  await waitForProtectedWorker(page);

  // Mid-rollout an old instance would answer with the bare worker. The old tab, already open, does
  // what older builds do once per load: register their worker URL, which is this one. (A navigation
  // would itself run the browser's update check, so the call comes from a page that stays put.)
  server.useLegacyDeployment(legacyManifest);
  const scriptFetches = (): number => server.requests.filter((request) => request.path === '/ngsw-worker.js').length;
  const before = scriptFetches();
  await page.evaluate(async () => {
    await navigator.serviceWorker.register('/ngsw-worker.js');
  });
  await page.waitForTimeout(1_000);

  expect(scriptFetches()).toBe(before);
  expect(await controlledByProtectedWorker(page)).toBe(true);
  server.useCurrentDeployment();
  await expectSlowShopBAnswered(page);
});

test('rollout_flip_is_undone_by_an_open_page_of_this_build: its migration replaces the worker again', async ({
  page,
  context,
}) => {
  const legacyManifest = await warmLegacyTab(page);
  await openThisBuild(context);
  await waitForProtectedWorker(page);

  // One update check reaches an old instance and reinstalls the bare worker; the next reaches this
  // build. Run from the open old tab: a new page's navigation would add an update check of its own.
  server.serveLegacyWorkerOnce(legacyManifest);
  await page.evaluate(async () => {
    await (await navigator.serviceWorker.getRegistration())?.update();
  });
  await expect.poll(() => controlledByProtectedWorker(page), { timeout: HANDOVER_MS }).toBe(false);

  // No page navigates: only the open app tab's controllerchange re-check can call update() now.
  await waitForProtectedWorker(page);
  await expectSlowShopBAnswered(page);
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

  const appTab = await openThisBuild(context);
  await waitForProtectedWorker(page);
  await expect.poll(() => legacyProtectedEntries(page), { timeout: 15_000 }).toEqual([]);

  const stored = await appTab.evaluate(async () => {
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
  await registerWorker(page);
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
