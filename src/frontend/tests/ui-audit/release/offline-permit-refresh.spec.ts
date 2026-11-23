import { randomUUID } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import { BARCODE, DEVICE_ID, FISCAL_YEAR, GRANT_SHA256, LEASE_ID, PINNED_BUILD, SELLER_PROFILE,
  batchRecord, buildPinnedApp, canonicalInstant, createFixtureSigner, installOfflineSaleFixture,
  permitFieldsFor, readClockRow, readRecord, readLeaseRow, readQueueRows } from './offline-permit-finalization-fixture';
import { SHOP_A, USER, startPurchaseReceivingServer } from './purchase-receiving-server';

const renewedLease = '11111111-1111-4111-8111-111111111112';
const renewedGrant = '22222222-2222-4222-8222-222222222223';
const signer = createFixtureSigner('p0508-browser');
let server: Awaited<ReturnType<typeof startPurchaseReceivingServer>>;

test.use({ actionTimeout: 12_000 });
test.describe.configure({ timeout: 120_000 });
test.beforeAll(() => { buildPinnedApp({ [signer.keyId]: signer.publicKeySpki }); });
test.beforeEach(async () => { server = await startPurchaseReceivingServer(PINNED_BUILD); });
test.afterEach(async () => { await server?.close(); });

async function enable(page: Page) {
  const fixture = await installOfflineSaleFixture(page, signer);
  // The initial merchant projection predates the held permit. Opening a second tab must not
  // consume a mock lease before the test installs its renewal.
  const initial = Date.now() - 120_000;
  await page.context().route('**/trading-authority*', route => route.fulfill({ json: {
    schemaVersion: 1, enforced: false, shopId: SHOP_A, authorityVerifiedAt: canonicalInstant(initial),
    validUntil: canonicalInstant(initial + 86_400_000),
  } }));
  await page.goto(server.origin + '/sales/new');
  const toggle = page.locator('.offline-sales-toggle');
  await toggle.click();
  await expect(toggle).toHaveAttribute('aria-pressed', 'true', { timeout: 120_000 });
  await expect.poll(async () => !!(await readLeaseRow(page))?.lease.offlinePermit).toBe(true);
  return fixture.delivered.at(-1)!;
}

async function cart(page: Page) {
  const input = page.locator('app-batch-search-bar input').first();
  await input.fill(BARCODE);
  await input.press('Enter');
  const picker = page.getByRole('dialog', { name: /Select a Batch/i });
  await picker.locator('.batch-option').first().press('Enter');
  await picker.getByRole('button', { name: /Add to Cart$/i }).click();
}

async function reconnect(page: Page) {
  await page.evaluate(() => {
    localStorage.removeItem('intelibill.offlineSales.offlinePackCycle.v1');
    window.dispatchEvent(new Event('online'));
  });
}

