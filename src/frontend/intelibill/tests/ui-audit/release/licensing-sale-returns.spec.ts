import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test, type Page, type Route } from '@playwright/test';

import { accessibilityFixtures, SALE } from './accessibility-localization-fixtures';
import { session, SHOP_A, startPurchaseReceivingServer } from './purchase-receiving-server';

const BUILD = resolve(__dirname, '../../../dist/INVENTORY/browser');
const SALE_PATH = '/api/sales/' + SALE.saleId;
const RETURN_PATH = SALE_PATH + '/returns';
const RETURN_NUMBER = 'RET-891-1';
const QUANTITY_CODE = 'SaleReturn.QuantityExceedsRemaining';

test.use({ serviceWorkers: 'block', reducedMotion: 'reduce' });

const json = (route: Route, body: unknown, status = 200) =>
  route.fulfill({ status, json: body, headers: { 'Cache-Control': 'no-store' } });

async function installFixture(page: Page, role: 'Owner' | 'Staff') {
  await accessibilityFixtures(page);
  const state = { remaining: 2, recorded: false };
  const log: { method: string; path: string; status: number; code?: string; quantity?: number }[] = [];
  const line = SALE.items[0];
  const storedReturn = {
    saleReturnId: 'return-891', returnNumber: RETURN_NUMBER, returnedAt: SALE.soldAt,
    totalRefundAmount: 50, dueReductionAmount: 0, payoutAmount: 50, payoutDestination: 'Refund',
    isVoided: false, voidedAt: null, voidReason: null,
    items: [{ saleReturnItemId: 'return-line-891', saleItemId: line.saleItemId, quantity: 1,
      condition: 'Restockable', approvedRefundAmount: 50 }],
  };
  const details = () => ({
    ...SALE, status: 'Posted', paidAmount: 100, dueAmount: 0,
    returns: state.recorded ? [storedReturn] : [],
    items: [{ ...line, returnedQuantity: 2 - state.remaining, returnableQuantity: state.remaining }],
  });

  await page.addInitScript((value) => {
    localStorage.setItem('inventory.auth.session.local', JSON.stringify(value));
  }, { ...session(), shops: session().shops.map((shop) => ({ ...shop, role })) });
  await page.route('**/api/licensing/shops/*/trading-authority', (route) => {
    const code = 'Licence.Expired';
    log.push({ method: 'GET', path: new URL(route.request().url()).pathname, status: 403, code });
    return json(route, { code, enforced: true }, 403);
  });
  await page.route(/\/api\/sales(?:\?.*)?$/, (route) => {
    if (route.request().method() === 'POST') {
      log.push({ method: 'POST', path: '/api/sales', status: 403, code: 'Licence.Expired' });
      return json(route, { code: 'Licence.Expired' }, 403);
    }
    return json(route, {
      items: [{ ...details(), itemCount: 1, returnNumbers: state.recorded ? [RETURN_NUMBER] : [], status: 'paid' }],
      totalCount: 1, pageNumber: 1, pageSize: 30, summary: { periodSales: 100, invoiceCount: 1, refundAmount: 0 },
    });
  });
  await page.route('**' + SALE_PATH, (route) => json(route, details()));
  await page.route(/\/api\/sales\/sale-a11y\/returns(?:\/preview)?$/, (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const payload = request.postDataJSON();
    expect(request.method()).toBe('POST');
    expect(payload.items).toHaveLength(1);
    expect(payload.items[0].saleItemId).toBe(line.saleItemId);
    const quantity: number = payload.items[0].quantity;
    const event = { method: request.method(), path, quantity };
    if (quantity > state.remaining) {
      log.push({ ...event, status: 400, code: QUANTITY_CODE });
      return json(route, { title: 'Validation.Failed', errors: [{ code: QUANTITY_CODE }] }, 400);
    }
    log.push({ ...event, status: 200 });
    if (path === RETURN_PATH) {
      expect(role).toBe('Owner');
      expect(payload.payoutDestination).toBeTruthy();
      state.remaining -= quantity;
      state.recorded = true;
      return json(route, details());
    }
    const financial = role === 'Owner' ? {
      totalRefundAmount: 50, dueReductionAmount: 0, payoutAmount: 50,
      totalTaxableAmount: 50, totalTaxAmount: 0,
      customerBalanceBefore: null, customerBalanceAfter: null,
    } : null;
    return json(route, {
      saleId: SALE.saleId, hasFinancialAccess: financial !== null, financial, warnings: [],
      lines: [{ saleItemId: line.saleItemId, itemId: line.itemId, inventoryBatchId: line.inventoryBatchId,
        requestedQuantity: quantity, returnedQuantity: 2 - state.remaining, returnableQuantity: state.remaining,
        condition: 'Restockable', willRestock: true, financial: null }],
    });
  });
  return { state, log };
}

