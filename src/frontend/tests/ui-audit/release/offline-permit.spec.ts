import { existsSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

import { expect, test, type Page } from '@playwright/test';

import { OFFLINE_PACK_STATE_CACHE, OFFLINE_PACK_STATE_KEY } from '../../../src/app/core/service-worker/offline-pack-flag';
import { accessibilityFixtures } from './accessibility-localization-fixtures';
import { SHOP_A, USER, session, startPurchaseReceivingServer } from './purchase-receiving-server';

/**
 * Browser release proof for P05-02 against the built app, real protected service worker, offline
 * pack, and real IndexedDB (`intelibill-invoice-leases`).
 *
 * Evidence boundary: API answers and compact permit tokens below are local fixtures matching
 * `tests/contracts/licensing/offline-permit-delivery-v1.json`; they exercise client decoding,
 * binding checks, IndexedDB persistence, stale-snapshot preservation, and offline reload display,
 * not server cryptographic signing (proved on real PostgreSQL by `P0502LicensingTests`). No live
 * licensing provider or Azure Key Vault is contacted.
 */

const BUILD = resolve(__dirname, '../../../dist/INVENTORY/browser');
const DELIVERY_FIXTURE_PATH = resolve(__dirname, '../../../../../tests/contracts/licensing/offline-permit-delivery-v1.json');
const DEVICE_ID = 'device-p0502-browser';
const LEASE_ID = '11111111-1111-4111-8111-111111111111';
const GRANT_ID = '22222222-2222-4222-8222-222222222222';
const PERMIT_ID = '33333333-3333-4333-8333-333333333333';
const ACCOUNT_ID = '44444444-4444-4444-8444-444444444444';
const GRANT_SHA256 = '6eceaedcda11fb4caa3d3bc4a9894ed5ebe33141ea01c3d2beee62c58094ba81';
const KEY_ID = 'fixture-permit-key-1';
const PACK_READY_MS = 120_000;
const CYCLE_STATE_KEY = 'intelibill.offlineSales.offlinePackCycle.v1';

const deliveryFixture = JSON.parse(readFileSync(DELIVERY_FIXTURE_PATH, 'utf8')) as {
  readonly leaseEvidence: { readonly version: number };
  readonly issuedRecord: { readonly type: string; readonly licencePermit: { readonly version: number; readonly status: string } };
  readonly refusedRecord: { readonly type: string; readonly licencePermit: { readonly version: number; readonly status: string } };
};

type FixtureServer = Awaited<ReturnType<typeof startPurchaseReceivingServer>>;
type BoundStreamMode = 'issued' | 'absent' | 'refused' | 'wrong-lease';
interface StreamRequestLog { readonly leaseId: string | null; readonly grantId: string | null; readonly deviceId: string | null }
interface BoundStreamState { mode: BoundStreamMode; completedAt: string; snapshotId: string }

let server: FixtureServer | undefined;
let pageErrors: string[];

test.describe.configure({ timeout: 240_000 });

test.beforeAll(() => {
  for (const file of ['ngsw-worker.js', 'ngsw-angular-worker.js', 'ngsw.json', 'ngsw-lite.json']) {
    if (!existsSync(join(BUILD, file))) throw new Error(`No ${file} in ${BUILD}. Run \`bun run build\` in src/frontend first.`);
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

const b64u = (value: string) => Buffer.from(value, 'utf8').toString('base64url');

function buildFixturePermitToken(
  leaseId: string, snapshotId: string, snapshotCompletedAt: string, validUntil: string, sessionId: string,
): string {
  const header = `{"alg":"RS256","typ":"intelibill-offline-permit+v1","kid":"${KEY_ID}"}`;
  const payload = JSON.stringify({
    v: 1, permitId: PERMIT_ID, grantId: GRANT_ID, grantSha256: GRANT_SHA256, shopId: SHOP_A, accountId: ACCOUNT_ID,
    userId: USER, sessionId, deviceId: DEVICE_ID, leaseId, snapshotId, accountRevision: 1, shopRevision: 1,
    currentAccountRevision: 1, currentShopRevision: 1, authorityVerifiedAt: '2026-11-20T12:00:00.000000Z',
    deviceVerifiedAt: '2026-11-22T11:59:00.000000Z', snapshotCompletedAt, issuedAt: snapshotCompletedAt,
    notBefore: snapshotCompletedAt, validUntil, limitingReason: 'AuthorityFreshness', tillSessionId: null,
  });
  return `${b64u(header)}.${b64u(payload)}.fixture-signature`;
}

function buildBoundPermitLine(state: BoundStreamState, sessionId: string): unknown | null {
  if (state.mode === 'absent') return null;
  if (state.mode === 'refused') {
    return {
      type: deliveryFixture.refusedRecord.type,
      licencePermit: {
        version: deliveryFixture.refusedRecord.licencePermit.version, status: deliveryFixture.refusedRecord.licencePermit.status,
        leaseId: LEASE_ID, grantId: GRANT_ID, snapshotId: state.snapshotId, refusalCode: 'OfflinePermit.DeadlineReached',
      },
    };
  }
  const targetLease = state.mode === 'wrong-lease' ? '99999999-9999-4999-8999-999999999999' : LEASE_ID;
  const validUntil = state.mode === 'wrong-lease' ? '2026-11-25T12:00:00.000000Z' : '2026-11-22T12:00:00.000000Z';
  const permitId = state.mode === 'wrong-lease' ? '88888888-8888-4888-8888-888888888888' : PERMIT_ID;
  return {
    type: deliveryFixture.issuedRecord.type,
    licencePermit: {
      version: deliveryFixture.issuedRecord.licencePermit.version, status: deliveryFixture.issuedRecord.licencePermit.status,
      leaseId: targetLease, grantId: GRANT_ID, snapshotId: state.snapshotId, permitId, keyId: KEY_ID,
      signedPermit: buildFixturePermitToken(targetLease, state.snapshotId, state.completedAt, validUntil, sessionId),
      notBefore: state.completedAt, validUntil, limitingReason: 'AuthorityFreshness',
    },
  };
}

async function setupRoutes(
  page: Page, anchoredLease: boolean, getBoundState: () => BoundStreamState,
): Promise<StreamRequestLog[]> {
  const authSession = session();
  const streamRequests: StreamRequestLog[] = [];

  await page.context().addInitScript(
    ({ authValue, shopId, deviceId }) => {
      localStorage.setItem('inventory.auth.session.local', JSON.stringify(authValue));
      localStorage.setItem('inventory.preferences.language', 'en-IN');
      localStorage.setItem(`intelibill.offlineSales.deviceId.v1:${shopId}`, deviceId);
    },
    { authValue: authSession, shopId: SHOP_A, deviceId: DEVICE_ID },
  );

  await accessibilityFixtures(page);
  await page.route('**/api/expiry-policies/readiness**', (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        catalogVersion: 1,
        results: { missingDateLots: 0, expiredLots: 0, quarantineLots: 0, items: [], totalCount: 0, page: 1, pageSize: 50 },
      }),
    }),
  );

  await page.context().route('**/api/sales/offline-snapshot/stream**', (route) => {
    const url = new URL(route.request().url());
    const leaseId = url.searchParams.get('leaseId');
    const grantId = url.searchParams.get('grantId');
    const deviceId = url.searchParams.get('deviceId');
    streamRequests.push({ leaseId, grantId, deviceId });

    const isBound = !!(leaseId && grantId && deviceId);
    const state: BoundStreamState = isBound
      ? getBoundState()
      : { mode: 'absent', completedAt: '2026-11-22T11:59:00.000000Z', snapshotId: 'snap-pre-lease' };

    const lines: unknown[] = [
      {
        type: 'metadata',
        metadata: {
          snapshotId: state.snapshotId, shopId: SHOP_A, schemaVersion: 4,
          businessTimeZoneId: 'Asia/Kolkata', catalogVersion: 1, startedAt: state.completedAt,
        },
      },
    ];
    const permitLine = isBound ? buildBoundPermitLine(state, authSession.sessionId) : null;
    if (permitLine) lines.push(permitLine);
    lines.push({
      type: 'complete',
      complete: {
        snapshotId: state.snapshotId, completedAt: state.completedAt,
        batchCount: 0, customerCount: 0, discountRuleCount: 0, activeLeaseCount: 0, serviceCount: 0,
      },
    });
    return route.fulfill({ contentType: 'application/x-ndjson', body: `${lines.map((line) => JSON.stringify(line)).join('\n')}\n` });
  });

  await page.context().route('**/api/sales/invoice-leases/reserve**', (route) => {
    const { deviceId } = route.request().postDataJSON() as { deviceId: string };
    return route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        leaseId: LEASE_ID, shopId: SHOP_A, deviceId, fiscalYear: '2026-27', prefix: 'A/2627/', numberPadding: 6,
        rangeStart: 1, rangeEnd: 100, nextNumber: 1, remainingCount: 100,
        reservedAt: '2026-11-22T11:59:00.000000Z', expiresAt: '2099-01-01T00:00:00.000000Z',
        ...(anchoredLease
          ? { licenceEvidence: { version: deliveryFixture.leaseEvidence.version, grantId: GRANT_ID, grantSha256: GRANT_SHA256, keyId: KEY_ID } }
          : {}),
      }),
    });
  });

  return streamRequests;
}

