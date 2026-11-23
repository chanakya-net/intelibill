import { spawnSync } from 'node:child_process';
import { createSign, generateKeyPairSync, randomUUID, type KeyObject } from 'node:crypto';
import { rmSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

import type { Page } from '@playwright/test';

import { accessibilityFixtures } from './accessibility-localization-fixtures';
import { SHOP_A, USER, session } from './purchase-receiving-server';

/**
 * Fixture for the P05-03 browser proof.
 *
 * Evidence boundary: the API answers are local fixtures and the signing key is a throwaway RSA-2048 key made in this
 * test process. Its private half never leaves this process; only the public half (base64 DER SPKI) is given to the
 * merchant build, through the same `--define` a release uses. What runs for real is the production build, the
 * protected service worker, the offline pack, WebCrypto RSA verification and IndexedDB. Server-side signing and
 * deadlines are proved on real PostgreSQL by `P0501LicensingTests` / `P0502LicensingTests`. No live licensing
 * provider, payment provider or key vault is contacted.
 */
const FRONTEND = resolve(__dirname, '../../..');
export const PINNED_BUILD_ROOT = join(FRONTEND, '.ui-audit', 'offline-permit-build');
export const PINNED_BUILD = join(PINNED_BUILD_ROOT, 'browser');

export const DEVICE_ID = 'device-p0503-browser';
export const SESSION_ID = '50000000-0000-4000-8000-000000000001';
export const LEASE_ID = '11111111-1111-4111-8111-111111111111';
export const GRANT_ID = '22222222-2222-4222-8222-222222222222';
export const ACCOUNT_ID = '44444444-4444-4444-8444-444444444444';
export const GRANT_SHA256 = '6eceaedcda11fb4caa3d3bc4a9894ed5ebe33141ea01c3d2beee62c58094ba81';
export const BATCH_ID = '60000000-0000-4000-8000-000000000001';
export const ITEM_NAME = 'Permit Proof Rice 1kg';
export const BARCODE = 'P0503';
export const FISCAL_YEAR = '2026-27';
export const LEASE_KEY = `${SHOP_A}::${DEVICE_ID}::${FISCAL_YEAR}`;
export const CLOCK_KEY = `clock::${SHOP_A}::${DEVICE_ID}::${USER}`;
export const PERMIT_WINDOW_MS = 10 * 60_000;

export interface FixtureSigner {
  readonly keyId: string;
  /** Public half only. */
  readonly publicKeySpki: string;
  sign(fields: PermitFields): string;
}

export interface PermitFields {
  readonly v: 1;
  readonly permitId: string;
  readonly grantId: string;
  readonly grantSha256: string;
  readonly shopId: string;
  readonly accountId: string;
  readonly userId: string;
  readonly sessionId: string;
  readonly deviceId: string;
  readonly leaseId: string;
  readonly snapshotId: string;
  readonly accountRevision: number;
  readonly shopRevision: number;
  readonly currentAccountRevision: number;
  readonly currentShopRevision: number;
  readonly authorityVerifiedAt: string;
  readonly deviceVerifiedAt: string;
  readonly snapshotCompletedAt: string;
  readonly issuedAt: string;
  readonly notBefore: string;
  readonly validUntil: string;
  readonly limitingReason: string;
  readonly tillSessionId: string | null;
}

const b64u = (value: string | Buffer) => Buffer.from(value).toString('base64url');

/** UTC instant with six fractional digits, as the merchant API writes them. */
export const canonicalInstant = (epochMs: number) => new Date(epochMs).toISOString().replace('Z', '000Z');

export function createFixtureSigner(keyId: string): FixtureSigner {
  const { publicKey, privateKey } = generateKeyPairSync('rsa', { modulusLength: 2048 });
  return signerFor(keyId, publicKey, privateKey);
}

function signerFor(keyId: string, publicKey: KeyObject, privateKey: KeyObject): FixtureSigner {
  return {
    keyId,
    publicKeySpki: publicKey.export({ type: 'spki', format: 'der' }).toString('base64'),
    sign(fields) {
      const header = `{"alg":"RS256","typ":"intelibill-offline-permit+v1","kid":"${keyId}"}`;
      const signingInput = `${b64u(header)}.${b64u(JSON.stringify(fields))}`;
      const signature = createSign('RSA-SHA256').update(signingInput).sign(privateKey);
      return `${signingInput}.${b64u(signature)}`;
    },
  };
}

/**
 * Builds the merchant app into its own ignored directory with production options and only the public pin map, then
 * applies the same service-worker and offline-pack steps `bun run build` applies.
 */
export function buildPinnedApp(pins: Readonly<Record<string, string>>): void {
  rmSync(PINNED_BUILD_ROOT, { recursive: true, force: true });
  const build = spawnSync(
    'bun',
    ['x', 'ng', 'build', '--output-path', PINNED_BUILD_ROOT, '--define', `IB_OFFLINE_PERMIT_PUBLIC_KEYS=${JSON.stringify(pins)}`],
    { cwd: FRONTEND, encoding: 'utf8' },
  );
  if (build.status !== 0) throw new Error(`Pinned build failed:\n${build.stdout}\n${build.stderr}`);

  const script = pathToFileURL(join(FRONTEND, 'scripts', 'finalize-service-worker.mjs')).href;
  const finalize = spawnSync(
    'node',
    [
      '--input-type=module',
      '-e',
      `import { finalizeServiceWorker, writeOfflinePackManifests } from ${JSON.stringify(script)};` +
        'finalizeServiceWorker(process.argv[1]); writeOfflinePackManifests(process.argv[1]);',
      PINNED_BUILD,
    ],
    { cwd: FRONTEND, encoding: 'utf8' },
  );
  if (finalize.status !== 0) throw new Error(`Worker finalization failed:\n${finalize.stdout}\n${finalize.stderr}`);
}

export const authSession = () => ({ ...session(), sessionId: SESSION_ID });

export function permitFieldsFor(snapshotId: string, completedAtMs: number, overrides: Partial<PermitFields> = {}): PermitFields {
  const completedAt = canonicalInstant(completedAtMs);
  return {
    v: 1,
    permitId: randomUUID(),
    grantId: GRANT_ID,
    grantSha256: GRANT_SHA256,
    shopId: SHOP_A,
    accountId: ACCOUNT_ID,
    userId: USER,
    sessionId: SESSION_ID,
    deviceId: DEVICE_ID,
    leaseId: LEASE_ID,
    snapshotId,
    accountRevision: 1,
    shopRevision: 1,
    currentAccountRevision: 1,
    currentShopRevision: 1,
    authorityVerifiedAt: canonicalInstant(completedAtMs - 60_000),
    deviceVerifiedAt: canonicalInstant(completedAtMs - 30_000),
    snapshotCompletedAt: completedAt,
    issuedAt: completedAt,
    notBefore: completedAt,
    validUntil: canonicalInstant(completedAtMs + PERMIT_WINDOW_MS),
    limitingReason: 'AuthorityFreshness',
    tillSessionId: null,
    ...overrides,
  };
}

/** The stored permit record exactly as the delivery path would keep it for these signed fields. */
export function storedPermitFor(fields: PermitFields, signedPermit: string, keyId: string) {
  const { v: _v, accountId: _a, accountRevision: _ar, shopRevision: _sr, currentAccountRevision: _car, currentShopRevision: _csr, ...kept } = fields;
  return { version: 1, ...kept, keyId, signedPermit };
}

export interface DeliveredPermit {
  readonly fields: PermitFields;
  readonly token: string;
}

export interface OfflineSaleFixture {
  /** Every bound permit the fixture API delivered, in order. */
  readonly delivered: DeliveredPermit[];
  readonly streamRequests: { bound: boolean }[];
}

function batchRecord(nowMs: number) {
  return {
    batchId: BATCH_ID,
    itemId: '61000000-0000-4000-8000-000000000001',
    itemName: ITEM_NAME,
    barcode: BARCODE,
    uom: 'NOS',
    hsnCode: null,
    batchNumber: 'LOT-P0503',
    quantity: 10,
    costPrice: 40,
    mrp: 60,
    salesPrice: 50,
    taxRatePercent: 0,
    taxIncluded: false,
    purchaseTaxIncluded: false,
    expiryDate: null,
    manufacturingDate: null,
    expiryEvidenceId: '62000000-0000-4000-8000-000000000001',
    effectivePolicy: { version: 1, mode: 'Ignore', expiryRequired: false, sellThroughExpiryDate: true, warningDays: 7, overrideAllowed: false },
    physicalQuantity: 10,
    quarantinedQuantity: 0,
    availableQuantity: 10,
    batchVersion: 1,
    expiryCapturedAt: new Date(nowMs - 1000).toISOString(),
    expiryEvidenceExpiresAt: new Date(nowMs + 7 * 86_400_000).toISOString(),
    quantityScale: 0,
    documentUnit: 'NOS',
  };
}

const SELLER_PROFILE = {
  seller: {
    legalName: 'Permit Proof Stores',
    tradeName: 'Permit Proof Stores',
    address: '42 MG Road, Bengaluru, Karnataka 560001',
    stateCode: '29',
    stateName: 'Karnataka',
    gstin: null,
    taxRegime: 'Unregistered',
  },
  eInvoiceStatus: 'NotAttested',
  seriesCode: 'A',
};

/** Installs the signed-in session and the fixture API: a sellable snapshot, an anchored lease, a signed permit. */
export async function installOfflineSaleFixture(page: Page, signer: FixtureSigner): Promise<OfflineSaleFixture> {
  const fixture: OfflineSaleFixture = { delivered: [], streamRequests: [] };

  await page.context().addInitScript(
    ({ authValue, shopId, deviceId }) => {
      localStorage.setItem('inventory.auth.session.local', JSON.stringify(authValue));
      localStorage.setItem('inventory.preferences.language', 'en-IN');
      localStorage.setItem(`intelibill.offlineSales.deviceId.v1:${shopId}`, deviceId);
    },
    { authValue: authSession(), shopId: SHOP_A, deviceId: DEVICE_ID },
  );
  await accessibilityFixtures(page);
  await page.route('**/api/expiry-policies/readiness**', (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({ catalogVersion: 1, results: { missingDateLots: 0, expiredLots: 0, quarantineLots: 0, items: [], totalCount: 0, page: 1, pageSize: 50 } }),
    }),
  );

  await page.context().route('**/api/sales/offline-snapshot/stream**', (route) => {
    const url = new URL(route.request().url());
    const bound = !!(url.searchParams.get('leaseId') && url.searchParams.get('grantId') && url.searchParams.get('deviceId'));
    fixture.streamRequests.push({ bound });
    const now = Date.now();
    const snapshotId = randomUUID();
    const completedAt = canonicalInstant(now);
    const lines: unknown[] = [
      { type: 'metadata', metadata: { snapshotId, shopId: SHOP_A, schemaVersion: 4, businessTimeZoneId: 'Asia/Kolkata', catalogVersion: 1, startedAt: completedAt } },
      { type: 'batch', batch: batchRecord(now) },
    ];
    if (bound) {
      const fields = permitFieldsFor(snapshotId, now);
      const token = signer.sign(fields);
      fixture.delivered.push({ fields, token });
      lines.push({
        type: 'licencePermit',
        licencePermit: {
          version: 1, status: 'issued', leaseId: LEASE_ID, grantId: GRANT_ID, snapshotId, permitId: fields.permitId,
          keyId: signer.keyId, signedPermit: token, notBefore: fields.notBefore, validUntil: fields.validUntil,
          limitingReason: fields.limitingReason,
        },
      });
    }
    lines.push({ type: 'complete', complete: { snapshotId, completedAt, batchCount: 1, customerCount: 0, discountRuleCount: 0, activeLeaseCount: 0, serviceCount: 0 } });
    return route.fulfill({ contentType: 'application/x-ndjson', body: `${lines.map((line) => JSON.stringify(line)).join('\n')}\n` });
  });

  await page.context().route('**/api/sales/invoice-leases/reserve**', (route) => {
    const { deviceId } = route.request().postDataJSON() as { deviceId: string };
    const now = Date.now();
    return route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        leaseId: LEASE_ID, shopId: SHOP_A, deviceId, fiscalYear: FISCAL_YEAR, prefix: 'A/2627/', numberPadding: 6,
        rangeStart: 1, rangeEnd: 100, nextNumber: 1, remainingCount: 100,
        reservedAt: canonicalInstant(now - 1000), expiresAt: canonicalInstant(now + 7 * 86_400_000),
        sellerProfile: SELLER_PROFILE,
        licenceEvidence: { version: 1, grantId: GRANT_ID, grantSha256: GRANT_SHA256, keyId: signer.keyId },
      }),
    });
  });

  return fixture;
}

