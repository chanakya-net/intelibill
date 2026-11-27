import { randomUUID } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import { BARCODE, DEVICE_ID, LEASE_ID, PINNED_BUILD,
  writeLeaseRow, writeRecord, storedPermitFor, buildPinnedApp, canonicalInstant, createFixtureSigner, installOfflineSaleFixture,
  readClockRow, readRecord, readLeaseRow, readQueueRows } from './offline-permit-finalization-fixture';
import { SHOP_A, USER, startPurchaseReceivingServer } from './purchase-receiving-server';

import { renewalRoutes, renewedLease } from './offline-permit-renewal-routes';
const signer = createFixtureSigner('p0508-browser');
let server: Awaited<ReturnType<typeof startPurchaseReceivingServer>>;

test.use({ actionTimeout: 12_000 });
test.describe.configure({ timeout: 120_000 });
test.beforeAll(() => { buildPinnedApp({ [signer.keyId]: signer.publicKeySpki }); });
test.beforeEach(async () => { server = await startPurchaseReceivingServer(PINNED_BUILD); });
test.afterEach(async () => { await server?.close(); });

async function enable(page: Page, options?: Parameters<typeof installOfflineSaleFixture>[2]) {
  const fixture = await installOfflineSaleFixture(page, signer, options);
  // The initial merchant projection predates the held permit. Opening a second tab must not
  // consume a mock lease before the test installs its renewal.
  const initial = (options?.now?.() ?? Date.now()) - 120_000;
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
  const requests = await renewalRoutes(page, signer, verified);
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
    const counts = await renewalRoutes(page, signer, Date.now(), mode);
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
  const counts = await renewalRoutes(page, signer, Date.now());
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
  await context.setOffline(false);
  await expect.poll(async () => {
    const held = (await readLeaseRow(page))?.lease.offlinePermit;
    const usable = (await pointer(page))?.['usableSnapshotId'];
    return held?.['snapshotId'] !== original.fields.snapshotId && held?.['snapshotId'] === usable;
  }).toBe(true);
  expect(counts.reservations).toBe(1);
  const repaired = (await readLeaseRow(page))!.lease.offlinePermit!;
  expect(repaired['authorityVerifiedAt']).toBe(original.fields.authorityVerifiedAt);
  expect(repaired['validUntil']).toBe(original.fields.validUntil);
  await expect.poll(async () => (await readClockRow(page))?.checkpoint['permitId']).toBe(repaired['permitId']);
  await context.setOffline(true);
  await page.locator('app-cart-checkout-summary button').last().click();
  await expect(page.getByRole('dialog', { name: 'Sale Queued' })).toContainText('A/2627/000001');
});

test('reload repairs settings after IndexedDB committed the new range', async ({ page, context }) => {
  await enable(page);
  await page.evaluate(id => {
    const save = Storage.prototype.setItem;
    Storage.prototype.setItem = function(key, value) {
      if (key.startsWith('intelibill.offlineSales.deviceSettings.') && JSON.parse(value).lastReservedLease?.leaseId === id)
        throw new DOMException('Interrupted settings write', 'QuotaExceededError');
      save.call(this, key, value);
    };
  }, renewedLease);
  const counts = await renewalRoutes(page, signer, Date.now());
  await reconnect(page);
  await expect.poll(async () => (await readLeaseRow(page))?.lease.leaseId).toBe(renewedLease);
  expect((await deviceSettings(page)).lastReservedLease.leaseId).toBe(LEASE_ID);
  await page.reload();
  await page.locator('.shell-main').waitFor();
  await reconnect(page);
  await expect.poll(async () => (await deviceSettings(page)).lastReservedLease.leaseId).toBe(renewedLease);
  const permit = (await readLeaseRow(page))!.lease.offlinePermit!;
  expect((await deviceSettings(page)).lastOfflinePermit.permitId).toBe(permit['permitId']);
  await expect.poll(async () => (await readClockRow(page))?.checkpoint['permitId']).toBe(permit['permitId']);
  expect(counts.reservations).toBe(1);
  await context.setOffline(true);
  await cart(page);
  await page.locator('app-cart-checkout-summary button').last().click();
  await expect(page.getByRole('dialog', { name: 'Sale Queued' })).toContainText('A/2627/000101');
});