async function readStoredLeaseFromIndexedDb(page: Page): Promise<Record<string, unknown> | null> {
  return page.evaluate(
    ({ shopId, deviceId }) =>
      new Promise<Record<string, unknown> | null>((resolveIdb, rejectIdb) => {
        const open = indexedDB.open('intelibill-invoice-leases', 1);
        open.onerror = () => rejectIdb(open.error);
        open.onsuccess = () => {
          const db = open.result;
          const req = db.transaction('invoice-leases', 'readonly').objectStore('invoice-leases').get(`${shopId}::${deviceId}::2026-27`);
          req.onerror = () => rejectIdb(req.error);
          req.onsuccess = () => {
            const record = req.result as { lease?: Record<string, unknown> } | undefined;
            db.close();
            resolveIdb(record?.lease ?? null);
          };
        };
      }),
    { shopId: SHOP_A, deviceId: DEVICE_ID },
  );
}

async function readDeviceSettings(page: Page): Promise<Record<string, unknown> | null> {
  return page.evaluate(
    ({ shopId, deviceId }) =>
      JSON.parse(localStorage.getItem(`intelibill.offlineSales.deviceSettings.v1:${shopId}:${deviceId}`) ?? 'null'),
    { shopId: SHOP_A, deviceId: DEVICE_ID },
  );
}

