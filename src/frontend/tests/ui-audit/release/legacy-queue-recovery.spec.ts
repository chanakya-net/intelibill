import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

import { expect, test } from '@playwright/test';

import { OFFLINE_PACK_STATE_CACHE, OFFLINE_PACK_STATE_KEY } from '../../../src/app/core/service-worker/offline-pack-flag';
import { isLegacyProtectedCacheName } from '../../../src/app/core/service-worker/protected-cache-names';
import { buildLegacyManifest } from './legacy-manifest';
import {
  BUILD,
  DEVICE_ID,
  INITIAL_NEXT_NUMBER,
  OLD_SIGNED_TOKEN,
  OWNER_ID,
  STAFF_ID,
  assertPreservedPayloadBytes,
  buildSession,
  buildSignedPreUpgradeRow,
  controlledByProtectedWorker,
  readLeaseNextNumber,
  readQueueRowsRaw,
  seedLegacyBrowserState,
  sha256Hex,
  startLegacyRecoveryServer,
  vectors,
  type LegacyRecoveryServerHandle,
} from './legacy-queue-recovery-fixture';
import { TENANT_PROBE } from './release-server';

/**
 * Browser release proof for P05-06 against the built production bundle, real protected service worker,
 * offline pack, and real Chromium IndexedDB (`intelibill-offline-sales-queue` v1 and `intelibill-invoice-leases` v1).
 *
 * Evidence boundary: API answers below are local HTTP fixtures matching the merchant API contracts;
 * real PostgreSQL persistence, signature verification, race serialization and replay authorization live
 * in `P0506LicensingTests`. No live licensing provider or Azure Key Vault is contacted.
 */

const PACK_READY_MS = 120_000;
const HANDOVER_MS = 45_000;

let handle: LegacyRecoveryServerHandle | undefined;

test.describe.configure({ timeout: 240_000 });

test.beforeAll(() => {
  for (const file of ['ngsw-worker.js', 'ngsw-angular-worker.js', 'ngsw.json', 'ngsw-lite.json']) {
    if (!existsSync(join(BUILD, file))) {
      throw new Error(`No ${file} in ${BUILD}. Run \`bun run build\` in src/frontend first.`);
    }
  }
});

test.afterEach(async () => {
  await handle?.server.close();
  handle = undefined;
});