test('a known suspension survives reload and an unenforced authority response', async ({ page }) => {
  await enable(page);
  const held = await readLeaseRow(page);
  const counts = await renewalRoutes(page, signer, Date.now());
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
    const counts = await renewalRoutes(page, signer, Date.now());
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
  const counts = await renewalRoutes(page, signer, Date.now(), 'valid', new Promise<void>(resolve => { release = resolve; }));
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
  const counts = await renewalRoutes(page, signer, Date.now(), 'valid', barrier, tab => { streamOwner = tab; });
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

for (const cap of ['equal end', 'AuthorityFreshness', 'FiscalYearEnd', 'AuthDeadline', 'TillDayEnd'] as const) {
  test(`repeated verified polls spend zero blocks at ${cap}`, async ({ page }) => {
    const start = Date.parse('2027-03-31T18:00:00Z');
    // Use the fiscal boundary only in its own case so it cannot mask the other caps.
    const fixed = cap === 'FiscalYearEnd' ? start : Date.parse('2026-11-23T18:00:00Z');
    const hour = 3_600_000;
    const authEnd = fixed + (cap === 'AuthDeadline' ? 1 : 72) * hour;
    const heldEnd = cap === 'FiscalYearEnd' || cap === 'TillDayEnd' ? fixed + hour / 2 :
      cap === 'AuthDeadline' ? authEnd : cap === 'AuthorityFreshness' ? fixed + 48 * hour - 60_000 : fixed + hour;
    await page.clock.install({ time: fixed });
    const original = await enable(page, { now: () => fixed, authEnd, fields: {
      validUntil: canonicalInstant(heldEnd), limitingReason: cap === 'equal end' ? 'LicenceEnd' : cap,
    } });
    if (cap === 'TillDayEnd') await selectFixtureTill(page, original, fixed);
    const counts = await renewalRoutes(page, signer, fixed);
    let polls = 0;
    await page.route('**/trading-authority*', route => {
      polls++;
      return route.fulfill({ json: { schemaVersion: 1, enforced: true, shopId: SHOP_A,
        authorityVerifiedAt: canonicalInstant(fixed), validUntil: canonicalInstant(cap === 'equal end' ? heldEnd : fixed + 48 * hour) } });
    });
    for (let i = 0; i < 2; i++) {
      await page.evaluate(key => {
        const settings = JSON.parse(localStorage.getItem(key)!);
        localStorage.setItem(key, JSON.stringify({ ...settings, permitRefreshNotice: 'restricted' }));
      }, `intelibill.offlineSales.deviceSettings.v1:${SHOP_A}:${DEVICE_ID}`);
      await reconnect(page);
      await expect.poll(async () => (await deviceSettings(page)).permitRefreshNotice).toBeUndefined();
    }
    expect(polls).toBeGreaterThanOrEqual(2);
    expect(counts).toEqual({ reservations: 0, streams: 0 });
    expect((await readLeaseRow(page))?.lease.offlinePermit?.['snapshotId']).toBe(original.fields.snapshotId);
  });
}

async function selectFixtureTill(page: Page, original: Awaited<ReturnType<typeof enable>>, fixed: number) {
  const till = { id: randomUUID(), state: 'Open', cashierId: USER, businessDate: '2026-11-23', businessTimeZoneId: 'Asia/Kolkata' };
  const capability = { enabled: true, businessDate: till.businessDate };
  await page.route('**/tills/capabilities*', route => route.fulfill({ json: capability }));
  await page.route('**/tills/sessions*', route => route.fulfill({ json: { items: [till], totalCount: 1 } }));
  await writeRecord(page, 'intelibill-till-verification', 'verified', { shopId: SHOP_A, userId: USER,
    capability, session: till, verifiedAt: canonicalInstant(fixed) });
  const fields = { ...original.fields, tillSessionId: till.id };
  const row = (await readLeaseRow(page))!;
  row.lease.offlinePermit = storedPermitFor(fields, signer.sign(fields), signer.keyId);
  await writeLeaseRow(page, row);
  const loaded = page.waitForResponse('**/tills/sessions*');
  await page.reload();
  await loaded;
  return till.id;
}

test('a server-only stock cap keeps the earlier range and its retry delay', async ({ page }) => {
  const original = await enable(page);
  const counts = await renewalRoutes(page, signer, Date.now(), 'stock-cap', undefined, undefined, original.fields.validUntil);
  await reconnect(page);
  await expect.poll(async () => (await pointer(page))?.['lastAttemptStatus']).toBe('failed');
  expect((await pointer(page))?.['usableSnapshotId']).toBe(original.fields.snapshotId);
  expect((await deviceSettings(page)).permitRetry.failures).toBe(1);
  const read = page.waitForRequest('**/trading-authority*');
  await reconnect(page);
  await read;
  await page.evaluate(() => navigator.locks.request('intelibill.tab-task:offline-pack-update', () => undefined));
  expect(counts).toEqual({ reservations: 1, streams: 1 });
});

test('a new selected till receives a same-lease permit', async ({ page }) => {
  const original = await enable(page);
  const selected = await selectFixtureTill(page, original, Date.now());
  const row = (await readLeaseRow(page))!;
  const old = { ...original.fields, tillSessionId: randomUUID() };
  row.lease.offlinePermit = storedPermitFor(old, signer.sign(old), signer.keyId);
  await writeLeaseRow(page, row);
  const counts = await renewalRoutes(page, signer, Date.now());
  await reconnect(page);
  await expect.poll(async () => (await readLeaseRow(page))?.lease.offlinePermit?.['tillSessionId']).toBe(selected);
  expect((await readLeaseRow(page))?.lease.leaseId).toBe(LEASE_ID);
  expect(counts.reservations).toBe(0);
});
