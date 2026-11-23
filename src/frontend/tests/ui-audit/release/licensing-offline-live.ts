import { createHash } from 'node:crypto';
import { existsSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

import { expect, type BrowserContext, type Page } from '@playwright/test';

import {
  controlledByProtectedWorker,
  missingFromCurrentPack,
  readPackFlag,
  readWorkerState,
  type BuiltManifest,
} from './legacy-queue-recovery-fixture';
import { readAll, readRecord, type LeaseRow } from './offline-permit-finalization-fixture';

export const sha256Hex = (value: string): string => createHash('sha256').update(value, 'utf8').digest('hex');

interface LiveConfig {
  readonly origin: string;
  readonly email: string;
  readonly password: string;
  readonly barcode: string;
  readonly evidence: string;
}

export function readLiveConfig(): LiveConfig {
  return {
    origin: process.env['P0509_ORIGIN']!,
    email: process.env['P0509_EMAIL']!,
    password: process.env['P0509_PASSWORD']!,
    barcode: process.env['P0509_BARCODE']!,
    evidence: process.env['P0509_EVIDENCE']!,
  };
}

function writePhase(evidence: string, phase: string, data: object): void {
  const path = join(evidence, `observations-${phase}.json`);
  writeFileSync(path + '.tmp', JSON.stringify(data, null, 2));
  renameSync(path + '.tmp', path);
}

async function waitForSignal(evidence: string, filename: string): Promise<string> {
  const full = join(evidence, filename);
  await expect.poll(() => existsSync(full), { timeout: 60_000 }).toBe(true);
  return readFileSync(full, 'utf8').trim();
}

async function signInMerchant(page: Page, config: LiveConfig): Promise<{ userId: string; shopId: string; sessionId: string }> {
  await page.goto(`${config.origin}/login`);
  await page.locator('#identifier').fill(config.email);
  await page.locator('#password').evaluate((input: HTMLInputElement, secret) => {
    input.value = secret;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }, config.password);
  const login = page.waitForResponse(
    (r) => new URL(r.url()).pathname === '/api/auth/login' && r.request().method() === 'POST',
  );
  await page.locator('button[type=submit]').click();
  const response = await login;
  expect(response.status()).toBe(200);
  await expect(page).not.toHaveURL(/\/login/);

  return page.evaluate(() => {
    const raw = JSON.parse(localStorage.getItem('inventory.auth.session.local') ?? '{}');
    return {
      userId: String(raw.user?.id ?? ''),
      shopId: String(raw.activeShopId ?? ''),
      sessionId: String(raw.sessionId ?? ''),
    };
  });
}

async function readScopedLease(page: Page, shopId: string, deviceId: string): Promise<LeaseRow | null> {
  const rows = await readAll<LeaseRow>(page, 'intelibill-invoice-leases', 'invoice-leases');
  return rows.find((row) => row.key.startsWith(`${shopId}::${deviceId}::`) && !row.key.startsWith('clock::')) ?? null;
}

async function readScopedClock(page: Page, shopId: string, deviceId: string, userId: string) {
  return readRecord<{ key: string; checkpoint: Record<string, unknown> }>(
    page,
    'intelibill-invoice-leases',
    'invoice-leases',
    `clock::${shopId}::${deviceId}::${userId}`,
  );
}

async function readScopedQueue(page: Page, shopId: string, deviceId: string) {
  const rows = await readAll<{
    key: string;
    status: string;
    clientSaleId: string;
    invoiceNumber: string;
    shopId: string;
    deviceId: string;
    payload: Record<string, unknown>;
    reviewCaseId?: string | null;
    errorCode?: string | null;
    serverSaleId?: string | null;
  }>(page, 'intelibill-offline-sales-queue', 'queued-sales');
  return rows.filter((row) => row.shopId === shopId && row.deviceId === deviceId);
}

async function enableOfflineAndVerifyPack(page: Page, config: LiveConfig, scope: { userId: string; shopId: string }) {
  await page.goto(`${config.origin}/sales/new`);
  await page.locator('.shell-main').waitFor({ timeout: 30_000 });
  const toggle = page.locator('.offline-sales-toggle');
  await toggle.click();
  await expect(toggle).toHaveAttribute('aria-pressed', 'true', { timeout: 120_000 });

  const deviceId = await page.evaluate(
    (shopId) => localStorage.getItem(`intelibill.offlineSales.deviceId.v1:${shopId}`),
    scope.shopId,
  );
  expect(deviceId).toBeTruthy();

  await expect
    .poll(async () => (await readScopedLease(page, scope.shopId, deviceId!))?.lease.offlinePermit?.['signedPermit'] ?? null, {
      timeout: 30_000,
    })
    .toBeTruthy();
  await expect
    .poll(async () => (await readScopedClock(page, scope.shopId, deviceId!, scope.userId))?.checkpoint['blocked'], {
      timeout: 30_000,
    })
    .toBeNull();

  await expect.poll(() => controlledByProtectedWorker(page), { timeout: 45_000 }).toBe(true);
  await expect.poll(() => readPackFlag(page), { timeout: 45_000 }).toBe('on');
  const manifest = await page.evaluate(async () => (await fetch('/ngsw.json', { cache: 'no-store' })).json() as Promise<BuiltManifest>);
  await expect.poll(() => missingFromCurrentPack(page, manifest), { timeout: 60_000 }).toEqual([]);
  expect(await readWorkerState(page)).toContain('Driver state: NORMAL');

  const leaseRow = (await readScopedLease(page, scope.shopId, deviceId!))!;
  return { deviceId: deviceId!, leaseRow };
}

async function addGoodsAndQueueOffline(page: Page, barcode: string): Promise<void> {
  const input = page.locator('app-batch-search-bar input').first();
  await input.fill(barcode);
  await input.press('Enter');
  const picker = page.getByRole('dialog', { name: /Select a Batch/i });
  await expect(picker).toBeVisible({ timeout: 15_000 });
  await picker.locator('.batch-option').first().press('Enter');
  await picker.getByRole('button', { name: /Add to Cart$/i }).click();
  await expect(picker).toBeHidden();
  await page.locator('app-cart-checkout-summary button').last().click();
  await expect(page.getByRole('dialog', { name: 'Sale Queued' })).toBeVisible({ timeout: 30_000 });
}

async function disconnectAndProveOffline(page: Page, context: BrowserContext): Promise<void> {
  await context.setOffline(true);
  await page.reload();
  await page.locator('.shell-main').waitFor({ timeout: 30_000 });
  const probe = await page.evaluate(async () => {
    try {
      const resp = await fetch('/api/ping?ngsw-bypass=true', { cache: 'no-store' });
      return `status:${resp.status}`;
    } catch {
      return 'network-error';
    }
  });
  expect(probe).toBe('network-error');
}

async function reconnectAndCaptureSync(
  page: Page,
  context: BrowserContext,
  scope: { shopId: string },
  deviceId: string,
  clientSaleId: string,
  rawPayloadSha256: string,
  expectedQueueStatus: 'Synced' | 'NeedsReview',
) {
  let wireBody: { deviceId: string; sales: Record<string, unknown>[] } | null = null;
  let httpStatus = 0;
  let responseBodyPromise: Promise<{ results: Record<string, unknown>[] } | null> | null = null;
  const onResponse = (res: import('@playwright/test').Response) => {
    if (new URL(res.url()).pathname !== '/api/sales/offline-sync' || res.request().method() !== 'POST') return;
    if (wireBody) return;
    wireBody = res.request().postDataJSON() as { deviceId: string; sales: Record<string, unknown>[] };
    httpStatus = res.status();
    responseBodyPromise = res.json().catch(() => null) as Promise<{ results: Record<string, unknown>[] } | null>;
  };
  page.on('response', onResponse);

  try {
    await context.setOffline(false);
    await page.evaluate(() => window.dispatchEvent(new Event('online')));
    await expect.poll(() => httpStatus, { timeout: 60_000 }).toBe(200);
    const responseBody = responseBodyPromise ? await responseBodyPromise : null;
    const wireSale = wireBody!.sales.find((s) => s['clientSaleId'] === clientSaleId)!;
    expect(wireSale).toBeTruthy();

    await expect
      .poll(async () => (await readScopedQueue(page, scope.shopId, deviceId)).find((r) => r.clientSaleId === clientSaleId)?.status, {
        timeout: 30_000,
      })
      .toBe(expectedQueueStatus);
    const afterSyncRow = (await readScopedQueue(page, scope.shopId, deviceId)).find((r) => r.clientSaleId === clientSaleId)!;
    expect(sha256Hex(JSON.stringify(afterSyncRow.payload))).toBe(rawPayloadSha256);

    return {
      httpStatus,
      responseBody,
      wireSale,
      wireSaleSha256: sha256Hex(JSON.stringify(wireSale)),
      queueStatusAfter: afterSyncRow.status,
      reviewCaseId: afterSyncRow.reviewCaseId ?? null,
      errorCode: afterSyncRow.errorCode ?? null,
      serverSaleId: afterSyncRow.serverSaleId ?? null,
      rawPayloadSha256After: sha256Hex(JSON.stringify(afterSyncRow.payload)),
    };
  } finally {
    page.off('response', onResponse);
  }
}

export async function runLiveOrdinaryExpiryScenario(page: Page, context: BrowserContext): Promise<void> {
  const config = readLiveConfig();
  const scope = await signInMerchant(page, config);
  const { deviceId, leaseRow } = await enableOfflineAndVerifyPack(page, config, scope);
  const permit = leaseRow.lease.offlinePermit!;

  await disconnectAndProveOffline(page, context);
  await addGoodsAndQueueOffline(page, config.barcode);

  const [queuedRow] = await readScopedQueue(page, scope.shopId, deviceId);
  expect(queuedRow).toBeTruthy();
  const rawPayloadSha256 = sha256Hex(JSON.stringify(queuedRow.payload));
  const licenceEvidence = queuedRow.payload['licenceEvidence'] as Record<string, unknown>;
  expect(licenceEvidence['signedPermit']).toBe(permit['signedPermit']);

  writePhase(config.evidence, 'queued', {
    shopId: scope.shopId, userId: scope.userId, sessionId: scope.sessionId, deviceId,
    leaseId: leaseRow.lease.leaseId, grantId: permit['grantId'], permitId: permit['permitId'],
    authorityVerifiedAt: permit['authorityVerifiedAt'], notBefore: permit['notBefore'],
    validUntil: permit['validUntil'], limitingReason: permit['limitingReason'],
    clientSaleId: queuedRow.clientSaleId, invoiceNumber: queuedRow.invoiceNumber,
    soldAt: queuedRow.payload['soldAt'], rawPayloadSha256,
    signedPermitSha256: sha256Hex(String(licenceEvidence['signedPermit'])),
  });

  await waitForSignal(config.evidence, 'expired.ready');
  const synced = await reconnectAndCaptureSync(page, context, scope, deviceId, queuedRow.clientSaleId, rawPayloadSha256, 'Synced');
  writePhase(config.evidence, 'synced', synced);
}

export async function runLiveMissedSuspensionScenario(page: Page, context: BrowserContext): Promise<void> {
  const config = readLiveConfig();
  const scope = await signInMerchant(page, config);
  const { deviceId, leaseRow } = await enableOfflineAndVerifyPack(page, config, scope);
  const permit = leaseRow.lease.offlinePermit!;

  await disconnectAndProveOffline(page, context);
  writePhase(config.evidence, 'offline', {
    shopId: scope.shopId, userId: scope.userId, sessionId: scope.sessionId, deviceId,
    leaseId: leaseRow.lease.leaseId, grantId: permit['grantId'], permitId: permit['permitId'],
    authorityVerifiedAt: permit['authorityVerifiedAt'], notBefore: permit['notBefore'],
    validUntil: permit['validUntil'], limitingReason: permit['limitingReason'],
    signedPermitSha256: sha256Hex(String(permit['signedPermit'])),
  });

  const effectiveIso = await waitForSignal(config.evidence, 'suspended.ready');
  const targetMs = Date.parse(effectiveIso) + 1_000;
  const remainingMs = targetMs - Date.now();
  if (remainingMs > 0) await page.waitForTimeout(remainingMs);

  await addGoodsAndQueueOffline(page, config.barcode);
  const [queuedRow] = await readScopedQueue(page, scope.shopId, deviceId);
  expect(queuedRow).toBeTruthy();
  const rawPayloadSha256 = sha256Hex(JSON.stringify(queuedRow.payload));
  const licenceEvidence = queuedRow.payload['licenceEvidence'] as Record<string, unknown>;
  expect(licenceEvidence['signedPermit']).toBe(permit['signedPermit']);

  writePhase(config.evidence, 'queued', {
    shopId: scope.shopId, deviceId, clientSaleId: queuedRow.clientSaleId,
    invoiceNumber: queuedRow.invoiceNumber, soldAt: queuedRow.payload['soldAt'],
    rawPayloadSha256, signedPermitSha256: sha256Hex(String(licenceEvidence['signedPermit'])),
  });

  await waitForSignal(config.evidence, 'reconnect.ready');
  const synced = await reconnectAndCaptureSync(page, context, scope, deviceId, queuedRow.clientSaleId, rawPayloadSha256, 'NeedsReview');
  writePhase(config.evidence, 'synced', synced);
}
