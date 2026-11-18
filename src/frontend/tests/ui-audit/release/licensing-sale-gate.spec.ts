import { createHash } from 'node:crypto';
import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test, type Page, type Route } from '@playwright/test';

import { accessibilityFixtures, SALE } from './accessibility-localization-fixtures';
import { session, SHOP_A, startPurchaseReceivingServer } from './purchase-receiving-server';

const BUILD = resolve(__dirname, '../../../dist/INVENTORY/browser');
const AUTH_STORAGE = 'inventory.auth.session.local';
const EXPIRED = 'This shop’s trading access has ended.';
const OUTAGE = 'Trading access could not be verified.';
const SERVICE = {
  kind: 'Service', serviceId: 'repair-fixture', code: 'REPAIR', name: 'Fixture repair',
  description: null, price: 50, hsnCode: null, taxRatePercent: 0, taxIncluded: false,
};

test.use({ serviceWorkers: 'block', reducedMotion: 'reduce' });

type Phase = 'verified' | 'expired' | 'outage' | 'observe-only';
type PostMode = 'normal' | 'drop-before-commit' | 'drop-after-commit';

const json = (route: Route, body: unknown, status = 200) =>
  route.fulfill({ status, json: body, headers: { 'Cache-Control': 'no-store' } });

async function installFixture(page: Page) {
  await accessibilityFixtures(page);
  const state = { phase: 'verified' as Phase, mode: 'normal' as PostMode, membershipDenied: false };
  const log: {
    method: string; status: number | 'lost'; key?: string; hash?: string; code?: string;
    lineTypes?: string[];
  }[] = [];
  const committed = new Map<string, string>();

  const refusal = () => state.phase === 'outage'
    ? { status: 503, code: 'Licence.VerificationUnavailable' }
    : { status: 403, code: 'Licence.Expired' };
  await page.route('**/api/licensing/shops/*/trading-authority', (route) => {
    if (state.phase === 'verified') {
      log.push({ method: 'authority', status: 200 });
      return json(route, { schemaVersion: 1, shopId: SHOP_A, enforced: true,
        authorityVerifiedAt: new Date().toISOString(),
        validUntil: new Date(Date.now() + 3600_000).toISOString() });
    }
    const denied = refusal();
    log.push({ method: 'authority', ...denied });
    return json(route, { ...denied, enforced: state.phase !== 'observe-only' }, denied.status);
  });
  await page.route('**/api/sales/sellables?*', (route) => {
    const url = new URL(route.request().url());
    const query = url.searchParams.get('searchTerm') ?? url.searchParams.get('barcode');
    return query?.toUpperCase() === 'REPAIR' ? json(route, [SERVICE]) : route.fallback();
  });
  await page.route('**/api/sales/by-idempotency-key/*', (route) => {
    const key = decodeURIComponent(new URL(route.request().url()).pathname.split('/').at(-1)!);
    const found = committed.has(key);
    log.push({ method: 'lookup', key, status: found ? 200 : 404 });
    return json(route, found ? SALE : { code: 'Sale.IdempotencyKeyNotFound' }, found ? 200 : 404);
  });
  await page.route('**/api/sales', async (route) => {
    if (route.request().method() !== 'POST') return route.fallback();
    const body = route.request().postDataJSON() as { idempotencyKey: string; items: { lineType: string }[] };
    const key = body.idempotencyKey;
    const hash = createHash('sha256').update(route.request().postData()!).digest('hex');
    const event = { method: 'sale', key, hash, lineTypes: body.items.map((line) => line.lineType) };
    if (state.membershipDenied) {
      const denied = { status: 403, code: 'Till.Forbidden' };
      log.push({ ...event, ...denied });
      return json(route, denied, denied.status);
    }
    if (committed.has(key)) {
      expect(hash).toBe(committed.get(key));
      log.push({ ...event, status: 201 });
      return json(route, SALE, 201);
    }
    if (state.phase === 'expired' || state.phase === 'outage') {
      const denied = refusal();
      log.push({ ...event, ...denied });
      return json(route, { ...denied, title: 'Fixture licence refusal' }, denied.status);
    }
    if (state.mode !== 'drop-before-commit') committed.set(key, hash);
    if (state.mode !== 'normal') {
      log.push({ ...event, status: 'lost' });
      return route.abort('failed');
    }
    log.push({ ...event, status: 201 });
    return json(route, SALE, 201);
  });
  return { state, log, committed, posts: () => log.filter((event) => event.method === 'sale') };
}

async function addLine(page: Page, search: string) {
  const input = page.locator('app-batch-search-bar input').first();
  await input.fill(search);
  await input.press('Enter');
  if (search === SERVICE.code) {
    await expect(page.locator('app-cart-table')).toContainText(SERVICE.name);
    return;
  }
  const picker = page.getByRole('dialog', { name: /Select a Batch/i });
  await expect(picker).toBeVisible();
  const options = picker.locator('.batch-option');
  await options.first().press('Enter');
  await picker.getByRole('button', { name: /Add to Cart$/i }).click();
  await expect(picker).toBeHidden();
}

