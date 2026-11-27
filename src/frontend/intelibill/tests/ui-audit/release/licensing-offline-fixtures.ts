import { readFileSync } from 'node:fs';

import { expect, type BrowserContext, type Page } from '@playwright/test';

import { angularManifestHash } from '../../../src/app/core/service-worker/offline-pack-flag';
import { isLegacyProtectedCacheName } from '../../../src/app/core/service-worker/protected-cache-names';
import {
  OWNER_ID,
  STAFF_ID,
  buildSession,
  controlledByProtectedWorker,
  missingFromCurrentPack,
  readBuiltFullManifest,
  readPackFlag,
  readWorkerState,
  sha256Hex,
} from './legacy-queue-recovery-fixture';
import {
  BARCODE,
  ITEM_NAME,
  canonicalInstant,
  installOfflineSaleFixture,
  readClockRow,
  readLeaseRow,
  type FixtureSigner,
} from './offline-permit-finalization-fixture';
import { SHOP_A } from './purchase-receiving-server';
import { TENANT_PROBE } from './release-server';

export const PACK_READY_MS = 120_000;
export const MSG_EXPIRED = 'Offline permission for this device has ended.';
export const MSG_CLOCK = "This device's clock does not match its earlier readings";

export const submit = (page: Page) => page.locator('app-cart-checkout-summary button').last();
export const queuedDialog = (page: Page) => page.getByRole('dialog', { name: 'Sale Queued' });

export async function enableWithFixture(
  page: Page,
  signer: FixtureSigner,
  origin: string,
  options?: Parameters<typeof installOfflineSaleFixture>[2],
) {
  const fixture = await installOfflineSaleFixture(page, signer, options);
  const initial = (options?.now?.() ?? Date.now()) - 120_000;
  await page.context().route('**/trading-authority*', (route) =>
    route.fulfill({
      json: {
        schemaVersion: 1, enforced: false, shopId: SHOP_A,
        authorityVerifiedAt: canonicalInstant(initial),
        validUntil: canonicalInstant(initial + 86_400_000),
      },
    }),
  );
  await page.goto(`${origin}/sales/new`);
  await page.locator('.shell-main').waitFor({ timeout: 30_000 });
  const toggle = page.locator('.offline-sales-toggle');
  await toggle.click();
  await expect(toggle).toHaveAttribute('aria-pressed', 'true', { timeout: PACK_READY_MS });
  await expect.poll(async () => !!(await readLeaseRow(page))?.lease.offlinePermit, { timeout: 30_000 }).toBe(true);
  await expect.poll(async () => (await readClockRow(page))?.checkpoint['blocked'], { timeout: 30_000 }).toBeNull();
  await expect.poll(() => controlledByProtectedWorker(page), { timeout: 45_000 }).toBe(true);
  return fixture;
}

export async function addBatchToCart(page: Page): Promise<void> {
  const input = page.locator('app-batch-search-bar input').first();
  await input.fill(BARCODE);
  await input.press('Enter');
  const picker = page.getByRole('dialog', { name: /Select a Batch/i });
  await expect(picker).toBeVisible();
  await picker.locator('.batch-option').first().press('Enter');
  await picker.getByRole('button', { name: /Add to Cart$/i }).click();
  await expect(picker).toBeHidden();
  await expect(page.locator('app-cart-table')).toContainText(ITEM_NAME);
}

export async function goOfflineFromWorker(page: Page, context: BrowserContext, assetCounter: () => number): Promise<void> {
  await context.setOffline(true);
  const beforeAssets = assetCounter();
  await page.reload();
  await page.locator('.shell-main').waitFor({ timeout: 30_000 });
  expect(assetCounter()).toBe(beforeAssets);
}

export async function triggerReconnect(page: Page): Promise<void> {
  await page.evaluate(() => {
    localStorage.removeItem('intelibill.offlineSales.offlinePackCycle.v1');
    window.dispatchEvent(new Event('online'));
  });
}

export async function verifyLegacyWorkerUpgrade(page: Page, origin: string): Promise<void> {
  await page.goto(`${origin}/sales/offline-reconciliation`);
  await page.locator('.reconciliation-page').waitFor({ timeout: 30_000 });
  await expect.poll(() => controlledByProtectedWorker(page), { timeout: 45_000 }).toBe(true);
  await expect.poll(async () => (await page.evaluate(() => caches.keys())).filter((n) => isLegacyProtectedCacheName(n)), { timeout: 30_000 }).toEqual([]);
  await expect.poll(() => readPackFlag(page), { timeout: PACK_READY_MS }).toBe('on');
  const full = readBuiltFullManifest();
  const fullHash = await angularManifestHash(full);
  await page.evaluate(() => navigator.serviceWorker.controller?.postMessage({ action: 'CHECK_FOR_UPDATES', nonce: 1 }));
  await expect.poll(() => missingFromCurrentPack(page, full), { timeout: PACK_READY_MS }).toEqual([]);
  expect(await readWorkerState(page)).toContain(`Latest manifest hash: ${fullHash}`);
}

export async function verifyLegacyOfflinePrintAndAccess(page: Page, context: BrowserContext, origin: string): Promise<void> {
  await context.setOffline(true);
  const probe = await page.evaluate(async (p) => {
    try { return `status:${(await fetch(p)).status}`; } catch { return 'network-error'; }
  }, TENANT_PROBE);
  expect(probe).toBe('network-error');

  for (const template of ['a4', 'thermal'] as const) {
    await page.goto(`${origin}/sales/legacy-671d-0002/print?offline=1&template=${template}`);
    await expect(page.locator('.invoice-document')).toContainText('Frozen Legacy Traders Pvt Ltd', { timeout: 30_000 });
    await expect(page.locator('.invoice-document')).not.toContainText('27NEWCHANGED1Z5');
    await page.goto(`${origin}/sales/legacy-7ff1-0001/print?offline=1&template=${template}`);
    await expect(page.locator('.invoice-document')).toContainText('Legacy Rice 1kg', { timeout: 30_000 });
  }
  await page.evaluate((s) => localStorage.setItem('inventory.auth.session.local', JSON.stringify(s)), buildSession('Staff', STAFF_ID, false));
  await page.goto(`${origin}/sales/legacy-671d-0002/print?offline=1&template=a4`);
  await expect(page.locator('.print-state--error[role="alert"]')).toBeVisible({ timeout: 30_000 });
  await page.evaluate((s) => localStorage.setItem('inventory.auth.session.local', JSON.stringify(s)), buildSession('Owner', OWNER_ID, false));
}

export async function verifyLegacyRecoveryRoundTrip(page: Page, context: BrowserContext, origin: string): Promise<string> {
  await context.setOffline(false);
  await page.goto(`${origin}/sales/offline-reconciliation`);
  await expect(page.locator('.export-bundle-btn')).toBeEnabled({ timeout: 30_000 });
  const [download] = await Promise.all([page.waitForEvent('download'), page.locator('.export-bundle-btn').click()]);
  const downloadedPath = (await download.path())!;
  const downloadedText = readFileSync(downloadedPath, 'utf8');
  const bundle = JSON.parse(downloadedText) as { checksum: string; invoices: Record<string, unknown>[] };
  expect(sha256Hex(downloadedText.slice(downloadedText.indexOf('"invoices":') + '"invoices":'.length, -1))).toBe(bundle.checksum);
  await page.locator('.pending-export button').first().click();
  await page.locator('.file-input').setInputFiles(downloadedPath);
  await page.locator('.submit-import-btn').click();
  await expect(page.locator('.import-results .import-result')).toHaveCount(3, { timeout: 30_000 });
  return bundle.checksum;
}