type CandidateMode = 'valid' | 'stale' | 'untrusted' | 'wrong-session' | 'reordered-revision' | 'refused';
async function renewalRoutes(page: Page, verified: number, mode: CandidateMode = 'valid', barrier?: Promise<void>, onStream?: (tab: Page) => void) {
  const routes = onStream ? page.context() : page;
  const counts = { reservations: 0, streams: 0 };
  await routes.route('**/trading-authority*', route => route.fulfill({ json: {
    schemaVersion: 1, shopId: SHOP_A, enforced: true, authorityVerifiedAt: canonicalInstant(verified),
    validUntil: canonicalInstant(verified + 60 * 60_000),
  } }));
  await routes.route('**/invoice-leases/reserve*', route => {
    counts.reservations++;
    return route.fulfill({ json: {
      leaseId: renewedLease, shopId: SHOP_A, deviceId: DEVICE_ID, fiscalYear: FISCAL_YEAR,
      prefix: 'A/2627/', numberPadding: 6, rangeStart: 101, rangeEnd: 200, nextNumber: 101, remainingCount: 100,
      reservedAt: canonicalInstant(verified), expiresAt: canonicalInstant(verified + 86_400_000),
      sellerProfile: SELLER_PROFILE, licenceEvidence: { version: 1, grantId: renewedGrant, grantSha256: GRANT_SHA256, keyId: signer.keyId },
    } });
  });
  await routes.route('**/offline-snapshot/stream*', async route => {
    if (new URL(route.request().url()).searchParams.get('leaseId') !== renewedLease) return route.fallback();
    counts.streams++;
    onStream?.(route.request().frame().page());
    if (barrier) await barrier;
    const signingKey = mode === 'untrusted' ? createFixtureSigner('other') : signer;
    const snapshotId = randomUUID();
    const issuedAt = Math.max(verified, Date.now());
    const completedAt = canonicalInstant(issuedAt);
    const fields = permitFieldsFor(snapshotId, issuedAt, { leaseId: renewedLease, grantId: renewedGrant,
      authorityVerifiedAt: canonicalInstant(mode === 'stale' ? verified - 120_000 : verified), deviceVerifiedAt: canonicalInstant(verified),
      ...(mode === 'wrong-session' ? { sessionId: randomUUID() } : {}),
      shopRevision: mode === 'reordered-revision' ? 0 : 2, currentShopRevision: mode === 'reordered-revision' ? 0 : 2 });
    const records = [
      { type: 'metadata', metadata: { snapshotId, shopId: SHOP_A, schemaVersion: 4, businessTimeZoneId: 'Asia/Kolkata', catalogVersion: 1, startedAt: completedAt } },
      { type: 'batch', batch: batchRecord(verified) },
      { type: 'licencePermit', licencePermit: mode === 'refused' ?
        { version: 1, status: 'refused', refusalCode: 'OfflinePermit.AuthorityChanged', leaseId: renewedLease, grantId: renewedGrant, snapshotId } :
        { version: 1, status: 'issued', ...fields, keyId: signingKey.keyId, signedPermit: signingKey.sign(fields) } },
      { type: 'complete', complete: { snapshotId, completedAt, batchCount: 1, customerCount: 0, discountRuleCount: 0, activeLeaseCount: 0, serviceCount: 0 } },
    ];
    return route.fulfill({ contentType: 'application/x-ndjson', body: records.map(r => JSON.stringify(r)).join('\n') + '\n' });
  });
  return counts;
}

test('unseen renewal extends nothing; a verified reconnect updates future issuance', async ({ page, context }) => {
  await page.clock.install();
  const original = await enable(page);
  await context.setOffline(true);
  await page.reload();
  await cart(page);
  const submit = page.locator('app-cart-checkout-summary button').last();
  await submit.click();
  await expect(page.getByRole('dialog', { name: 'Sale Queued' })).toContainText('A/2627/000001');
  const [earlier] = await readQueueRows(page);
  const verified = Date.parse(original.fields.validUntil) + 1000;
  const requests = await renewalRoutes(page, verified);
  await page.clock.setSystemTime(verified + 1000);
  await page.reload();
  await cart(page);
  await submit.click();
  await expect(page.getByTestId('offline-permit-ended')).toBeVisible();
  expect((await readLeaseRow(page))?.lease.leaseId).toBe(LEASE_ID);
  expect(requests.reservations).toBe(0);

  await page.route('**/api/sales/offline-sync', route => route.fulfill({ json: { results: [] } }));
  const sent = page.waitForRequest(request => request.url().endsWith('/api/sales/offline-sync'));
  await context.setOffline(false);
  expect((await sent).postDataJSON().sales[0].licenceEvidence.signedPermit).toBe(original.token);
  await expect.poll(async () => (await readLeaseRow(page))?.lease.leaseId, { timeout: 25_000 }).toBe(renewedLease);
  expect(requests.reservations).toBe(1);
  await page.locator('app-sidebar-profile-menu').getByRole('button').first().click();
  await page.getByText('Manage Shop', { exact: true }).filter({ visible: true }).click();
  const overlay = page.locator('section.manage-shop-overlay');
  await overlay.getByRole('button', { name: 'Offline billing for this device' }).click();
  const adopted = (await readLeaseRow(page))!.lease.offlinePermit!;
  const ist = (instant: unknown) => new Intl.DateTimeFormat('en-IN', {
    dateStyle: 'medium', timeStyle: 'medium', timeZone: 'Asia/Kolkata',
  }).format(new Date(String(instant))) + ' IST';
  await expect(overlay.locator('.offline-authority-row')).toContainText(ist(adopted['authorityVerifiedAt']), { ignoreCase: true });
  await expect(overlay.locator('.offline-permit-row')).toContainText(ist(adopted['validUntil']), { ignoreCase: true });
  await expect(overlay).toContainText('Offline permission renewed for future invoices.');
  await overlay.locator('.overlay-close').click();
  const [preserved] = await readQueueRows(page);
  expect(JSON.stringify(preserved.payload)).toBe(JSON.stringify(earlier.payload));
  await context.setOffline(true);
  await submit.click();
  await expect(page.getByRole('dialog', { name: 'Sale Queued' })).toContainText('A/2627/000101');
  const second = (await readQueueRows(page)).find(row => row.invoiceNumber === 'A/2627/000101');
  expect(second?.payload['licenceEvidence'].leaseId).toBe(renewedLease);
});

