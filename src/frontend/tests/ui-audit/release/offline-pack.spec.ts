import { existsSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

import { expect, test, type BrowserContext, type Page } from '@playwright/test';

import { angularManifestHash, OFFLINE_PACK_STATE_CACHE, OFFLINE_PACK_STATE_KEY } from '../../../src/app/core/service-worker/offline-pack-flag';
import {
  PROTECTED_WORKER_PROTOCOL,
  PROTECTED_WORKER_QUERY,
} from '../../../src/app/core/service-worker/service-worker-registration';
import { accessibilityFixtures } from './accessibility-localization-fixtures';
import { buildLegacyManifest } from './legacy-manifest';
import { SHOP_A, session, startPurchaseReceivingServer } from './purchase-receiving-server';

/**
 * The on-demand offline pack (docs/frontend-architecture.md § Offline Pack), proved against the
 * production build and its real worker: a device without offline billing downloads nothing in the
 * background, enabling downloads and verifies the full pack, the pack serves `/sales/new` with the
 * network cut, a later deployment is offered and applied through the shell's control, and a device
 * that had the pack before the flag existed keeps it.
 *
 * The signed-in shell is served by the receiving fixture server (`purchase-receiving-server.ts`,
 * static files through `release-server.ts`) with the accessibility fixtures' API answers.
 */

const BUILD = resolve(__dirname, '../../../dist/INVENTORY/browser');
const WORKER_READY_MS = 45_000;
const PACK_READY_MS = 120_000;

type FixtureServer = Awaited<ReturnType<typeof startPurchaseReceivingServer>>;
let server: FixtureServer | undefined;
let pageErrors: string[];

interface Manifest {
  readonly assetGroups: readonly { readonly name: string; readonly installMode: string; readonly urls: readonly string[] }[];
  readonly appData?: { readonly buildId?: string };
  readonly [key: string]: unknown;
}

const builtManifest = (file = 'ngsw.json'): Manifest => JSON.parse(readFileSync(join(BUILD, file), 'utf8')) as Manifest;
const prefetched = (manifest: Manifest): string[] =>
  manifest.assetGroups.filter((group) => group.installMode === 'prefetch').flatMap((group) => group.urls);

test.describe.configure({ timeout: 240_000 });

test.beforeAll(() => {
  for (const file of ['ngsw-worker.js', 'ngsw-angular-worker.js', 'ngsw.json', 'ngsw-lite.json']) {
    if (!existsSync(join(BUILD, file))) {
      throw new Error(`No ${file} in ${BUILD}. Run \`bun run build\` in src/frontend first.`);
    }
  }
});

test.beforeEach(async ({ page }) => {
  pageErrors = [];
  page.on('pageerror', (error) => pageErrors.push(error.message));
  server = await startPurchaseReceivingServer(BUILD);
});

test.afterEach(async () => {
  await server?.close();
  server = undefined;
});

function origin(): string {
  return server!.origin;
}

function staticRequests(): string[] {
  return server!.assets.requests.map((request) => request.path);
}

/** A remembered owner session for Shop A, and the API answers enabling needs. */
async function signIn(page: Page): Promise<void> {
  await page.context().addInitScript((value) => {
    localStorage.setItem('inventory.auth.session.local', JSON.stringify(value));
    localStorage.setItem('inventory.preferences.language', 'en-IN');
  }, session());
  await accessibilityFixtures(page);
  await page.context().route('**/api/sales/offline-snapshot/stream**', (route) => {
    const now = new Date().toISOString();
    const lines = [
      {
        type: 'metadata',
        metadata: {
          snapshotId: 'snapshot-pack',
          shopId: SHOP_A,
          schemaVersion: 4,
          businessTimeZoneId: 'Asia/Kolkata',
          catalogVersion: 1,
          startedAt: now,
        },
      },
      {
        type: 'complete',
        complete: {
          snapshotId: 'snapshot-pack',
          completedAt: now,
          batchCount: 0,
          customerCount: 0,
          discountRuleCount: 0,
          activeLeaseCount: 0,
          serviceCount: 0,
        },
      },
    ];
    return route.fulfill({ contentType: 'application/x-ndjson', body: `${lines.map((line) => JSON.stringify(line)).join('\n')}\n` });
  });
  await page.context().route('**/api/sales/invoice-leases/reserve**', (route) => {
    const { deviceId } = route.request().postDataJSON() as { deviceId: string };
    return route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        leaseId: 'lease-pack',
        shopId: SHOP_A,
        deviceId,
        fiscalYear: '2026-2027',
        prefix: 'A/2627/',
        numberPadding: 6,
        rangeStart: 1,
        rangeEnd: 100,
        nextNumber: 1,
        remainingCount: 100,
        reservedAt: new Date().toISOString(),
        expiresAt: '2099-01-01T00:00:00.000Z',
      }),
    });
  });
}

