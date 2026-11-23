import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

import { expect, test, type BrowserContext, type Page } from '@playwright/test';

import { angularManifestHash } from '../../../src/app/core/service-worker/offline-pack-flag';
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
  missingFromCurrentPack,
  readBuiltFullManifest,
  readLeaseNextNumber,
  readPackFlag,
  readQueueRowsRaw,
  readWorkerState,
  seedLegacyBrowserState,
  sha256Hex,
  startLegacyRecoveryServer,
  vectors,
  type LegacyRecoveryServerHandle,
} from './legacy-queue-recovery-fixture';
import { TENANT_PROBE } from './release-server';

/**
 * Browser release proof for P05-06 against the built production bundle, real protected service worker,
 * manifest-verified offline pack, and real Chromium IndexedDB (`intelibill-offline-sales-queue` v1
 * and `intelibill-invoice-leases` v1).
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

  await stubWindowPrint(context);
  const signedRow = buildSignedPreUpgradeRow();
  const signedExpectedJson = JSON.stringify(signedRow['payload']);

  await warmLegacyWorkerAndSeed(page, handle, signedRow, signedExpectedJson);
  await handoverToProtectedFullPack(page, handle);
  await verifyOfflineReprintsAndAccess(page, context, handle.server.origin, signedExpectedJson);
  await exportAndImportSupportBundle(page, context, handle);
  await verifyReloadInvariants(page, handle, signedExpectedJson);

  if (process.env['P0506_EVIDENCE_DIR']) {
    await page.screenshot({ path: join(process.env['P0506_EVIDENCE_DIR'], 'p0506-reconciliation-after-import.png'), fullPage: true });
  }
  expect(pageErrors.filter((m) => !/fetch|network|Failed to load/i.test(m))).toEqual([]);
});

async function stubWindowPrint(context: BrowserContext): Promise<void> {
  await context.addInitScript(() => {
    (window as unknown as { __printCalls?: number }).__printCalls = 0;
    window.print = () => {
      (window as unknown as { __printCalls: number }).__printCalls += 1;
    };
  });
}

async function readPrintCalls(page: Page): Promise<number> {
  return page.evaluate(() => (window as unknown as { __printCalls: number }).__printCalls);
}

async function warmLegacyWorkerAndSeed(
  page: Page,
  srv: LegacyRecoveryServerHandle,
  signedRow: Record<string, unknown>,
  signedExpectedJson: string,
): Promise<void> {
  srv.server.useLegacyDeployment(await buildLegacyManifest(BUILD));
  await page.goto(`${srv.server.origin}/__harness`);
  await page.evaluate(async () => { await navigator.serviceWorker.register('/ngsw-worker.js'); });
  await page.waitForFunction(() => navigator.serviceWorker.controller !== null, undefined, { timeout: HANDOVER_MS });
  expect(await controlledByProtectedWorker(page)).toBe(false);

  const preProbe = await page.evaluate(async (path) => {
    const resp = await fetch(path, { headers: { Authorization: 'Shop-Legacy' } });
    return ((await resp.json()) as { tenant: string }).tenant;
  }, TENANT_PROBE);
  expect(preProbe).toBe('Shop-Legacy');

  await seedLegacyBrowserState(page, signedRow);
  assertPreservedPayloadBytes(await readQueueRowsRaw(page), signedExpectedJson);
}

async function handoverToProtectedFullPack(page: Page, srv: LegacyRecoveryServerHandle): Promise<void> {
  srv.server.useCurrentDeployment();
  await page.goto(`${srv.server.origin}/sales/offline-reconciliation`);
  await page.locator('.reconciliation-page').waitFor({ timeout: 30_000 });
  await expect.poll(() => controlledByProtectedWorker(page), { timeout: HANDOVER_MS }).toBe(true);

  await expect.poll(
    async () => (await page.evaluate(() => caches.keys())).filter((n) => isLegacyProtectedCacheName(n)),
    { timeout: 30_000 },
  ).toEqual([]);
  await expect.poll(() => readPackFlag(page), { timeout: PACK_READY_MS }).toBe('on');

  // Verify EVERY required prefetched URL of the NEW build's manifest is in its versioned caches (R007).
  const full = readBuiltFullManifest();
  const fullHash = await angularManifestHash(full);
  await page.evaluate(() => navigator.serviceWorker.controller?.postMessage({ action: 'CHECK_FOR_UPDATES', nonce: 1 }));
  await expect.poll(() => missingFromCurrentPack(page, full), { timeout: PACK_READY_MS }).toEqual([]);
  const swState = await readWorkerState(page);
  expect(swState).toContain('Driver state: NORMAL');
  expect(swState).toContain(`Latest manifest hash: ${fullHash}`);

  await expect.poll(() => srv.syncBodies.length, { timeout: 30_000 }).toBeGreaterThanOrEqual(1);
  const firstSync = JSON.parse(srv.syncBodies[0]) as { deviceId: string; sales: Record<string, unknown>[] };
  expect(firstSync.deviceId).toBe(DEVICE_ID);
  const byId = new Map(firstSync.sales.map((s) => [s['clientSaleId'] as string, s]));
  expect(JSON.stringify(byId.get('legacy-7ff1-0001'))).toBe(vectors.unversionedPreDocument.expectedSyncRequestJson);
  expect(JSON.stringify(byId.get('legacy-671d-0002'))).toBe(vectors.v1FrozenDocument.expectedSyncRequestJson);
  expect((byId.get('legacy-signed-0003')?.['licenceEvidence'] as { signedPermit: string })?.signedPermit).toBe(OLD_SIGNED_TOKEN);
}

async function verifyOfflineReprintsAndAccess(
  page: Page,
  context: BrowserContext,
  origin: string,
  signedExpectedJson: string,
): Promise<void> {
  await context.setOffline(true);
  const offlineProbe = await page.evaluate(async (path) => {
    try { return `status:${(await fetch(path, { headers: { Authorization: 'Shop-Offline' } })).status}`; }
    catch { return 'network-error'; }
  }, TENANT_PROBE);
  expect(offlineProbe).toBe('network-error');

  await assertFrozenV2Print(page, origin, 'a4');
  await assertFrozenV2Print(page, origin, 'thermal');
  await assertLegacyPreDocumentPrint(page, origin, 'a4');
  await assertLegacyPreDocumentPrint(page, origin, 'thermal');
  await assertOfflineGraceAndStaffDenial(page, origin);
  assertPreservedPayloadBytes(await readQueueRowsRaw(page), signedExpectedJson);
}

async function assertFrozenV2Print(page: Page, origin: string, template: 'a4' | 'thermal'): Promise<void> {
  await page.goto(`${origin}/sales/legacy-671d-0002/print?offline=1&template=${template}`);
  const doc = page.locator('.invoice-document');
  await expect(doc).toBeVisible({ timeout: 30_000 });
  await expect(doc).toContainText('A/2627/000002');
  await expect(doc).toContainText('Frozen Legacy Traders Pvt Ltd');
  await expect(doc).toContainText('29AAPFU0939F1ZR');
  await expect(doc).toContainText('Steel Bottle 1L');
  await expect(doc).toContainText('118');
  await expect(doc).not.toContainText('27NEWCHANGED1Z5');
  await expect(doc).not.toContainText('99 New Street');

  const before = await readPrintCalls(page);
  await page.locator('.screen-controls__button').first().click();
  await expect.poll(() => readPrintCalls(page)).toBeGreaterThan(before);
}

async function assertLegacyPreDocumentPrint(page: Page, origin: string, template: 'a4' | 'thermal'): Promise<void> {
  await page.goto(`${origin}/sales/legacy-7ff1-0001/print?offline=1&template=${template}`);
  await expect(page.locator('.print-state--notice')).toBeVisible({ timeout: 30_000 });
  const doc = page.locator('.invoice-document');
  await expect(doc).toBeVisible();
  await expect(doc).toContainText(vectors.unversionedPreDocument.invoiceNumber);
  await expect(doc).toContainText('Legacy Rice 1kg');
  await expect(doc).toContainText('200');
  await expect(doc).not.toContainText('27NEWCHANGED1Z5');
  await expect(doc).not.toContainText('99 New Street');
  await page.locator('.screen-controls__button').first().click();
}

async function assertOfflineGraceAndStaffDenial(page: Page, origin: string): Promise<void> {
  await page.evaluate((s) => localStorage.setItem('inventory.auth.session.local', JSON.stringify(s)), buildSession('Owner', OWNER_ID, true));
  await page.goto(`${origin}/sales/legacy-671d-0002/print?offline=1&template=a4`);
  await expect(page.locator('.invoice-document')).toContainText('A/2627/000002', { timeout: 30_000 });

  // Non-issuer Staff must reach terminal access-denied alert with zero invoice rendered and zero window.print calls (R006).
  await page.evaluate((s) => localStorage.setItem('inventory.auth.session.local', JSON.stringify(s)), buildSession('Staff', STAFF_ID, false));
  await page.goto(`${origin}/sales/legacy-671d-0002/print?offline=1&template=a4`);
  const deniedAlert = page.locator('.print-state--error[role="alert"]');
  await expect(deniedAlert).toBeVisible({ timeout: 30_000 });
  await expect(deniedAlert).toContainText('can be printed only by the person who issued it');
  await expect(page.locator('.invoice-document')).toHaveCount(0);
  expect(await readPrintCalls(page)).toBe(0);

  await page.evaluate((s) => localStorage.setItem('inventory.auth.session.local', JSON.stringify(s)), buildSession('Owner', OWNER_ID, false));
}

async function exportAndImportSupportBundle(page: Page, context: BrowserContext, srv: LegacyRecoveryServerHandle): Promise<void> {
  await context.setOffline(false);
  await page.goto(`${srv.server.origin}/sales/offline-reconciliation`);
  await expect(page.locator('.export-bundle-btn')).toBeEnabled({ timeout: 30_000 });

  const [download] = await Promise.all([page.waitForEvent('download'), page.locator('.export-bundle-btn').click()]);
  const downloadedPath = await download.path();
  expect(downloadedPath).toBeTruthy();
  const downloadedText = readFileSync(downloadedPath!, 'utf8');
  const bundle = JSON.parse(downloadedText) as { format: string; version: number; checksum: string; invoices: Record<string, unknown>[] };
  expect(bundle.format).toBe('intelibill.offline-recovery');
  expect(bundle.version).toBe(1);
  expect(sha256Hex(downloadedText.slice(downloadedText.indexOf('"invoices":') + '"invoices":'.length, -1))).toBe(bundle.checksum);

  const byId = new Map(bundle.invoices.map((i) => [i['clientSaleId'] as string, i]));
  expect([...byId.keys()].sort()).toEqual(['legacy-671d-0002', 'legacy-7ff1-0001', 'legacy-signed-0003']);
  expect(JSON.stringify(byId.get('legacy-7ff1-0001'))).toBe(vectors.unversionedPreDocument.expectedSyncRequestJson);
  expect(JSON.stringify(byId.get('legacy-671d-0002'))).toBe(vectors.v1FrozenDocument.expectedSyncRequestJson);
  expect((byId.get('legacy-signed-0003')!['licenceEvidence'] as { signedPermit: string }).signedPermit).toBe(OLD_SIGNED_TOKEN);

  await page.locator('.pending-export button').first().click();
  await page.locator('.file-input').setInputFiles(downloadedPath!);
  await expect(page.locator('.submit-import-btn')).toBeEnabled();
  await page.locator('.submit-import-btn').click();
  await expect(page.locator('.import-results .import-result')).toHaveCount(3, { timeout: 30_000 });

  expect(srv.recoveryBodies).toHaveLength(1);
  expect(srv.recoveryBodies[0]).toContain(`"bundle":${downloadedText}`);
}

async function verifyReloadInvariants(page: Page, srv: LegacyRecoveryServerHandle, signedExpectedJson: string): Promise<void> {
  await page.reload();
  await page.locator('.reconciliation-page').waitFor({ timeout: 30_000 });
  assertPreservedPayloadBytes(await readQueueRowsRaw(page), signedExpectedJson);
  expect(await readLeaseNextNumber(page)).toBe(INITIAL_NEXT_NUMBER);
  expect(srv.leaseReservationCount()).toBe(0);
}