const pointer = (page: Page) => readRecord(page, 'intelibill-offline-sales-snapshot', 'shop-pointers', SHOP_A);
const deviceSettings = (page: Page) => page.evaluate(key => JSON.parse(localStorage.getItem(key)!),
  `intelibill.offlineSales.deviceSettings.v1:${SHOP_A}:${DEVICE_ID}`);

for (const mode of ['stale', 'untrusted', 'wrong-session', 'reordered-revision', 'refused'] as const) {
  test(`candidate response (${mode}) preserves the old usable authority through reload`, async ({ page, context }) => {
    const original = await enable(page);
    const lease = await readLeaseRow(page);
    const clock = await readClockRow(page);
    const settings = await deviceSettings(page);
    const counts = await renewalRoutes(page, Date.now(), mode);
    await reconnect(page);
    await expect.poll(async () => (await pointer(page))?.['lastAttemptStatus']).toBe('failed');
    expect((await pointer(page))?.['usableSnapshotId']).toBe(original.fields.snapshotId);
    await page.reload();
    await page.locator('.shell-main').waitFor();
    await reconnect(page);
    expect((await deviceSettings(page)).permitRetry.failures).toBe(1);
    expect((await deviceSettings(page)).lastApiVerifiedAt).toBe(settings.lastApiVerifiedAt);
    expect(await readLeaseRow(page)).toEqual(lease);
    expect(await readClockRow(page)).toEqual(clock);
    await context.setOffline(true);
    await page.reload();
    await cart(page);
    await page.locator('app-cart-checkout-summary button').last().click();
    await expect(page.getByRole('dialog', { name: 'Sale Queued' })).toContainText('A/2627/000001');
    expect((await readQueueRows(page))[0].payload['licenceEvidence'].signedPermit).toBe(original.token);
    expect(counts).toEqual({ reservations: 1, streams: 1 });
  });
}

test('aborting the lease write after the snapshot commit fails closed on the next boot', async ({ page, context }) => {
  const original = await enable(page);
  const lease = await readLeaseRow(page);
  const clock = await readClockRow(page);
  await page.evaluate(candidate => {
    const put = IDBObjectStore.prototype.put;
    IDBObjectStore.prototype.put = function(value, key) {
      const request = put.apply(this, key === undefined ? [value] : [value, key]);
      if (this.name === 'invoice-leases' && value.lease?.leaseId === candidate) this.transaction.abort();
      return request;
    };
  }, renewedLease);
  await renewalRoutes(page, Date.now());
  await reconnect(page);
  await expect.poll(async () => (await pointer(page))?.['usableSnapshotId']).not.toBe(original.fields.snapshotId);
  expect((await pointer(page))?.['lastAttemptStatus']).toBe('complete');
  expect(await readLeaseRow(page)).toEqual(lease);
  expect(await readClockRow(page)).toEqual(clock);
  await context.setOffline(true);
  await page.reload();
  await cart(page);
  await page.locator('app-cart-checkout-summary button').last().click();
  await expect(page.getByText('This device has no valid offline permission', { exact: false }).first()).toBeVisible();
  expect(await readLeaseRow(page)).toEqual(lease);
  expect(await readQueueRows(page)).toEqual([]);
});

test('a known suspension survives reload and an unenforced authority response', async ({ page }) => {
  await enable(page);
  const held = await readLeaseRow(page);
  const counts = await renewalRoutes(page, Date.now());
  const marker = `intelibill.shopDataRestricted.v1:${USER}|${SHOP_A}`;
  await page.evaluate(key => localStorage.setItem(key, 'suspension'), marker);
  let reads = 0;
  await page.route('**/trading-authority*', route => {
    reads++;
    return route.fulfill({ json: { schemaVersion: 1, enforced: false, shopId: SHOP_A,
      authorityVerifiedAt: canonicalInstant(Date.now()), validUntil: canonicalInstant(Date.now() + 60_000) } });
  });
  await page.reload();
  await reconnect(page);
  await expect.poll(() => reads).toBeGreaterThan(0);
  expect(await page.evaluate(key => [localStorage.getItem(key), localStorage.getItem(key + ':confirmed')], marker))
    .toEqual(['suspension', null]);
  await expect(page.locator('app-cart-checkout-summary')).toHaveCount(0);
  expect(await readLeaseRow(page)).toEqual(held);
  expect(counts).toEqual({ reservations: 0, streams: 0 });
});