async function openSales(page: Page): Promise<void> {
  await page.goto(`${origin()}/sales/new`);
  await page.locator('.shell-main').waitFor({ timeout: 30_000 });
}

async function waitForWorker(page: Page): Promise<void> {
  await page.waitForFunction(() => navigator.serviceWorker.controller !== null, undefined, { timeout: WORKER_READY_MS });
}

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

/** Every URL held by an asset cache of any app version, by path. */
async function cachedAssetPaths(page: Page): Promise<string[]> {
  return page.evaluate(async () => {
    const paths: string[] = [];
    for (const name of await caches.keys()) {
      if (!/:assets:[^:]+:cache$/.test(name)) continue;
      for (const request of await (await caches.open(name)).keys()) {
        paths.push(new URL(request.url).pathname);
      }
    }
    return paths.sort();
  });
}

/** The prefetched files of `manifest` missing from the caches the worker files that version under. */
async function missingFromPack(page: Page, manifest: Manifest): Promise<string[]> {
  const hash = await angularManifestHash(manifest);
  const groups = manifest.assetGroups.filter((group) => group.installMode === 'prefetch').map((group) => ({ name: group.name, urls: group.urls }));
  return page.evaluate(
    async ({ hash: version, groups: wanted }) => {
      const missing: string[] = [];
      for (const group of wanted) {
        const name = `ngsw:/:${version}:assets:${group.name}:cache`;
        const cached = (await caches.has(name))
          ? new Set((await (await caches.open(name)).keys()).map((request) => new URL(request.url).pathname))
          : new Set<string>();
        missing.push(...group.urls.filter((url) => !cached.has(url)));
      }
      return missing;
    },
    { hash, groups },
  );
}

async function packFlag(page: Page): Promise<string | null> {
  return page.evaluate(
    async ({ cacheName, key }) => {
      const response = await caches.match(new URL(key, document.baseURI).href, { cacheName });
      return response ? response.text() : null;
    },
    { cacheName: OFFLINE_PACK_STATE_CACHE, key: OFFLINE_PACK_STATE_KEY },
  );
}

async function workerState(page: Page): Promise<string> {
  return page.evaluate(async () => (await fetch('/ngsw/state')).text());
}

async function enableOfflineFromShell(page: Page): Promise<void> {
  const toggle = page.locator('.offline-sales-toggle');
  await expect(toggle).toHaveAttribute('aria-pressed', 'false');
  await toggle.click();
  await expect(toggle).toHaveAttribute('aria-pressed', 'true', { timeout: PACK_READY_MS });
}