async function openReturn(page: Page) {
  await page.getByRole('button', { name: 'Return items' }).click();
  const dialog = page.locator('.return-preview-dialog');
  await dialog.getByRole('checkbox').first().click();
  await dialog.locator('.return-preview-stepper input').fill('1');
  await dialog.locator('.return-preview-control').filter({ hasText: 'Condition' }).getByRole('combobox').click();
  await page.getByRole('option', { name: 'Restockable' }).click();
  await dialog.getByRole('button', { name: 'Preview refund' }).click();
  await expect(dialog.getByText('Calculated effects')).toBeVisible();
  return dialog;
}

async function selectCash(page: Page) {
  await page.locator('.return-preview-payout').getByRole('combobox').click();
  await page.getByRole('option', { name: 'Cash', exact: true }).click();
}

let server: Awaited<ReturnType<typeof startPurchaseReceivingServer>>;
test.beforeEach(async () => { server = await startPurchaseReceivingServer(BUILD); });
test.afterEach(async () => { await server.close(); });

test('expired owner can return a prior invoice line and must refresh after competing consumption', async ({ page, browser }, info) => {
  const fixture = await installFixture(page, 'Owner');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(server.origin + '/sales');
  await page.getByRole('button', { name: 'View receipt' }).first().click();
  const sale = page.locator('.sale-detail-dialog');
  const invoiceTotal = sale.locator('.sale-summary-grand strong');
  await expect(invoiceTotal).toContainText('100.00');
  const totalBefore = await invoiceTotal.textContent();

  const first = await openReturn(page);
  await selectCash(page);
  await first.getByRole('button', { name: 'Record return' }).click();
  await expect(first).toBeHidden();
  await expect(sale.getByText(RETURN_NUMBER, { exact: true })).toBeVisible();
  await expect(invoiceTotal).toHaveText(totalBefore!);
  expect(fixture.state.remaining).toBe(1);

  const stale = await openReturn(page);
  await selectCash(page);
  await expect(stale.locator('.return-preview-stepper input')).toHaveAttribute('max', '1');
  fixture.state.remaining = 0;
  await stale.getByRole('button', { name: 'Record return' }).click();
  await expect(stale.getByText('The remaining returnable quantity has changed.', { exact: false })).toBeVisible();
  await expect(stale.getByRole('button', { name: 'Record return' })).toBeDisabled();
  await expect(stale.getByRole('button', { name: 'Preview refund' })).toBeDisabled();
  await expect(stale.locator('.return-preview-stepper input')).toHaveValue('1');
  await page.screenshot({ path: info.outputPath('return-refusal.png') });
  await stale.getByRole('button', { name: 'Refresh sale' }).click();
  await expect(stale).toBeHidden();
  await expect(sale.getByText(RETURN_NUMBER, { exact: true })).toBeVisible();
  await expect(sale.getByRole('button', { name: 'Return items' })).toHaveCount(0);
  await expect(invoiceTotal).toHaveText(totalBefore!);
  expect(fixture.log.filter((event) => event.method === 'POST')).toMatchObject([
    { path: RETURN_PATH + '/preview', status: 200, quantity: 1 },
    { path: RETURN_PATH, status: 200, quantity: 1 },
    { path: RETURN_PATH + '/preview', status: 200, quantity: 1 },
    { path: RETURN_PATH, status: 400, code: QUANTITY_CODE, quantity: 1 },
  ]);
  await page.goto(server.origin + '/sales/new');
  await expect(page.getByRole('alert')).toContainText('This shop’s trading access has ended.');
  await expect(page.locator('app-cart-checkout-summary button').last()).toBeDisabled();
  expect(fixture.log).toContainEqual({
    method: 'GET', path: '/api/licensing/shops/' + SHOP_A + '/trading-authority', status: 403, code: 'Licence.Expired',
  });

  mkdirSync(info.outputDir, { recursive: true });
  writeFileSync(info.outputPath('fixture-http.json'), JSON.stringify({
    chromium: browser.version(), requests: fixture.log, originalInvoiceTotal: totalBefore,
  }, null, 2) + '\n');
});

test('expired staff can preview a prior return without financials or posting controls', async ({ page }, info) => {
  const fixture = await installFixture(page, 'Staff');
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(server.origin + '/sales');
  await page.getByRole('button', { name: 'View receipt' }).first().click();
  const dialog = await openReturn(page);
  await expect(dialog.getByText('Operational preview')).toBeVisible();
  await expect(dialog.getByRole('heading', { name: 'Refund impact' })).toHaveCount(0);
  await expect(dialog.locator('.return-preview-payout')).toHaveCount(0);
  await expect(dialog.getByRole('button', { name: 'Record return' })).toHaveCount(0);
  expect(fixture.log.filter((event) => event.method === 'POST')).toMatchObject([
    { path: RETURN_PATH + '/preview', status: 200, quantity: 1 },
  ]);
  await dialog.locator('.return-preview-header').scrollIntoViewIfNeeded();
  await page.screenshot({ path: info.outputPath('staff-operational-preview.png') });
});