async function openManageShopOfflineStep(page: Page): Promise<void> {
  await page.locator('app-sidebar-profile-menu').getByRole('button').first().click();
  await page.getByText('Manage Shop', { exact: true }).filter({ visible: true }).click();
  const dialog = page.locator('section.manage-shop-overlay');
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: 'Offline billing for this device' }).click();
  await expect(dialog.locator('.offline-card')).toBeVisible();
}

async function closeManageShop(page: Page): Promise<void> {
  await page.locator('section.manage-shop-overlay .overlay-close').click();
  await expect(page.locator('section.manage-shop-overlay')).toHaveCount(0);
}

async function triggerStaleSnapshotRefresh(page: Page): Promise<void> {
  await page.evaluate(
    ({ shopId, deviceId, cycleKey }) => {
      const settingsKey = `intelibill.offlineSales.deviceSettings.v1:${shopId}:${deviceId}`;
      const current = JSON.parse(localStorage.getItem(settingsKey) ?? 'null');
      if (current) {
        current.lastCompleteSnapshotAt = '2020-01-01T00:00:00.000Z';
        localStorage.setItem(settingsKey, JSON.stringify(current));
      }
      localStorage.removeItem(cycleKey);
      window.dispatchEvent(new Event('online'));
    },
    { shopId: SHOP_A, deviceId: DEVICE_ID, cycleKey: CYCLE_STATE_KEY },
  );
}