const submit = (page: Page) => page.locator('app-cart-checkout-summary button').last();
const pending = (page: Page) => page.locator('.pending-checkouts');

let server: Awaited<ReturnType<typeof startPurchaseReceivingServer>>;
let fixture: Awaited<ReturnType<typeof installFixture>>;

test.beforeEach(async ({ page }) => {
  server = await startPurchaseReceivingServer(BUILD);
  fixture = await installFixture(page);
  await page.addInitScript(({ key, value }) => {
    if (!localStorage.getItem(key)) localStorage.setItem(key, JSON.stringify(value));
  }, { key: AUTH_STORAGE, value: session() });
});

test.afterEach(async ({ browser }, info) => {
  mkdirSync(info.outputDir, { recursive: true });
  writeFileSync(info.outputPath('fixture-licensing-http.json'), JSON.stringify({
    browser: browser.version(), requests: fixture.log, fixtureCommits: fixture.committed.size,
  }, null, 2) + '\n');
  await server.close();
});

test('P04-05 stale mixed checkout keeps its cart when trading ends', async ({ page }, info) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(server.origin + '/sales/new');
  await addLine(page, 'A11Y');
  await addLine(page, 'REPAIR');
  await expect(page.locator('app-cart-table').getByText(SERVICE.name, { exact: false })).toBeVisible();
  await expect(page.getByTestId('checkout-economics')).toContainText('100.00');
  await expect(submit(page)).toBeEnabled();
  fixture.state.phase = 'expired';

  await submit(page).click();
  await expect(page.getByRole('alert')).toContainText(EXPIRED);
  await expect(pending(page)).toHaveCount(0);
  await expect(page.locator('app-cart-table').getByText(SERVICE.name, { exact: false })).toBeVisible();
  await expect(submit(page)).toBeDisabled();
  expect(fixture.posts()).toMatchObject([{ status: 403, lineTypes: ['Goods', 'Service'] }]);

  await page.reload();
  await expect(page.getByRole('alert')).toContainText(EXPIRED);
  await expect(submit(page)).toBeDisabled();
  expect(fixture.posts()).toHaveLength(1);
  expect(fixture.committed.size).toBe(0);
  await page.screenshot({ path: info.outputPath('expired-checkout-desktop.png'), fullPage: true, animations: 'disabled' });
});

for (const committed of [true, false]) {
  test(`P04-05 pending owner checkout retries the same key when the first sale ${committed ? 'committed' : 'never committed'}`, async ({ page }, info) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(server.origin + '/sales/new');
    await addLine(page, 'A11Y');
    await expect(page.getByTestId('checkout-economics')).toContainText('50.00');
    fixture.state.mode = committed ? 'drop-after-commit' : 'drop-before-commit';
    await submit(page).click();
    await expect(pending(page)).toBeVisible();
    await expect.poll(() => fixture.posts().length).toBe(1);
    await expect(pending(page)).toContainText('Unconfirmed');

    if (committed) {
      const other = session();
      other.user.id = '30000000-0000-4000-8000-000000000099';
      const [header, , signature] = other.accessToken.split('.');
      other.accessToken = [header, Buffer.from(JSON.stringify({
        sub: other.user.id, active_shop_id: SHOP_A, exp: Math.floor(Date.now() / 1000) + 3600,
      })).toString('base64url'), signature].join('.');
      await page.evaluate(({ key, value }) => localStorage.setItem(key, JSON.stringify(value)),
        { key: AUTH_STORAGE, value: other });
      await page.reload();
      await expect(page.getByRole('heading', { name: 'New Sale' })).toBeVisible();
      await expect(pending(page)).toHaveCount(0);
      await page.evaluate(({ key, value }) => localStorage.setItem(key, JSON.stringify(value)),
        { key: AUTH_STORAGE, value: session() });
    }

    fixture.state.phase = 'expired';
    await page.reload();
    await expect(page.getByRole('alert')).toContainText(EXPIRED);
    await expect(pending(page)).toBeVisible();
    await page.screenshot({ path: info.outputPath('pending-under-expiry-mobile.png'), fullPage: true, animations: 'disabled' });
    if (!committed) {
      await pending(page).getByRole('button', { name: 'Check status' }).click();
      await expect.poll(() => fixture.log.filter((event) => event.method === 'lookup').at(-1)?.status).toBe(404);
      await expect(pending(page)).toBeVisible();
    }

    await pending(page).getByRole('button', { name: 'Retry same checkout' }).click();
    await expect(pending(page)).toHaveCount(0);
    if (committed) {
      const confirmation = page.getByRole('dialog', { name: 'Sale Recorded' });
      await expect(confirmation).toContainText(SALE.invoiceNumber);
      await confirmation.screenshot({ path: info.outputPath('replayed-invoice-mobile.png'), animations: 'disabled' });
    } else {
      await expect(page.getByRole('alert')).toContainText(EXPIRED);
      await expect(page.locator('app-cart-table app-quantity-field input')).toHaveValue('1');
    }
    const [first, retry] = fixture.posts();
    expect(fixture.posts()).toHaveLength(2);
    expect(retry).toMatchObject({ key: first.key, hash: first.hash, status: committed ? 201 : 403 });
    expect(fixture.committed.size).toBe(committed ? 1 : 0);
  });
}