/** Raw IndexedDB access from the page, independent of the app's own services. */
export async function readRecord<T = Record<string, unknown>>(page: Page, database: string, store: string, key: string): Promise<T | null> {
  return page.evaluate(
    ({ database, store, key }) =>
      new Promise<unknown>((done, fail) => {
        const open = indexedDB.open(database);
        open.onerror = () => fail(open.error);
        open.onsuccess = () => {
          const db = open.result;
          if (!db.objectStoreNames.contains(store)) { db.close(); done(null); return; }
          const request = db.transaction(store, 'readonly').objectStore(store).get(key);
          request.onerror = () => fail(request.error);
          request.onsuccess = () => { db.close(); done(request.result ?? null); };
        };
      }),
    { database, store, key },
  ) as Promise<T | null>;
}

export async function readAll<T = Record<string, unknown>>(page: Page, database: string, store: string): Promise<T[]> {
  return page.evaluate(
    ({ database, store }) =>
      new Promise<unknown[]>((done, fail) => {
        const open = indexedDB.open(database);
        open.onerror = () => fail(open.error);
        open.onsuccess = () => {
          const db = open.result;
          if (!db.objectStoreNames.contains(store)) { db.close(); done([]); return; }
          const request = db.transaction(store, 'readonly').objectStore(store).getAll();
          request.onerror = () => fail(request.error);
          request.onsuccess = () => { db.close(); done(request.result); };
        };
      }),
    { database, store },
  ) as Promise<T[]>;
}

