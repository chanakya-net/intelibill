import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import type http from 'node:http';
import { join, resolve } from 'node:path';

import { expect, type Page } from '@playwright/test';

import {
  angularManifestHash,
  OFFLINE_PACK_STATE_CACHE,
  OFFLINE_PACK_STATE_KEY,
} from '../../../src/app/core/service-worker/offline-pack-flag';
import {
  PROTECTED_WORKER_PROTOCOL,
  PROTECTED_WORKER_QUERY,
} from '../../../src/app/core/service-worker/service-worker-registration';
import { startReleaseServer, type ReleaseServer } from './release-server';

export const BUILD = resolve(__dirname, '../../../dist/INVENTORY/browser');
const VECTORS_PATH = resolve(__dirname, '../../../../../tests/contracts/licensing/p05-06-legacy-queue-vectors.json');

export interface LegacyVectorEntry {
  readonly producerCommit: string;
  readonly invoiceNumber: string;
  readonly deviceIdempotencyKey: string;
  readonly queueRecordJson: string;
  readonly payloadJson: string;
  readonly payloadSha256: string;
  readonly expectedSyncRequestJson: string;
  readonly expectedBundleInvoicesJson: string;
  readonly expectedBundleChecksum: string;
  readonly expectedServerIdempotencyKey: string;
  readonly expectedServerPayloadHash: string;
}

export interface BuiltManifest {
  readonly assetGroups: readonly { readonly name: string; readonly installMode: string; readonly urls: readonly string[] }[];
  readonly appData?: { readonly buildId?: string };
  readonly [key: string]: unknown;
}

export const vectors = JSON.parse(readFileSync(VECTORS_PATH, 'utf8')) as {
  readonly scope: { readonly shopId: string; readonly userId: string; readonly deviceId: string; readonly exportedAt: string };
  readonly unversionedPreDocument: LegacyVectorEntry;
  readonly v1FrozenDocument: LegacyVectorEntry;
};

export const { shopId: SHOP_ID, userId: OWNER_ID, deviceId: DEVICE_ID } = vectors.scope;
export const STAFF_ID = '30000000-0000-4000-8000-000000000099';
export const INITIAL_NEXT_NUMBER = 25;
export const OLD_SIGNED_TOKEN =
  'eyJhbGciOiJSUzI1NiIsInR5cCI6ImludGVsaWJpbGwtb2ZmbGluZS1wZXJtaXQrdjEiLCJraWQiOiJwMDUwNi1vbGQta2V5In0.eyJ2IjoxLCJwZXJtaXRJZCI6IjAwMDAwMDAwLTAwMDAtNDAwMC04MDAwLTAwMDAwMDAwMDAwMyJ9.old-frozen-compact-signature';
export const CURRENT_PERMIT_TOKEN =
  'eyJhbGciOiJSUzI1NiIsInR5cCI6ImludGVsaWJpbGwtb2ZmbGluZS1wZXJtaXQrdjEiLCJraWQiOiJwMDUwNi1jdXJyZW50LWtleSJ9.eyJ2IjoxLCJwZXJtaXRJZCI6IjAwMDAwMDAwLTAwMDAtNDAwMC04MDAwLTAwMDAwMDAwMDA5OSJ9.different-current-permit-signature';

export const sha256Hex = (text: string): string => createHash('sha256').update(text, 'utf8').digest('hex');
export const readBuiltFullManifest = (): BuiltManifest => JSON.parse(readFileSync(join(BUILD, 'ngsw.json'), 'utf8')) as BuiltManifest;

export interface LegacyRecoveryServerHandle {
  readonly server: ReleaseServer;
  readonly syncBodies: string[];
  readonly recoveryBodies: string[];
  leaseReservationCount(): number;
}

export function buildSession(role: 'Owner' | 'Staff', userId: string, expiredAccess = false) {
  const now = Date.now();
  const accessExpiry = expiredAccess ? now - 10 * 60 * 1000 : now + 60 * 60 * 1000;
  const jwt = `e30.${Buffer.from(JSON.stringify({ sub: userId, active_shop_id: SHOP_ID, exp: Math.floor(accessExpiry / 1000) })).toString('base64url')}.fixture`;
  return {
    accessToken: jwt, refreshToken: 'fixture-refresh-token',
    accessTokenExpiresAt: new Date(accessExpiry).toISOString(),
    refreshTokenExpiresAt: new Date(now + 24 * 60 * 60 * 1000).toISOString(),
    rememberMe: true,
    user: { id: userId, email: `${role.toLowerCase()}@legacy-demo.test`, phoneNumber: null, firstName: role, lastName: 'Demo' },
    activeShopId: SHOP_ID,
    shops: [{ shopId: SHOP_ID, shopName: 'Live Changed Shop Name', role, isDefault: true, lastUsedAt: null }],
    sessionId: 'fixture-session-p0506',
  };
}

