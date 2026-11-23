import { expect, test, type BrowserContext, type Page } from '@playwright/test';

import {
  BARCODE,
  BATCH_ID,
  DEVICE_ID,
  GRANT_ID,
  GRANT_SHA256,
  ITEM_NAME,
  LEASE_ID,
  PINNED_BUILD,
  buildPinnedApp,
  canonicalInstant,
  createFixtureSigner,
  durableState,
  installOfflineSaleFixture,
  permitFieldsFor,
  readClockRow,
  readLeaseRow,
  readQueueRows,
  storedPermitFor,
  writeLeaseRow,
  writeRecord,
  type FixtureSigner,
  type LeaseRow,
  type OfflineSaleFixture,
  type PermitFields,
} from './offline-permit-finalization-fixture';
import { SHOP_A, USER, startPurchaseReceivingServer } from './purchase-receiving-server';

/**
 * Browser release proof for P05-03: offline finalization verifies the stored signed permit before it takes an invoice
 * number and again before it writes the queue row.
 *
 * What is real here: the production build (built once below with only the public pin of a throwaway key), the
 * protected service worker and offline pack, WebCrypto RSA verification, Web Locks and IndexedDB, read back raw.
 * What is a fixture: the API answers and the signing key. This is not live provider verification, and a browser is
 * not tamper-proof time: these tests show the app refuses when its own stored readings disagree, nothing more.
 */

type FixtureServer = Awaited<ReturnType<typeof startPurchaseReceivingServer>>;

const PACK_READY_MS = 120_000;
const MSG_INVALID = 'This device has no valid offline permission for this sale.';
const MSG_EXPIRED = 'Offline permission for this device has ended.';
const MSG_CLOCK = "This device's clock does not match its earlier readings";

let signer: FixtureSigner;
let server: FixtureServer | undefined;

test.describe.configure({ timeout: 300_000 });

test.beforeAll(() => {
  test.setTimeout(300_000);
  signer = createFixtureSigner('p0503-browser-key-1');
  buildPinnedApp({ [signer.keyId]: signer.publicKeySpki });
});

test.beforeEach(async () => {
  server = await startPurchaseReceivingServer(PINNED_BUILD);
});

test.afterEach(async () => {
  await server?.close();
  server = undefined;
});

const submit = (page: Page) => page.locator('app-cart-checkout-summary button').last();
const queuedDialog = (page: Page) => page.getByRole('dialog', { name: 'Sale Queued' });

/** Enables offline billing through the normal online UI, waits for the signed permit and its clock checkpoint. */
async function enableOnline(page: Page): Promise<OfflineSaleFixture> {
  const fixture = await installOfflineSaleFixture(page, signer);
  await page.goto(`${server!.origin}/sales/new`);
  await page.locator('.shell-main').waitFor({ timeout: 30_000 });

  const toggle = page.locator('.offline-sales-toggle');
  await toggle.click();
  await expect(toggle).toHaveAttribute('aria-pressed', 'true', { timeout: PACK_READY_MS });
  await expect.poll(async () => (await readLeaseRow(page))?.lease.offlinePermit?.['signedPermit'] ?? null, { timeout: 30_000 })
    .toBe(fixture.delivered.at(-1)?.token ?? 'no permit delivered');
  await expect.poll(async () => (await readClockRow(page))?.checkpoint['blocked'], { timeout: 30_000 }).toBeNull();
  return fixture;
}

async function goOffline(context: BrowserContext, page: Page): Promise<void> {
  await context.unrouteAll({ behavior: 'ignoreErrors' });
  await context.setOffline(true);
  await page.reload();
  await page.locator('.shell-main').waitFor({ timeout: 30_000 });
}