export async function writeRecord(page: Page, database: string, store: string, record: unknown): Promise<void> {
  await page.evaluate(
    ({ database, store, record }) =>
      new Promise<void>((done, fail) => {
        // Version 1 with the app's own key path, so a store the app has not opened yet is created as it would be.
        const open = indexedDB.open(database, 1);
        open.onupgradeneeded = () => {
          if (!open.result.objectStoreNames.contains(store)) open.result.createObjectStore(store, { keyPath: 'key' });
        };
        open.onerror = () => fail(open.error);
        open.onsuccess = () => {
          const db = open.result;
          const transaction = db.transaction(store, 'readwrite');
          transaction.objectStore(store).put(record);
          transaction.oncomplete = () => { db.close(); done(); };
          transaction.onabort = () => fail(transaction.error);
        };
      }),
    { database, store, record },
  );
}

export interface LeaseRow {
  key: string;
  lease: { leaseId: string; nextNumber: number; remainingCount: number; offlinePermit: Record<string, unknown> | null; licenceEvidence: unknown };
  claim?: unknown;
}

export const readLeaseRow = (page: Page) => readRecord<LeaseRow>(page, 'intelibill-invoice-leases', 'invoice-leases', LEASE_KEY);
export const writeLeaseRow = (page: Page, row: LeaseRow) => writeRecord(page, 'intelibill-invoice-leases', 'invoice-leases', row);
export const readClockRow = (page: Page) =>
  readRecord<{ key: string; checkpoint: Record<string, unknown> }>(page, 'intelibill-invoice-leases', 'invoice-leases', CLOCK_KEY);
export const readQueueRows = (page: Page) =>
  readAll<{ key: string; status: string; invoiceNumber: string; shopId: string; deviceId: string; payload: Record<string, any> }>(
    page, 'intelibill-offline-sales-queue', 'queued-sales',
  );

/** The durable facts a refusal must leave exactly as they were. */
export async function durableState(page: Page) {
  const lease = await readLeaseRow(page);
  const queue = await readQueueRows(page);
  const shadow = await readAll<Record<string, unknown>>(page, 'intelibill-offline-shadow-stock', 'shadow-stock');
  return {
    nextNumber: lease?.lease.nextNumber ?? null,
    remainingCount: lease?.lease.remainingCount ?? null,
    claim: lease?.claim ?? null,
    queue: JSON.stringify(queue),
    queueCount: queue.length,
    shadow: JSON.stringify(shadow),
  };
}