function readBody(req: http.IncomingMessage): Promise<string> {
  return new Promise((resolveBody, rejectBody) => {
    const chunks: Buffer[] = [];
    req.on('data', (chunk: Buffer) => chunks.push(chunk));
    req.on('end', () => resolveBody(Buffer.concat(chunks).toString('utf8')));
    req.on('error', rejectBody);
  });
}

function sendJson(res: http.ServerResponse, status: number, body: unknown): boolean {
  res.writeHead(status, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' });
  res.end(JSON.stringify(body));
  return true;
}

async function handleSyncAndRecoveryApi(
  req: http.IncomingMessage,
  res: http.ServerResponse,
  path: string,
  syncBodies: string[],
  recoveryBodies: string[],
  onReserve: () => void,
): Promise<boolean> {
  if (path === '/api/sales/invoice-leases/reserve' && req.method === 'POST') {
    onReserve();
    return sendJson(res, 403, { code: 'Licence.Expired', detail: 'No new reservation allowed during legacy recovery.' });
  }
  if (path === '/api/sales/offline-sync' && req.method === 'POST') {
    const raw = await readBody(req);
    syncBodies.push(raw);
    const parsed = JSON.parse(raw) as { sales: { clientSaleId: string; invoiceNumber: string }[] };
    return sendJson(res, 200, {
      results: parsed.sales.map((sale, i) => ({
        clientSaleId: sale.clientSaleId, status: 'NeedsReview', saleId: null, invoiceNumber: sale.invoiceNumber,
        warnings: [], errors: [{ code: 'OfflineLicenceHistory.EvidenceMissing', message: 'Historical evidence required for review.' }],
        reviewCaseId: `10000000-0000-4000-8000-00000000010${i + 1}`,
      })),
    });
  }
  if (path === '/api/offline-reconciliation/recovery-submissions' && req.method === 'POST') {
    const raw = await readBody(req);
    recoveryBodies.push(raw);
    const parsed = JSON.parse(raw) as { operationId: string; bundle: { invoices: { clientSaleId: string; invoiceNumber: string; issuedByUserId?: string }[] } };
    return sendJson(res, 200, {
      operationId: parsed.operationId,
      results: parsed.bundle.invoices.map((inv, i) => ({
        clientSaleId: inv.clientSaleId, invoiceNumber: inv.invoiceNumber,
        status: inv.issuedByUserId ? 'ReviewOpened' : 'Escalated',
        caseId: `20000000-0000-4000-8000-00000000020${i + 1}`, saleId: null,
        errors: [{ code: inv.issuedByUserId ? 'OfflineReconciliation.RecoveryReviewRequired' : 'OfflineReconciliation.ProvenanceUnknown', message: 'Reviewed.' }],
      })),
    });
  }
  return false;
}

function handleShopAndSnapshotApi(req: http.IncomingMessage, res: http.ServerResponse, path: string): boolean {
  if (path === '/api/offline-reconciliation' && req.method === 'GET')
    return sendJson(res, 200, { items: [], page: 1, pageSize: 20, totalCount: 0, unresolvedCount: 0 });
  if (path === '/api/shops/me')
    return sendJson(res, 200, [{ shopId: SHOP_ID, shopName: 'Live Changed Shop Name', role: 'Owner', isDefault: true, lastUsedAt: null }]);
  if (path === `/api/shops/${SHOP_ID}`)
    return sendJson(res, 200, { shopId: SHOP_ID, name: 'Live Changed Shop Name', address: '99 New Street, Mumbai', city: 'Mumbai', state: 'Maharashtra', pincode: '400001', gstNumber: '27NEWCHANGED1Z5', businessTimeZone: 'Asia/Kolkata' });
  if (/^\/api\/licensing\/shops\/[^/]+\/trading-authority$/.test(path))
    return sendJson(res, 403, { code: 'Licence.Expired', enforced: true });
  if (path.startsWith('/api/expiry-policies')) {
    const policy = { version: 1, mode: 'Ignore', expiryRequired: false, sellThroughExpiryDate: true, warningDays: 7, overrideAllowed: false };
    return sendJson(res, 200, { defaultPolicy: policy, overridePolicy: null, effectivePolicy: policy, catalogVersion: 1 });
  }
  if (path === '/api/sales/offline-snapshot/stream') {
    const now = new Date().toISOString();
    const lines = [
      { type: 'metadata', metadata: { snapshotId: 'snap-legacy-p0506', shopId: SHOP_ID, schemaVersion: 4, businessTimeZoneId: 'Asia/Kolkata', catalogVersion: 1, startedAt: now } },
      { type: 'complete', complete: { snapshotId: 'snap-legacy-p0506', completedAt: now, batchCount: 0, customerCount: 0, discountRuleCount: 0, activeLeaseCount: 0, serviceCount: 0 } },
    ];
    res.writeHead(200, { 'Content-Type': 'application/x-ndjson', 'Cache-Control': 'no-store' });
    res.end(`${lines.map((line) => JSON.stringify(line)).join('\n')}\n`);
    return true;
  }
  return path.startsWith('/api/') ? sendJson(res, 200, []) : false;
}

export async function startLegacyRecoveryServer(): Promise<LegacyRecoveryServerHandle> {
  const syncBodies: string[] = [];
  const recoveryBodies: string[] = [];
  let leaseReservations = 0;

  const server = await startReleaseServer(BUILD, async (req, res, url) => {
    if (await handleSyncAndRecoveryApi(req, res, url.pathname, syncBodies, recoveryBodies, () => { leaseReservations += 1; })) return true;
    return handleShopAndSnapshotApi(req, res, url.pathname);
  });
  return { server, syncBodies, recoveryBodies, leaseReservationCount: () => leaseReservations };
}

export async function controlledByProtectedWorker(page: Page): Promise<boolean> {
  return page.evaluate(
    ({ query, protocol }) => new Promise<boolean>((resolveQuery) => {
      const controller = navigator.serviceWorker.controller;
      if (!controller) { resolveQuery(false); return; }
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

export async function missingFromCurrentPack(page: Page, manifest: BuiltManifest): Promise<string[]> {
  const hash = await angularManifestHash(manifest);
  const groups = manifest.assetGroups.filter((g) => g.installMode === 'prefetch').map((g) => ({ name: g.name, urls: g.urls }));
  return page.evaluate(async ({ version, wanted }) => {
    const missing: string[] = [];
    for (const group of wanted) {
      const name = `ngsw:/:${version}:assets:${group.name}:cache`;
      const cached = (await caches.has(name))
        ? new Set((await (await caches.open(name)).keys()).map((r) => new URL(r.url).pathname))
        : new Set<string>();
      missing.push(...group.urls.filter((url) => !cached.has(url)));
    }
    return missing;
  }, { version: hash, wanted: groups });
}

export async function readPackFlag(page: Page): Promise<string | null> {
  return page.evaluate(
    async ({ cacheName, key }) => (await caches.match(new URL(key, document.baseURI).href, { cacheName }))?.text() ?? null,
    { cacheName: OFFLINE_PACK_STATE_CACHE, key: OFFLINE_PACK_STATE_KEY },
  );
}

export async function readWorkerState(page: Page): Promise<string> {
  return page.evaluate(async () => (await fetch('/ngsw/state')).text());
}

export async function readQueueRowsRaw(page: Page): Promise<Record<string, unknown>[]> {
  return page.evaluate(() => new Promise<Record<string, unknown>[]>((resolveRows, rejectRows) => {
    const open = indexedDB.open('intelibill-offline-sales-queue', 1);
    open.onerror = () => rejectRows(open.error);
    open.onsuccess = () => {
      const db = open.result;
      const req = db.transaction('queued-sales', 'readonly').objectStore('queued-sales').getAll();
      req.onerror = () => rejectRows(req.error);
      req.onsuccess = () => { const rows = (req.result as Record<string, unknown>[]) ?? []; db.close(); resolveRows(rows); };
    };
  }));
}

export async function readLeaseNextNumber(page: Page): Promise<number | null> {
  return page.evaluate(({ shopId, deviceId }) => new Promise<number | null>((resolveLease, rejectLease) => {
    const open = indexedDB.open('intelibill-invoice-leases', 1);
    open.onerror = () => rejectLease(open.error);
    open.onsuccess = () => {
      const db = open.result;
      const req = db.transaction('invoice-leases', 'readonly').objectStore('invoice-leases').get(`${shopId}::${deviceId}::2026-27`);
      req.onerror = () => rejectLease(req.error);
      req.onsuccess = () => { const row = req.result as { lease?: { nextNumber?: number } } | undefined; db.close(); resolveLease(row?.lease?.nextNumber ?? null); };
    };
  }), { shopId: SHOP_ID, deviceId: DEVICE_ID });
}

export function buildSignedPreUpgradeRow(): Record<string, unknown> {
  const base = JSON.parse(vectors.v1FrozenDocument.queueRecordJson) as Record<string, unknown>;
  const basePayload = base['payload'] as Record<string, unknown>;
  const signedId = 'legacy-signed-0003';
  const deviceKey = `offline-sale-${signedId}`;
  return {
    ...base,
    key: `${SHOP_ID}::${DEVICE_ID}::${signedId}`,
    clientSaleId: signedId, idempotencyKey: deviceKey, invoiceNumber: 'A/2627/000003', soldAt: '2027-01-20T11:55:00.000Z',
    payload: {
      ...basePayload,
      clientSaleId: signedId, idempotencyKey: deviceKey, invoiceNumber: 'A/2627/000003', soldAt: '2027-01-20T11:55:00.000Z',
      licenceEvidence: {
        version: 1, permitId: '00000000-0000-4000-8000-000000000003', grantId: '00000000-0000-4000-8000-000000000013',
        grantSha256: 'a'.repeat(64), leaseId: '11111111-1111-4111-8111-111111111111',
        snapshotId: '22222222-2222-4222-8222-222222222222', keyId: 'p0506-old-key', signedPermit: OLD_SIGNED_TOKEN,
      },
    },
  };
}

export function assertPreservedPayloadBytes(rows: Record<string, unknown>[], signedExpectedJson: string): void {
  const byId = new Map(rows.map((r) => [r['clientSaleId'] as string, r]));
  expect(rows).toHaveLength(3);
  const raw7ff1 = JSON.stringify(byId.get('legacy-7ff1-0001')!['payload']);
  const raw671d = JSON.stringify(byId.get('legacy-671d-0002')!['payload']);
  const rawSigned = JSON.stringify(byId.get('legacy-signed-0003')!['payload']);
  expect(raw7ff1).toBe(vectors.unversionedPreDocument.payloadJson);
  expect(sha256Hex(raw7ff1)).toBe(vectors.unversionedPreDocument.payloadSha256);
  expect(byId.get('legacy-7ff1-0001')!['idempotencyKey']).toBe(vectors.unversionedPreDocument.deviceIdempotencyKey);
  expect(byId.get('legacy-7ff1-0001')!['invoiceNumber']).toBe(vectors.unversionedPreDocument.invoiceNumber);
  expect(raw671d).toBe(vectors.v1FrozenDocument.payloadJson);
  expect(sha256Hex(raw671d)).toBe(vectors.v1FrozenDocument.payloadSha256);
  expect(byId.get('legacy-671d-0002')!['idempotencyKey']).toBe(vectors.v1FrozenDocument.deviceIdempotencyKey);
  expect(rawSigned).toBe(signedExpectedJson);
  expect((byId.get('legacy-signed-0003')!['payload'] as { licenceEvidence: { signedPermit: string } }).licenceEvidence.signedPermit).toBe(OLD_SIGNED_TOKEN);
}

export async function seedLegacyBrowserState(page: Page, signedRow: Record<string, unknown>): Promise<void> {
  const raw7ff1 = JSON.parse(vectors.unversionedPreDocument.queueRecordJson) as Record<string, unknown>;
  const raw671d = JSON.parse(vectors.v1FrozenDocument.queueRecordJson) as Record<string, unknown>;
  await seedLegacyLocalStorage(page, buildSession('Owner', OWNER_ID));
  await seedLegacySnapshotIdb(page);
  await seedLegacyQueueIdb(page, [raw7ff1, raw671d, signedRow]);
  await seedLegacyLeaseIdb(page);
}

async function seedLegacyLocalStorage(page: Page, authValue: ReturnType<typeof buildSession>): Promise<void> {
  await page.evaluate(({ auth, shopId, deviceId }) => {
    const nowIso = new Date().toISOString();
    localStorage.setItem('inventory.auth.session.local', JSON.stringify(auth));
    localStorage.setItem('inventory.preferences.language', 'en-IN');
    localStorage.setItem(`intelibill.offlineSales.deviceId.v1:${shopId}`, deviceId);
    localStorage.setItem(`intelibill.offlineSales.deviceSettings.v1:${shopId}:${deviceId}`, JSON.stringify({
      shopId, deviceId, label: 'Legacy Counter', enabled: true, enabledAt: nowIso,
      enabledByUserId: auth.user.id, enabledByUserName: 'Owner Demo',
      lastCompleteSnapshotAt: nowIso, lastApiVerifiedAt: nowIso, lastSnapshotWarningMarker: null, lastReservedLease: null,
    }));
  }, { auth: authValue, shopId: SHOP_ID, deviceId: DEVICE_ID });
}

async function seedLegacySnapshotIdb(page: Page): Promise<void> {
  await page.evaluate((shopId) => new Promise<void>((res, rej) => {
    const nowIso = new Date().toISOString();
    const open = indexedDB.open('intelibill-offline-sales-snapshot', 2);
    open.onupgradeneeded = () => {
      for (const [name, keyPath] of [
        ['snapshot-attempts', 'snapshotId'], ['shop-pointers', 'shopId'], ['batches', 'key'],
        ['customers', 'key'], ['discount-rules', 'key'], ['active-leases', 'key'], ['services', 'key'],
      ] as const) {
        if (!open.result.objectStoreNames.contains(name)) open.result.createObjectStore(name, { keyPath });
      }
    };
    open.onerror = () => rej(open.error);
    open.onsuccess = () => {
      const db = open.result;
      const tx = db.transaction(['snapshot-attempts', 'shop-pointers'], 'readwrite');
      tx.objectStore('snapshot-attempts').put({ snapshotId: 'snap-legacy-v3', shopId, schemaVersion: 3, businessTimeZoneId: 'Asia/Kolkata', catalogVersion: 1, startedAt: nowIso, completedAt: nowIso, status: 'complete' });
      tx.objectStore('shop-pointers').put({ shopId, usableSnapshotId: 'snap-legacy-v3', usableCompletedAt: nowIso, stale: false, lastAttemptSnapshotId: 'snap-legacy-v3', lastAttemptStartedAt: nowIso, lastAttemptStatus: 'complete' });
      tx.oncomplete = () => { db.close(); res(); };
      tx.onerror = () => rej(tx.error);
    };
  }), SHOP_ID);
}

async function seedLegacyQueueIdb(page: Page, rows: Record<string, unknown>[]): Promise<void> {
  await page.evaluate((items) => new Promise<void>((res, rej) => {
    const open = indexedDB.open('intelibill-offline-sales-queue', 1);
    open.onupgradeneeded = () => {
      if (!open.result.objectStoreNames.contains('queued-sales')) open.result.createObjectStore('queued-sales', { keyPath: 'key' });
    };
    open.onerror = () => rej(open.error);
    open.onsuccess = () => {
      const db = open.result;
      const tx = db.transaction('queued-sales', 'readwrite');
      for (const row of items) tx.objectStore('queued-sales').put(row);
      tx.oncomplete = () => { db.close(); res(); };
      tx.onerror = () => rej(tx.error);
    };
  }), rows);
}

async function seedLegacyLeaseIdb(page: Page): Promise<void> {
  await page.evaluate(({ shopId, deviceId, nextNumber, currentPermitToken }) => new Promise<void>((res, rej) => {
    const open = indexedDB.open('intelibill-invoice-leases', 1);
    open.onupgradeneeded = () => {
      if (!open.result.objectStoreNames.contains('invoice-leases')) open.result.createObjectStore('invoice-leases', { keyPath: 'key' });
    };
    open.onerror = () => rej(open.error);
    open.onsuccess = () => {
      const db = open.result;
      const tx = db.transaction('invoice-leases', 'readwrite');
      tx.objectStore('invoice-leases').put({
        key: `${shopId}::${deviceId}::2026-27`, shopId, deviceId, fiscalYear: '2026-27',
        lease: {
          leaseId: '11111111-1111-4111-8111-111111111111', shopId, deviceId, fiscalYear: '2026-27',
          prefix: 'A/2627/', numberPadding: 6, rangeStart: 1, rangeEnd: 100, nextNumber,
          remainingCount: 101 - nextNumber, reservedAt: '2027-01-20T11:00:00.000Z', expiresAt: '2027-01-27T00:00:00.000Z',
          offlinePermit: {
            version: 1, permitId: '00000000-0000-4000-8000-000000000099', grantId: '00000000-0000-4000-8000-000000000098',
            grantSha256: 'b'.repeat(64), leaseId: '11111111-1111-4111-8111-111111111111', snapshotId: '99999999-9999-4999-8999-999999999999',
            keyId: 'p0506-current-key', signedPermit: currentPermitToken, notBefore: '2027-01-20T11:58:00.000Z',
            validUntil: '2027-01-22T11:58:00.000Z', limitingReason: 'AuthorityFreshness',
          },
        },
        updatedAt: '2027-01-20T11:58:00.000Z',
      });
      tx.oncomplete = () => { db.close(); res(); };
      tx.onerror = () => rej(tx.error);
    };
  }), { shopId: SHOP_ID, deviceId: DEVICE_ID, nextNumber: INITIAL_NEXT_NUMBER, currentPermitToken: CURRENT_PERMIT_TOKEN });
}