async function addToCart(page: Page): Promise<void> {
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

/** Done on the confirmation leaves for the sales list; the next sale starts from the till page again, offline. */
async function startNextSale(page: Page): Promise<void> {
  await queuedDialog(page).getByRole('button').last().click();
  await expect(queuedDialog(page)).toBeHidden();
  await page.goto(`${server!.origin}/sales/new`);
  await page.locator('.shell-main').waitFor({ timeout: 30_000 });
}

/** Replaces the stored permit on the real lease row with another one, leaving everything else as found. */
async function storePermit(page: Page, permit: Record<string, unknown> | null): Promise<LeaseRow> {
  const row = (await readLeaseRow(page))!;
  await writeLeaseRow(page, { ...row, lease: { ...row.lease, offlinePermit: permit } });
  return row;
}

async function expectRefused(page: Page, message: string, before: Awaited<ReturnType<typeof durableState>>): Promise<void> {
  await submit(page).click();
  await expect(page.getByText(message, { exact: false }).first()).toBeVisible();
  await expect(queuedDialog(page)).toHaveCount(0);
  // The cart is kept, and nothing durable moved: same cursor, same remaining count, same queue bytes, same stock.
  await expect(page.locator('app-cart-table')).toContainText(ITEM_NAME);
  expect(await durableState(page)).toEqual(before);
}

test('a valid signed permit issues one invoice; an expired one then issues nothing and names the deadline', async ({ page, context }) => {
  const fixture = await enableOnline(page);
  const delivered = fixture.delivered.at(-1)!;
  await goOffline(context, page);

  await addToCart(page);
  await submit(page).click();
  await expect(queuedDialog(page)).toBeVisible();
  await expect(queuedDialog(page)).toContainText('A/2627/000001');

  const lease = (await readLeaseRow(page))!;
  expect(lease.lease.nextNumber).toBe(2);
  expect(lease.lease.remainingCount).toBe(99);
  expect(lease.claim ?? null).toBeNull();

  const rows = await readQueueRows(page);
  expect(rows).toHaveLength(1);
  const [row] = rows;
  expect(row).toMatchObject({ status: 'Pending', invoiceNumber: 'A/2627/000001', shopId: SHOP_A, deviceId: DEVICE_ID });
  expect(row.payload['issuedByUserId']).toBe(USER);
  expect(row.payload['idempotencyKey']).toBe(`offline-sale-${row.payload['clientSaleId']}`);
  // The original signed bytes, frozen with the invoice.
  expect(row.payload['licenceEvidence']).toEqual({
    version: 1,
    permitId: delivered.fields.permitId,
    grantId: GRANT_ID,
    grantSha256: GRANT_SHA256,
    leaseId: LEASE_ID,
    snapshotId: delivered.fields.snapshotId,
    keyId: signer.keyId,
    signedPermit: delivered.token,
  });
  // Existing contracts: integer arithmetic v1 and the frozen v2 document under the lease's seller.
  expect(row.payload['pricing']).toMatchObject({ calculationVersion: 1, totals: { grandTotal: 50, paidAmount: 50, dueAmount: 0 } });
  expect(row.payload['document']).toMatchObject({ number: 'A/2627/000001', seller: { legalName: 'Permit Proof Stores' } });
  expect(row.payload['document']['version']).toBe(2);

  const shadow = JSON.parse((await durableState(page)).shadow) as { inventoryBatchId: string; quantity: number }[];
  expect(shadow).toMatchObject([{ inventoryBatchId: BATCH_ID, quantity: 9 }]);

  // The issued invoice prints from the device, owner-bound.
  const print = await context.newPage();
  await print.goto(`${server!.origin}/sales/${row.payload['clientSaleId']}/print?template=a4&offline=1`);
  await expect(print.getByText('A/2627/000001').first()).toBeVisible({ timeout: 30_000 });
  await print.close();

  // Next sale: the same permit identity, genuinely signed, but its window has ended.
  await startNextSale(page);
  const ended = { ...delivered.fields, validUntil: canonicalInstant(Date.parse(delivered.fields.notBefore) + 1) };
  await storePermit(page, storedPermitFor(ended, signer.sign(ended), signer.keyId));
  await addToCart(page);
  const before = await durableState(page);
  expect(before.queueCount).toBe(1);

  await expectRefused(page, MSG_EXPIRED, before);
  const deadline = page.getByTestId('offline-permit-ended');
  await expect(deadline).toContainText('Offline permission ended:');
  await expect(deadline).toContainText('IST');
  // The earlier pending invoice is byte-identical.
  expect(JSON.parse((await durableState(page)).queue)).toEqual(rows);
});

test('forged, untrusted, out-of-scope, edited and missing permits take no number and queue nothing', async ({ page, context }) => {
  const fixture = await enableOnline(page);
  const genuine = fixture.delivered.at(-1)!;
  await goOffline(context, page);
  await addToCart(page);

  // A stock row and a pending row that predate every refusal: neither may change.
  await writeRecord(page, 'intelibill-offline-shadow-stock', 'shadow-stock', {
    key: `${SHOP_A}::${DEVICE_ID}::${BATCH_ID}`, shopId: SHOP_A, deviceId: DEVICE_ID, inventoryBatchId: BATCH_ID, quantity: 7,
    updatedAt: '2026-01-01T00:00:00.000Z',
  });
  await writeRecord(page, 'intelibill-offline-sales-queue', 'queued-sales', {
    key: `${SHOP_A}::${DEVICE_ID}::earlier-sale`, shopId: SHOP_A, deviceId: DEVICE_ID, clientSaleId: 'earlier-sale',
    idempotencyKey: 'offline-sale-earlier-sale', invoiceNumber: 'A/2627/000900', soldAt: '2026-01-01T00:00:00.000Z',
    payload: { clientSaleId: 'earlier-sale', issuedByUserId: USER }, status: 'Pending', warnings: [], errorCode: null,
    errorMessage: null, serverSaleId: null, createdAt: '2026-01-01T00:00:00.000Z', updatedAt: '2026-01-01T00:00:00.000Z',
    syncAttemptCount: 0, lastSyncAttemptAt: null, syncAttempts: [],
  });

  const stranger = createFixtureSigner('p0503-untrusted-key');
  const otherDevice: PermitFields = { ...genuine.fields, deviceId: 'device-someone-else' };
  const [h, p, s] = genuine.token.split('.');
  const forgedToken = `${h}.${p}.${s[0] === 'A' ? 'B' : 'A'}${s.slice(1)}`;
  const original = storedPermitFor(genuine.fields, genuine.token, signer.keyId);

  const cases: readonly (readonly [string, Record<string, unknown> | null])[] = [
    ['forged signature', storedPermitFor(genuine.fields, forgedToken, signer.keyId)],
    ['valid signature under a key this build does not pin', storedPermitFor(genuine.fields, stranger.sign(genuine.fields), stranger.keyId)],
    ['genuinely signed for another device', storedPermitFor(otherDevice, signer.sign(otherDevice), signer.keyId)],
    ['stored deadline edited, signed bytes untouched', { ...original, validUntil: canonicalInstant(Date.now() + 30 * 86_400_000) }],
    ['no stored permit', null],
  ];

  for (const [name, permit] of cases) {
    await test.step(name, async () => {
      await storePermit(page, permit);
      const before = await durableState(page);
      expect(before).toMatchObject({ nextNumber: 1, remainingCount: 100, claim: null, queueCount: 1 });
      await expectRefused(page, MSG_INVALID, before);
    });
  }

  // The same cart issues once the genuine permit is back: every refusal above was the permit, nothing else.
  await storePermit(page, original);
  await submit(page).click();
  await expect(queuedDialog(page)).toContainText('A/2627/000001');
  expect((await readLeaseRow(page))!.lease.nextNumber).toBe(2);
  expect(await readQueueRows(page)).toHaveLength(2);
});

test('a clock set back latches a refusal that survives restoring the clock and an offline reload, until verified online again', async ({ page, context }) => {
  await page.clock.install();
  const fixture = await enableOnline(page);
  expect(fixture.delivered).toHaveLength(1);
  const realNow = Date.now();

  // Offline without dropping the fixture routes: they are needed again for the online verification below.
  await context.setOffline(true);
  await page.reload();
  await page.locator('.shell-main').waitFor({ timeout: 30_000 });
  await addToCart(page);

  // Five minutes into the ten-minute window a sale is issued: the stored wall high-water is now that moment.
  await page.clock.setSystemTime(realNow + 5 * 60_000);
  await submit(page).click();
  await expect(queuedDialog(page)).toContainText('A/2627/000001');
  await startNextSale(page);
  await addToCart(page);

  // The device clock is then set back four minutes: still inside the signed window, so only the clock evidence can
  // tell. This is the move that would otherwise stretch the window.
  await page.clock.setSystemTime(realNow + 60_000);
  const before = await durableState(page);
  expect(before).toMatchObject({ nextNumber: 2, queueCount: 1 });
  await submit(page).click();
  await expect(page.getByText(MSG_CLOCK, { exact: false }).first()).toBeVisible();
  await expect(queuedDialog(page)).toHaveCount(0);
  const latched = (await readClockRow(page))!.checkpoint;
  expect(latched['blocked']).toMatchObject({ reason: 'WallClockMovedBack' });
  expect(await durableState(page)).toEqual(before);

  // Restoring the wall clock does not release it.
  await page.clock.setSystemTime(realNow + 6 * 60_000);
  await expectRefused(page, MSG_CLOCK, before);

  // Nor does an offline reload: the latch is stored, not remembered.
  await page.reload();
  await page.locator('.shell-main').waitFor({ timeout: 30_000 });
  expect((await readClockRow(page))!.checkpoint['blocked']).toMatchObject({ reason: 'WallClockMovedBack' });
  await addToCart(page);
  await expectRefused(page, MSG_CLOCK, await durableState(page));

  // The clock is corrected to the real time before the device is verified online again.
  await page.clock.setSystemTime(Date.now());
  // Only a genuinely later signed observation, delivered over the live stream, releases it.
  await context.setOffline(false);
  // The refresh cycle only streams once the app has reached the API again, so the trigger is repeated until it does.
  await expect(async () => {
    await page.evaluate(
      ({ shopId, deviceId }) => {
        const key = `intelibill.offlineSales.deviceSettings.v1:${shopId}:${deviceId}`;
        const current = JSON.parse(localStorage.getItem(key) ?? 'null');
        current.lastCompleteSnapshotAt = '2020-01-01T00:00:00.000Z';
        localStorage.setItem(key, JSON.stringify(current));
        localStorage.removeItem('intelibill.offlineSales.offlinePackCycle.v1');
        window.dispatchEvent(new Event('online'));
      },
      { shopId: SHOP_A, deviceId: DEVICE_ID },
    );
    await page.waitForTimeout(2000);
    expect(fixture.delivered.length).toBe(2);
  }).toPass({ timeout: 90_000 });
  await expect.poll(async () => (await readClockRow(page))?.checkpoint['permitId'], { timeout: 30_000 }).toBe(fixture.delivered[1].fields.permitId);
  expect((await readClockRow(page))!.checkpoint['blocked']).toBeNull();

  await context.setOffline(true);
  await submit(page).click();
  await expect(queuedDialog(page)).toContainText('A/2627/000002');
  const second = (await readQueueRows(page)).find((row) => row.invoiceNumber === 'A/2627/000002')!;
  expect(second.payload['licenceEvidence']['permitId']).toBe(fixture.delivered[1].fields.permitId);
  expect((await readLeaseRow(page))!.lease.nextNumber).toBe(3);
});

test('two tabs never share or skip a number, and a permit that ends while a sale is held gives its number back', async ({ page, context }) => {
  // A test-side barrier in front of the real WebCrypto verify: it only waits, then delegates to the native call.
  await context.addInitScript(() => {
    const native = SubtleCrypto.prototype.verify;
    const state = { calls: 0, holdAt: 0, release: null as null | (() => void) };
    (window as unknown as { __permitBarrier: typeof state }).__permitBarrier = state;
    SubtleCrypto.prototype.verify = async function (this: SubtleCrypto, ...args: Parameters<SubtleCrypto['verify']>) {
      state.calls += 1;
      if (state.holdAt === state.calls) await new Promise<void>((resume) => { state.release = resume; });
      return native.apply(this, args);
    };
  });
  await page.clock.install();
  const fixture = await enableOnline(page);
  const genuine = fixture.delivered.at(-1)!;
  await goOffline(context, page);
  const sibling = await context.newPage();
  await sibling.goto(`${server!.origin}/sales/new`);
  await sibling.locator('.shell-main').waitFor({ timeout: 30_000 });
  await addToCart(page);
  await addToCart(sibling);

  const holdSecondVerify = (target: Page) =>
    target.evaluate(() => {
      const barrier = (window as unknown as { __permitBarrier: { calls: number; holdAt: number } }).__permitBarrier;
      barrier.holdAt = barrier.calls + 2;
    });
  const release = (target: Page) =>
    target.evaluate(() => (window as unknown as { __permitBarrier: { release: null | (() => void) } }).__permitBarrier.release?.());
  const claimed = async () => (await readLeaseRow(page))?.claim ?? null;

  // 1. The first tab is held between its two guards, number taken; the sibling's checkout queues behind it.
  await holdSecondVerify(page);
  await submit(page).click();
  await expect.poll(claimed, { timeout: 30_000 }).toMatchObject({ leaseId: LEASE_ID, number: 1 });
  await submit(sibling).click();
  await sibling.waitForTimeout(1500);
  expect((await readLeaseRow(page))!.lease.nextNumber).toBe(2);
  expect(await readQueueRows(page)).toHaveLength(0);
  await expect(queuedDialog(sibling)).toHaveCount(0);

  await release(page);
  await expect(queuedDialog(page)).toContainText('A/2627/000001');
  await expect(queuedDialog(sibling)).toContainText('A/2627/000002');
  const issued = (await readQueueRows(page)).map((row) => row.invoiceNumber).sort();
  expect(issued).toEqual(['A/2627/000001', 'A/2627/000002']);
  expect((await readLeaseRow(page))!).toMatchObject({ lease: { nextNumber: 3 } });
  expect(await claimed()).toBeNull();

  // 2. Held again with number 3 taken; the signed deadline passes before the second guard finishes.
  await startNextSale(page);
  await addToCart(page);
  const rowsBefore = await readQueueRows(page);
  await holdSecondVerify(page);
  await submit(page).click();
  await expect.poll(claimed, { timeout: 30_000 }).toMatchObject({ leaseId: LEASE_ID, number: 3 });
  await page.clock.setSystemTime(Date.parse(genuine.fields.validUntil) + 1);
  await release(page);

  await expect(page.getByText(MSG_EXPIRED, { exact: false }).first()).toBeVisible();
  await expect(page.getByTestId('offline-permit-ended')).toContainText('IST');
  await expect(queuedDialog(page)).toHaveCount(0);
  // Number 3 was given back: no gap, no new row, and the two issued rows are untouched.
  expect((await readLeaseRow(page))!.lease.nextNumber).toBe(3);
  expect(await claimed()).toBeNull();
  expect(await readQueueRows(page)).toEqual(rowsBefore);
});