test('enabling stores bound permit in IndexedDB, shows exact IST deadline, retains it across stale snapshots, and survives offline reload', async ({
  page,
  context,
}) => {
  let boundState: BoundStreamState = { mode: 'issued', completedAt: '2026-11-22T11:59:30.123456Z', snapshotId: 'snap-bound-1' };
  const streamRequests = await setupRoutes(page, true, () => boundState);

  await page.goto(`${server!.origin}/sales/new`);
  await page.locator('.shell-main').waitFor({ timeout: 30_000 });

  const toggle = page.locator('.offline-sales-toggle');
  await expect(toggle).toHaveAttribute('aria-pressed', 'false');
  await toggle.click();
  await expect(toggle).toHaveAttribute('aria-pressed', 'true', { timeout: PACK_READY_MS });

  const packStatus = await page.evaluate(() => JSON.parse(localStorage.getItem('intelibill.offlineSales.offlinePack.v1') ?? 'null'));
  expect(packStatus).toMatchObject({ state: 'ready' });
  const flag = await page.evaluate(
    async ({ cacheName, key }) => (await caches.match(new URL(key, document.baseURI).href, { cacheName }))?.text() ?? null,
    { cacheName: OFFLINE_PACK_STATE_CACHE, key: OFFLINE_PACK_STATE_KEY },
  );
  expect(flag).toBe('on');
  expect(streamRequests).toEqual([
    { leaseId: null, grantId: null, deviceId: null },
    { leaseId: LEASE_ID, grantId: GRANT_ID, deviceId: DEVICE_ID },
  ]);

  const storedLease = await readStoredLeaseFromIndexedDb(page);
  expect(storedLease).toMatchObject({
    leaseId: LEASE_ID,
    licenceEvidence: { version: 1, grantId: GRANT_ID, grantSha256: GRANT_SHA256, keyId: KEY_ID },
    offlinePermit: {
      version: 1, permitId: PERMIT_ID, grantId: GRANT_ID, grantSha256: GRANT_SHA256,
      leaseId: LEASE_ID, snapshotId: 'snap-bound-1', validUntil: '2026-11-22T12:00:00.000000Z',
    },
  });

  await openManageShopOfflineStep(page);
  const permitRow = page.locator('section.manage-shop-overlay .offline-permit-row');
  await expect(permitRow).toBeVisible();
  await expect(permitRow).toContainText('Reconnect by');
  await expect(permitRow).toContainText('22 Nov 2026, 5:30:00 pm IST');
  await expect(permitRow).not.toContainText('AuthorityFreshness');
  await closeManageShop(page);

  for (const [index, mode] of (['absent', 'refused', 'wrong-lease'] as const).entries()) {
    const completedAt = `2026-11-22T11:59:4${index}.000000Z`;
    boundState = { mode, completedAt, snapshotId: `snap-later-${mode}` };
    const beforeCount = streamRequests.length;
    await triggerStaleSnapshotRefresh(page);
    await expect.poll(() => streamRequests.length, { timeout: 30_000 }).toBe(beforeCount + 1);
    await expect.poll(async () => (await readDeviceSettings(page))?.['lastCompleteSnapshotAt'], { timeout: 30_000 }).toBe(completedAt);

    const leaseAfter = await readStoredLeaseFromIndexedDb(page);
    expect(leaseAfter?.['offlinePermit']).toMatchObject({
      permitId: PERMIT_ID, snapshotId: 'snap-bound-1', validUntil: '2026-11-22T12:00:00.000000Z',
    });
  }

  await context.unrouteAll({ behavior: 'ignoreErrors' });
  await context.setOffline(true);
  const staticBefore = server!.assets.requests.length;
  await page.reload();
  await page.locator('.shell-main').waitFor({ timeout: 30_000 });
  expect(server!.assets.requests.length).toBe(staticBefore);

  await openManageShopOfflineStep(page);
  await expect(page.locator('section.manage-shop-overlay .offline-permit-row')).toContainText('22 Nov 2026, 5:30:00 pm IST');
  expect(pageErrors.filter((message) => !/fetch|network|Failed to load/i.test(message))).toEqual([]);
});

test('legacy lease without licenceEvidence performs one snapshot request and renders no permit row', async ({ page }) => {
  const streamRequests = await setupRoutes(page, false, () => ({
    mode: 'absent', completedAt: '2026-11-22T11:59:00.000000Z', snapshotId: 'snap-legacy',
  }));

  await page.goto(`${server!.origin}/sales/new`);
  await page.locator('.shell-main').waitFor({ timeout: 30_000 });

  const toggle = page.locator('.offline-sales-toggle');
  await toggle.click();
  await expect(toggle).toHaveAttribute('aria-pressed', 'true', { timeout: PACK_READY_MS });

  expect(streamRequests).toEqual([{ leaseId: null, grantId: null, deviceId: null }]);
  expect((await readStoredLeaseFromIndexedDb(page))?.['offlinePermit']).toBeNull();

  await openManageShopOfflineStep(page);
  await expect(page.locator('section.manage-shop-overlay .offline-permit-row')).toHaveCount(0);
  expect(pageErrors).toEqual([]);
});