test('fresh device with offline off: only the opened route is downloaded and the worker holds no app files', async ({ page }) => {
  await signIn(page);
  await openSales(page);
  await expect(page.locator('app-new-sale-page')).toBeAttached();
  await waitForWorker(page);

  // The worker asked for its manifest and got the lite one: there is nothing for it to prefetch.
  await expect.poll(() => staticRequests().includes('/ngsw-lite.json'), { timeout: WORKER_READY_MS }).toBe(true);
  await page.waitForTimeout(5_000);

  expect(staticRequests()).not.toContain('/ngsw.json');
  expect(await packFlag(page)).toBe('off');
  expect(await cachedAssetPaths(page)).toEqual([]);

  // Every chunk the server sent is one this page loaded itself: no background download.
  const loaded = await page.evaluate(() =>
    performance.getEntriesByType('resource').map((entry) => new URL(entry.name).pathname),
  );
  const served = [...new Set(staticRequests().filter((path) => /^\/chunk-[A-Z0-9]+\.js$/.test(path)))];
  const allChunks = prefetched(builtManifest()).filter((url) => /^\/chunk-[A-Z0-9]+\.js$/.test(url));
  expect(served.length).toBeGreaterThan(0);
  expect(served.filter((path) => !loaded.includes(path))).toEqual([]);
  expect(served.length).toBeLessThan(allChunks.length);
  expect(pageErrors).toEqual([]);
});

