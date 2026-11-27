import { existsSync, renameSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Page, type Response } from '@playwright/test';
import { merchant, merchantSession } from './entitlement-browser-fixture';

const evidence = process.env['P0418_BROWSER_EVIDENCE']!;
const code = process.env['P0418_SERVICE_CODE']!;
const service = 'P04 Acceptance Repair';
const message = 'Trading is restricted for this shop.';
const submit = (page: Page) => page.locator('app-cart-checkout-summary button').last();
const cart = (page: Page) => page.locator('app-cart-table');
const confirmation = (page: Page) => page.getByRole('dialog', { name: 'Sale Recorded' });

async function addLine(page: Page) {
  const search = page.locator('app-batch-search-bar input').first();
  await search.fill(code);
  await search.press('Enter');
  await expect(cart(page)).toContainText(service);
  await expect(page.getByTestId('checkout-economics')).toContainText('50.00');
  await expect(submit(page)).toBeEnabled();
}

async function postSale(page: Page) {
  const response = page.waitForResponse((candidate) => new URL(candidate.url()).pathname === '/api/sales'
    && candidate.request().method() === 'POST');
  await submit(page).click();
  return response;
}

async function postedSale(page: Page, response: Response, token: string) {
  expect(response.status()).toBe(201);
  const { idempotencyKey } = response.request().postDataJSON();
  const lookup = await page.request.get(merchant + '/api/sales/by-idempotency-key/' + encodeURIComponent(idempotencyKey),
    { headers: { Authorization: 'Bearer ' + token } });
  expect(lookup.status()).toBe(200);
  return lookup.json();
}

function record(phase: string, data: object) {
  const path = join(evidence, 'observations-' + phase + '.json');
  writeFileSync(path + '.tmp', JSON.stringify(data, null, 2));
  renameSync(path + '.tmp', path);
}

async function screenshot(page: Page, phase: string) {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.screenshot({ path: join(evidence, phase + '-desktop.png'), animations: 'disabled', fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: join(evidence, phase + '-mobile.png'), animations: 'disabled', fullPage: true });
  await page.setViewportSize({ width: 1440, height: 900 });
}

test('shipping merchant checkout preserves cart and history during a trading suspension', async ({ page, context, browser }) => {
  const token = await merchantSession(page, 'owner');
  await page.goto(merchant + '/sales/new');
  await addLine(page);
  const first = await postSale(page);
  const { saleId, invoiceNumber } = await postedSale(page, first, token);
  await expect(confirmation(page)).toContainText(invoiceNumber);
  await screenshot(page, 'operating');
  await page.goto(merchant + '/sales/new');
  await addLine(page);
  record('operating', { signedIn: true, chromium: browser.version(), checkout: first.status(), saleId, invoiceNumber });

  await expect.poll(() => existsSync(join(evidence, 'restricted.ready')), { timeout: 30_000 }).toBe(true);
  const rejected = await postSale(page);
  expect(rejected.status()).toBe(403);
  await expect(page.getByText(message, { exact: false })).toBeVisible();
  await expect(cart(page)).toContainText(service);
  await expect(submit(page)).toBeDisabled();
  await screenshot(page, 'restricted');

  // Keep the staged checkout open while checking historical routes in the same signed-in browser context.
  const history = await context.newPage();
  await history.goto(merchant + '/sales');
  await expect(history.getByText(invoiceNumber, { exact: true }).first()).toBeVisible();
  await history.goto(merchant + '/sales/' + saleId + '/print');
  await expect(history.getByText(invoiceNumber, { exact: false }).first()).toBeVisible();
  await history.close();
  record('restricted', { checkout: rejected.status(), cartRetained: true,
    message, salesList: true, invoicePrint: true });

  await expect.poll(() => existsSync(join(evidence, 'released.ready')), { timeout: 30_000 }).toBe(true);
  await page.getByRole('button', { name: 'Check again', exact: true }).click();
  await expect(page.getByText(message, { exact: false })).toHaveCount(0);
  await expect(submit(page)).toBeEnabled();
  await expect(cart(page)).toContainText(service);
  const second = await postSale(page);
  const released = await postedSale(page, second, token);
  expect(released.invoiceNumber).not.toBe(invoiceNumber);
  await expect(confirmation(page)).toContainText(released.invoiceNumber);
  await screenshot(page, 'released');
  record('released', { checkout: second.status(), saleId: released.saleId, invoiceNumber: released.invoiceNumber });
});