for (const failure of ['earlier preview', 'verification unavailable', 'trading restricted', 'reservation refused'] as const) {
  test(`${failure} changes only the status notice`, async ({ page }) => {
    const original = await enable(page);
    const lease = await readLeaseRow(page);
    const clock = await readClockRow(page);
    const before = await deviceSettings(page);
    const counts = await renewalRoutes(page, Date.now());
    let refusedReservations = 0;
    if (failure === 'reservation refused') {
      await page.route('**/invoice-leases/reserve*', route => {
        refusedReservations++;
        return route.fulfill({ status: 403, json: { code: 'Licence.Expired' } });
      });
    } else {
      const verified = Date.parse(original.fields.authorityVerifiedAt) - 1000;
      await page.route('**/trading-authority*', route => {
        if (failure === 'earlier preview') return route.fulfill({ json: { schemaVersion: 1, shopId: SHOP_A,
          enforced: false, authorityVerifiedAt: canonicalInstant(verified), validUntil: canonicalInstant(verified + 86_400_000) } });
        const restricted = failure === 'trading restricted';
        return route.fulfill({ status: restricted ? 403 : 503, json: { enforced: false,
          code: restricted ? 'Licence.TradingRestricted' : 'Licence.VerificationUnavailable' } });
      });
    }
    await reconnect(page);
    const notice = ['reservation refused', 'trading restricted'].includes(failure) ? 'restricted' : 'unverified';
    await expect.poll(async () => (await deviceSettings(page)).permitRefreshNotice).toBe(notice);
    expect(await readLeaseRow(page)).toEqual(lease);
    expect(await readClockRow(page)).toEqual(clock);
    expect((await deviceSettings(page)).lastApiVerifiedAt).toBe(before.lastApiVerifiedAt);
    expect(counts).toEqual({ reservations: 0, streams: 0 });
    expect(refusedReservations).toBe(failure === 'reservation refused' ? 1 : 0);
  });
}

test('reloading during a candidate stream preserves the old lease and the reservation back-off', async ({ page }) => {
  await enable(page);
  const old = await readLeaseRow(page);
  let release!: () => void;
  const counts = await renewalRoutes(page, Date.now(), 'valid', new Promise<void>(resolve => { release = resolve; }));
  try {
    await reconnect(page);
    await expect.poll(() => counts.streams).toBe(1);
    await page.reload();
    await page.locator('.shell-main').waitFor();
    await reconnect(page);
    expect(await readLeaseRow(page)).toEqual(old);
    expect((await deviceSettings(page)).permitRetry.failures).toBe(1);
    expect(counts.reservations).toBe(1);
  } finally {
    release();
  }
});

test('two reconnecting tabs spend one block and a sale during the stream keeps the original permit', async ({ page, context }) => {
  const original = await enable(page);
  const sibling = await context.newPage();
  await sibling.goto(server.origin + '/sales/new');
  await sibling.locator('.shell-main').waitFor();
  let release!: () => void;
  const barrier = new Promise<void>(resolve => { release = resolve; });
  let streamOwner: Page | undefined;
  const counts = await renewalRoutes(page, Date.now(), 'valid', barrier, tab => { streamOwner = tab; });
  try {
    await Promise.all([reconnect(page), reconnect(sibling)]);
    await expect.poll(() => counts.streams).toBe(1);
    // Either tab can win the shared lock. Disconnect the other till while the winning stream is held.
    const issuer = streamOwner === page ? sibling : page;
    const tillNetwork = await context.newCDPSession(issuer);
    await tillNetwork.send('Network.enable');
    await tillNetwork.send('Network.emulateNetworkConditions', {
      offline: true, latency: 0, downloadThroughput: 0, uploadThroughput: 0,
    });
    await issuer.reload();
    await cart(issuer);
    await issuer.locator('app-cart-checkout-summary button').last().click();
    await expect(issuer.getByRole('dialog', { name: 'Sale Queued' })).toContainText('A/2627/000001');
    const [issued] = await readQueueRows(issuer);
    expect(issued.payload['licenceEvidence'].signedPermit).toBe(original.token);
    release();
    await expect.poll(async () => (await readLeaseRow(page))?.lease.leaseId).toBe(renewedLease);
    expect((await readQueueRows(page))[0].payload).toEqual(issued.payload);
    expect(counts.reservations).toBe(1);
  } finally {
    release();
    await sibling.close();
  }
});