test('P04-05 lost checkout stays recoverable after membership denial and reload', async ({ page }) => {
  await page.goto(server.origin + '/sales/new');
  await addLine(page, 'A11Y');
  await expect(page.getByTestId('checkout-economics')).toContainText('50.00');
  fixture.state.mode = 'drop-after-commit';
  await submit(page).click();
  await expect(pending(page)).toContainText('Unconfirmed');
  expect(fixture.committed.size).toBe(1);

  fixture.state.membershipDenied = true;
  await pending(page).getByRole('button', { name: 'Retry same checkout' }).click();
  await expect.poll(() => fixture.posts().at(-1)?.code).toBe('Till.Forbidden');
  await expect(pending(page)).toContainText('Unconfirmed');

  fixture.state.membershipDenied = false;
  await page.reload();
  await expect(pending(page)).toContainText('Unconfirmed');
  await expect(pending(page).getByText('Current cart')).toBeVisible();
  await submit(page).click();
  await expect(page.getByText('This cart is already a checkout awaiting confirmation.', { exact: false })).toBeVisible();
  await pending(page).getByRole('button', { name: 'Check status' }).click();
  await expect(page.getByRole('dialog', { name: 'Sale Recorded' })).toContainText(SALE.invoiceNumber);

  const [first, retry] = fixture.posts();
  expect(fixture.posts()).toHaveLength(2);
  expect(retry).toMatchObject({ key: first.key, hash: first.hash, status: 403 });
  expect(fixture.log.filter((event) => event.method === 'lookup'))
    .toEqual([{ method: 'lookup', key: first.key, status: 200 }]);
  expect(fixture.committed.size).toBe(1);
});

test('P04-05 verification outage keeps the cart and gives its own recovery guidance', async ({ page }, info) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(server.origin + '/sales/new');
  await addLine(page, 'A11Y');
  await expect(page.getByTestId('checkout-economics')).toContainText('50.00');
  fixture.state.phase = 'outage';
  await submit(page).click();
  await expect(page.getByRole('alert')).toContainText(OUTAGE);
  await expect(page.getByText(EXPIRED)).toHaveCount(0);
  await expect(pending(page)).toHaveCount(0);
  await expect(submit(page)).toBeDisabled();
  expect(fixture.posts().map(({ status }) => status)).toEqual([503]);
  await page.screenshot({ path: info.outputPath('verification-outage-mobile.png'), fullPage: true, animations: 'disabled' });

  fixture.state.phase = 'verified';
  await page.getByRole('button', { name: 'Check again', exact: true }).click();
  await expect(submit(page)).toBeEnabled();
  await expect(page.getByText(OUTAGE, { exact: false })).toHaveCount(0);
});

test('P04-05 refreshed authority replaces an expired POST message with the current decision', async ({ page }) => {
  await page.goto(server.origin + '/sales/new');
  await addLine(page, 'A11Y');
  await expect(page.getByTestId('checkout-economics')).toContainText('50.00');
  const checkAgain = page.getByRole('button', { name: 'Check again', exact: true });
  const quantity = page.locator('app-cart-table app-quantity-field input');
  const expired = page.getByText(EXPIRED, { exact: false });
  const outage = page.getByText(OUTAGE, { exact: false });

  fixture.state.phase = 'expired';
  await submit(page).click();
  await expect(expired).toHaveCount(1);
  await expect(outage).toHaveCount(0);
  await expect(quantity).toHaveValue('1');
  await expect(submit(page)).toBeDisabled();

  fixture.state.phase = 'outage';
  await checkAgain.click();
  await expect(outage).toHaveCount(1);
  await expect(expired).toHaveCount(0);
  await expect(quantity).toHaveValue('1');
  await expect(submit(page)).toBeDisabled();

  fixture.state.phase = 'verified';
  await checkAgain.click();
  await expect(submit(page)).toBeEnabled();
  await expect(expired).toHaveCount(0);
  await expect(outage).toHaveCount(0);
  await expect(quantity).toHaveValue('1');
  expect(fixture.posts().map(({ status }) => status)).toEqual([403]);
});

test('P04-05 observe-only expiry leaves checkout available', async ({ page }) => {
  fixture.state.phase = 'observe-only';
  await page.goto(server.origin + '/sales/new');
  await addLine(page, 'A11Y');
  await expect(page.getByText(EXPIRED)).toHaveCount(0);
  await expect(page.getByTestId('checkout-economics')).toContainText('50.00');
  await submit(page).click();
  await expect(page.getByRole('dialog', { name: 'Sale Recorded' })).toContainText(SALE.invoiceNumber);
  expect(fixture.committed.size).toBe(1);
});