test('P05-06 legacy queue survives upgrade, reprint and import', async ({ page, context }) => {
  const pageErrors: string[] = [];
  page.on('pageerror', (error) => pageErrors.push(error.message));
  handle = await startLegacyRecoveryServer();
  const { server, syncBodies, recoveryBodies } = handle;

  // Stub window.print so A4/thermal print pages run cleanly in headless Chromium and count calls.
  await context.addInitScript(() => {
    (window as unknown as { __printCalls?: number }).__printCalls = 0;
    window.print = () => {
      (window as unknown as { __printCalls: number }).__printCalls += 1;
    };
  });

  // 1. Old deployment side: legacy bare worker + legacy manifest with api-fresh cache,
  //    then seed v1 IndexedDB rows directly BEFORE the new build ever opens the store.
  const legacyManifest = await buildLegacyManifest(BUILD);
  server.useLegacyDeployment(legacyManifest);
  await page.goto(`${server.origin}/__harness`);
  await page.evaluate(async () => {
    await navigator.serviceWorker.register('/ngsw-worker.js');
  });
  await page.waitForFunction(() => navigator.serviceWorker.controller !== null, undefined, { timeout: HANDOVER_MS });
  expect(await controlledByProtectedWorker(page)).toBe(false);

  const preUpgradeProbe = await page.evaluate(async (path) => {
    const resp = await fetch(path, { headers: { Authorization: 'Shop-Legacy' } });
    return ((await resp.json()) as { tenant: string }).tenant;
  }, TENANT_PROBE);
  expect(preUpgradeProbe).toBe('Shop-Legacy');

  const signedRow = buildSignedPreUpgradeRow();
  const signedExpectedJson = JSON.stringify(signedRow['payload']);
  await seedLegacyBrowserState(page, signedRow);
  assertPreservedPayloadBytes(await readQueueRowsRaw(page), signedExpectedJson);

  // 2. Hand over on the SAME origin/profile to the built protected worker and full offline pack.
  server.useCurrentDeployment();
  await page.goto(`${server.origin}/sales/offline-reconciliation`);
  await page.locator('.reconciliation-page').waitFor({ timeout: 30_000 });
  await expect.poll(() => controlledByProtectedWorker(page), { timeout: HANDOVER_MS }).toBe(true);

  await expect.poll(
    async () => (await page.evaluate(() => caches.keys())).filter((name) => isLegacyProtectedCacheName(name)),
    { timeout: 30_000 },
  ).toEqual([]);
  await expect.poll(
    async () =>
      page.evaluate(
        async ({ cacheName, key }) => (await caches.match(new URL(key, document.baseURI).href, { cacheName }))?.text() ?? null,
        { cacheName: OFFLINE_PACK_STATE_CACHE, key: OFFLINE_PACK_STATE_KEY },
      ),
    { timeout: PACK_READY_MS },
  ).toBe('on');
  await expect.poll(
    async () => page.evaluate(async () => (await caches.keys()).filter((n) => n.includes(':assets:')).length),
    { timeout: PACK_READY_MS },
  ).toBeGreaterThan(0);

  // Wait for startup reconnect sync of pending rows -> held as NeedsReview so rows stay exportable.
  await expect.poll(() => syncBodies.length, { timeout: 30_000 }).toBeGreaterThanOrEqual(1);
  const firstSyncParsed = JSON.parse(syncBodies[0]) as { deviceId: string; sales: Record<string, unknown>[] };
  expect(firstSyncParsed.deviceId).toBe(DEVICE_ID);
  const syncedById = new Map(firstSyncParsed.sales.map((s) => [s['clientSaleId'] as string, s]));
  expect(JSON.stringify(syncedById.get('legacy-7ff1-0001'))).toBe(vectors.unversionedPreDocument.expectedSyncRequestJson);
  expect(JSON.stringify(syncedById.get('legacy-671d-0002'))).toBe(vectors.v1FrozenDocument.expectedSyncRequestJson);
  expect((syncedById.get('legacy-signed-0003')?.['licenceEvidence'] as { signedPermit: string })?.signedPermit).toBe(OLD_SIGNED_TOKEN);

  // 3. Cut the network and reload offline -> protected API cannot fall back to cache,
  //    then open both legacy shapes in A4 and thermal templates and invoke Print again.
  await context.setOffline(true);
  const offlineProbe = await page.evaluate(async (path) => {
    try {
      const resp = await fetch(path, { headers: { Authorization: 'Shop-Offline' } });
      return `status:${resp.status}`;
    } catch {
      return 'network-error';
    }
  }, TENANT_PROBE);
  expect(offlineProbe).toBe('network-error');

  for (const template of ['a4', 'thermal'] as const) {
    await page.goto(`${server.origin}/sales/legacy-671d-0002/print?offline=1&template=${template}`);
    const doc = page.locator('.invoice-document');
    await expect(doc).toBeVisible({ timeout: 30_000 });
    await expect(doc).toContainText('A/2627/000002');
    await expect(doc).toContainText('Frozen Legacy Traders Pvt Ltd');
    await expect(doc).toContainText('29AAPFU0939F1ZR');
    await expect(doc).toContainText('Steel Bottle 1L');
    await expect(doc).toContainText('118');
    await expect(doc).not.toContainText('27NEWCHANGED1Z5');
    await expect(doc).not.toContainText('99 New Street');

    const beforePrints = await page.evaluate(() => (window as unknown as { __printCalls: number }).__printCalls);
    await page.locator('.screen-controls__button').first().click();
    await expect.poll(() => page.evaluate(() => (window as unknown as { __printCalls: number }).__printCalls)).toBeGreaterThan(beforePrints);
  }

  for (const template of ['a4', 'thermal'] as const) {
    await page.goto(`${server.origin}/sales/legacy-7ff1-0001/print?offline=1&template=${template}`);
    await expect(page.locator('.print-state--notice')).toBeVisible({ timeout: 30_000 });
    const doc = page.locator('.invoice-document');
    await expect(doc).toBeVisible();
    await expect(doc).toContainText('A/2627/000001');
    await expect(doc).toContainText('Legacy Rice 1kg');
    await expect(doc).toContainText('200');
    await expect(doc).not.toContainText('27NEWCHANGED1Z5');
    await expect(doc).not.toContainText('99 New Street');
    await page.locator('.screen-controls__button').first().click();
  }

  // Offline grace check: expired access token with valid refresh token + usable snapshot grants issuer; another Staff user is denied.
  await page.evaluate((expiredOwner) => {
    localStorage.setItem('inventory.auth.session.local', JSON.stringify(expiredOwner));
  }, buildSession('Owner', OWNER_ID, true));
  await page.goto(`${server.origin}/sales/legacy-671d-0002/print?offline=1&template=a4`);
  await expect(page.locator('.invoice-document')).toContainText('A/2627/000002', { timeout: 30_000 });

  await page.evaluate((staffSession) => {
    localStorage.setItem('inventory.auth.session.local', JSON.stringify(staffSession));
  }, buildSession('Staff', STAFF_ID, false));
  await page.goto(`${server.origin}/sales/legacy-671d-0002/print?offline=1&template=a4`);
  await expect(page.locator('.invoice-document')).toHaveCount(0);

  await page.evaluate((ownerSession) => {
    localStorage.setItem('inventory.auth.session.local', JSON.stringify(ownerSession));
  }, buildSession('Owner', OWNER_ID, false));
  assertPreservedPayloadBytes(await readQueueRowsRaw(page), signedExpectedJson);

  // 4. Reconnect, export support bundle from /sales/offline-reconciliation, confirm download,
  //    and import the ACTUAL downloaded file as owner.
  await context.setOffline(false);
  await page.goto(`${server.origin}/sales/offline-reconciliation`);
  await page.locator('.export-bundle-btn').waitFor({ timeout: 30_000 });
  await expect(page.locator('.export-bundle-btn')).toBeEnabled({ timeout: 30_000 });

  const [download] = await Promise.all([
    page.waitForEvent('download'),
    page.locator('.export-bundle-btn').click(),
  ]);
  const downloadedPath = await download.path();
  expect(downloadedPath).toBeTruthy();
  const downloadedText = readFileSync(downloadedPath!, 'utf8');
  const downloadedBundle = JSON.parse(downloadedText) as {
    format: string;
    version: number;
    checksum: string;
    invoices: Record<string, unknown>[];
  };
  expect(downloadedBundle.format).toBe('intelibill.offline-recovery');
  expect(downloadedBundle.version).toBe(1);
  const exportedInvoicesRaw = downloadedText.slice(downloadedText.indexOf('"invoices":') + '"invoices":'.length, -1);
  expect(sha256Hex(exportedInvoicesRaw)).toBe(downloadedBundle.checksum);
  const exportedById = new Map(downloadedBundle.invoices.map((i) => [i['clientSaleId'] as string, i]));
  expect([...exportedById.keys()].sort()).toEqual(['legacy-671d-0002', 'legacy-7ff1-0001', 'legacy-signed-0003']);
  expect(JSON.stringify(exportedById.get('legacy-7ff1-0001'))).toBe(vectors.unversionedPreDocument.expectedSyncRequestJson);
  expect(JSON.stringify(exportedById.get('legacy-671d-0002'))).toBe(vectors.v1FrozenDocument.expectedSyncRequestJson);
  expect((exportedById.get('legacy-signed-0003')!['licenceEvidence'] as { signedPermit: string }).signedPermit).toBe(OLD_SIGNED_TOKEN);

  await page.locator('.pending-export button').first().click();
  await page.locator('.file-input').setInputFiles(downloadedPath!);
  await expect(page.locator('.submit-import-btn')).toBeEnabled();
  await page.locator('.submit-import-btn').click();
  await expect(page.locator('.import-results .import-result')).toHaveCount(3, { timeout: 30_000 });

  expect(recoveryBodies).toHaveLength(1);
  const submittedRaw = recoveryBodies[0];
  expect(submittedRaw).toContain(`"bundle":${downloadedText}`);
  const submittedInvoicesRaw = submittedRaw.slice(submittedRaw.indexOf('"invoices":') + '"invoices":'.length, -2);
  expect(sha256Hex(submittedInvoicesRaw)).toBe(downloadedBundle.checksum);

  // 5. Second reload: IDB payload bytes, signed token, lease cursor, queue count, and zero new reservations hold.
  await page.reload();
  await page.locator('.reconciliation-page').waitFor({ timeout: 30_000 });
  assertPreservedPayloadBytes(await readQueueRowsRaw(page), signedExpectedJson);
  expect(await readLeaseNextNumber(page)).toBe(INITIAL_NEXT_NUMBER);
  expect(handle.leaseReservationCount()).toBe(0);

  if (process.env['P0506_EVIDENCE_DIR']) {
    await page.screenshot({ path: join(process.env['P0506_EVIDENCE_DIR'], 'p0506-reconciliation-after-import.png'), fullPage: true });
  }

  expect(pageErrors.filter((m) => !/fetch|network|Failed to load/i.test(m))).toEqual([]);
});