test('enabling downloads and verifies the full pack, and /sales/new then reloads with the network cut', async ({ page, context }) => {
  await signIn(page);
  await openSales(page);

  await enableOfflineFromShell(page);

  const full = builtManifest();
  expect(staticRequests()).toContain('/ngsw.json');
  expect(await packFlag(page)).toBe('on');
  expect(await missingFromPack(page, full)).toEqual([]);
  const status = await page.evaluate(() => JSON.parse(localStorage.getItem('intelibill.offlineSales.offlinePack.v1') ?? 'null'));
  expect(status).toMatchObject({ state: 'ready', buildId: full.appData?.buildId, manifestHash: await angularManifestHash(full) });
  expect(await workerState(page)).toContain('Driver state: NORMAL');

  // Nothing under /api or /hubs is in any cache.
  const everyCachedUrl = await page.evaluate(async () =>
    (await Promise.all((await caches.keys()).map(async (name) => (await (await caches.open(name)).keys()).map((r) => r.url)))).flat(),
  );
  expect(everyCachedUrl.filter((url) => /\/(api|hubs)\//.test(new URL(url).pathname))).toEqual([]);

  // The network goes away; the counter reloads the billing screen.
  await context.unrouteAll({ behavior: 'ignoreErrors' });
  await context.setOffline(true);
  const before = staticRequests().length;
  await page.reload();
  await page.locator('.shell-main').waitFor({ timeout: 30_000 });
  await expect(page).toHaveURL(/\/sales\/new$/);
  await expect(page.locator('app-new-sale-page')).toBeAttached();
  await expect(page.locator('.offline-sales-toggle')).toHaveAttribute('aria-pressed', 'true');
  // Served from the pack: the server saw nothing.
  expect(staticRequests().length).toBe(before);
  expect(pageErrors.filter((message) => !/fetch|network|Failed to load/i.test(message))).toEqual([]);
});

test('an interrupted download refuses enabling with a clear message and puts the flag back to off', async ({ page }) => {
  await signIn(page);
  await openSales(page);
  // A locale this page never reads, so only the pack download notices.
  server!.assets.setFailingPaths(['/assets/i18n/ta-IN.json']);

  const toggle = page.locator('.offline-sales-toggle');
  await toggle.click();

  await expect(toggle).toHaveAttribute('title', /could not be downloaded completely/, { timeout: PACK_READY_MS });
  await expect(toggle).toHaveAttribute('aria-pressed', 'false');
  expect(await packFlag(page)).toBe('off');
  expect(await page.evaluate(() => localStorage.getItem('intelibill.offlineSales.offlinePack.v1'))).toBeNull();
});

test('a later deployment is picked up by a check and applied through the update control', async ({ page }) => {
  await signIn(page);
  await openSales(page);
  await waitForWorker(page);
  await expect.poll(() => staticRequests().includes('/ngsw-lite.json'), { timeout: WORKER_READY_MS }).toBe(true);
  // The next app start: the worker serves this tab from its first request. (A tab that loaded
  // before the worker existed is only assigned a version by its next app-file request, and Angular
  // tells only assigned tabs about a new one.)
  await page.reload();
  await page.locator('.shell-main').waitFor({ timeout: 30_000 });

  // The next deployment: a different build as far as the worker and the page can tell.
  const next = (file: 'ngsw.json' | 'ngsw-lite.json', manifest: Record<string, unknown>) => ({
    ...manifest,
    timestamp: Number(manifest['timestamp']) + 1,
    appData: { ...(manifest['appData'] as object), buildId: 'next-build' },
  });
  server!.assets.setManifestTransform(next);
  const nextLiteHash = await angularManifestHash(next('ngsw-lite.json', builtManifest('ngsw-lite.json')));

  const notice = page.locator('.shell-app-update');
  await expect
    .poll(
      async () => {
        await page.evaluate(() => window.dispatchEvent(new Event('online')));
        return notice.isVisible();
      },
      { timeout: 60_000, intervals: [2_000] },
    )
    .toBe(true);
  await expect(notice).toHaveAttribute('role', 'status');
  // Never applied on its own.
  await page.evaluate(() => ((window as unknown as { beforeUpdate: boolean }).beforeUpdate = true));
  await page.waitForTimeout(2_000);
  expect(await page.evaluate(() => (window as unknown as { beforeUpdate?: boolean }).beforeUpdate)).toBe(true);

  await notice.locator('.shell-app-update-action').click();

  await expect.poll(() => page.evaluate(() => (window as unknown as { beforeUpdate?: boolean }).beforeUpdate ?? false), { timeout: 30_000 }).toBe(false);
  await page.locator('.shell-main').waitFor({ timeout: 30_000 });
  await expect(page.locator('.shell-app-update')).toHaveCount(0);
  expect(await workerState(page)).toContain(`Latest manifest hash: ${nextLiteHash}`);
  expect(pageErrors).toEqual([]);
});

test('upgrade: a device that had the pack before the flag existed keeps it', async ({ page, context }) => {
  // The device as earlier releases left it: a worker with the whole app prefetched, no flag.
  const legacyManifest = await buildLegacyManifest(BUILD);
  server!.assets.useLegacyDeployment(legacyManifest);
  await page.goto(`${origin()}/__harness`);
  await page.evaluate(async () => {
    await navigator.serviceWorker.register('/ngsw-worker.js');
  });
  await waitForWorker(page);
  await expect.poll(async () => (await cachedAssetPaths(page)).includes('/index.html'), { timeout: PACK_READY_MS }).toBe(true);
  expect(await packFlag(page)).toBeNull();

  // This release is deployed; the browser's own update check installs the new worker before any
  // page of this build has run (so no flag is written).
  server!.assets.useCurrentDeployment();
  const blank = await context.newPage();
  await blank.goto(`${origin()}/__harness`);
  await expect.poll(() => controlledByProtectedWorker(page), { timeout: 45_000 }).toBe(true);
  await page.evaluate(() => {
    navigator.serviceWorker.controller?.postMessage({ action: 'CHECK_FOR_UPDATES', nonce: 1 });
  });

  const full = builtManifest();
  await expect.poll(() => missingFromPack(page, full), { timeout: PACK_READY_MS }).toEqual([]);
  expect(staticRequests()).not.toContain('/ngsw-lite.json');

  // Then the app opens on this device, where offline billing was already on for Shop A.
  await page.evaluate((shopId) => {
    localStorage.setItem(`intelibill.offlineSales.deviceId.v1:${shopId}`, 'device-upgrade');
    localStorage.setItem(
      `intelibill.offlineSales.deviceSettings.v1:${shopId}:device-upgrade`,
      JSON.stringify({ shopId, deviceId: 'device-upgrade', label: '', enabled: true }),
    );
  }, SHOP_A);
  await blank.close();
  await signIn(page);
  await openSales(page);

  await expect.poll(() => packFlag(page), { timeout: 30_000 }).toBe('on');
  await page.evaluate(() => {
    navigator.serviceWorker.controller?.postMessage({ action: 'CHECK_FOR_UPDATES', nonce: 2 });
  });
  await page.waitForTimeout(3_000);
  expect(staticRequests()).not.toContain('/ngsw-lite.json');
  expect(await missingFromPack(page, full)).toEqual([]);
});
