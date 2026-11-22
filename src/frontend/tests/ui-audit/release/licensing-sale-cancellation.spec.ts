import { writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test, type Page, type Route } from '@playwright/test';

import type { CancelSaleRequest } from '../../../src/app/features/sales/services/sale-cancellation.models';
import { CANCELLATION_SALE, CANCELLED_SALE } from '../fixtures/sale-invoices.fixture';
import { accessibilityFixtures } from './accessibility-localization-fixtures';
import { session, startPurchaseReceivingServer } from './purchase-receiving-server';

const BUILD = resolve(__dirname, '../../../dist/INVENTORY/browser');
const HINT = 'Only eligible prior receipts can be cancelled while trading is restricted.';
const SECURITY = 'Security suspension is enforced for this shop.';
const original = {
  ...CANCELLATION_SALE,
  soldAt: CANCELLATION_SALE.document!.issuedAt,
  businessDate: CANCELLATION_SALE.document!.businessDate,
};
const external = {
  ...original, saleId: 'sale-card', invoiceNumber: 'A/2627/000043', paymentMethod: 3,
  document: { ...original.document!, number: 'A/2627/000043' },
};
const refusal = {
  code: 'SaleCancellation.ExternalRefundWorkflowRequired',
  message: 'External refund is not recorded by this cancellation workflow.',
};
const cancelPath = '/api/sales/' + original.saleId + '/cancellations';
const recoveryPath = '/api/financial-operations/sale-cancellation/';

test.use({
  serviceWorkers: 'block',
  reducedMotion: 'reduce',
  viewport: { width: 1440, height: 1000 },
  actionTimeout: 10_000,
});

const json = (route: Route, body: unknown, status = 200) =>
  route.fulfill({ status, json: body, headers: { 'Cache-Control': 'no-store' } });

function preview(sale: typeof original, eligible: boolean) {
  return {
    saleId: sale.saleId,
    invoiceNumber: sale.invoiceNumber,
    document: sale.document,
    status: 'Posted',
    expectedVersion: sale.version,
    eligible,
    postingEnabled: true,
    sourceCompletionOnly: true,
    businessDate: original.businessDate,
    reasons: eligible ? [] : [refusal],
    lines: [{ saleItemId: 'sale-line', description: 'Frozen item', quantity: 2, uqc: 'NOS' }],
    totalAmount: 200,
    dueReductionAmount: 120,
    cashRefundAmount: eligible ? 80 : 0,
    externalRefundAmount: eligible ? 0 : 80,
    creditRestorationAmount: 0,
    requiresGoodsConfirmation: true,
    requiresCashRefundConfirmation: eligible,
  };
}

async function installSaleReads(page: Page, readCurrent: () => typeof CANCELLED_SALE) {
  await page.route(/\/api\/sales(?:\?.*)?$/, (route) =>
    json(route, {
      items: [readCurrent(), external].map((sale) => ({
        ...sale, status: sale.status === 'Cancelled' ? 'cancelled' : 'partiallyPaid',
        itemCount: sale.items.length, returnNumbers: [],
      })),
      totalCount: 2, pageNumber: 1, pageSize: 20,
      summary: { periodSales: 400, invoiceCount: 2, refundAmount: 0 },
    }));
  await page.route('**/api/sales/' + original.saleId, (route) => json(route, readCurrent()));
  await page.route('**/api/sales/' + external.saleId, (route) => json(route, external));
}

type FixtureRequest = {
  method: string;
  path: string;
  outcome: number | 'connection lost';
  payload?: CancelSaleRequest;
};

async function installFixtures(page: Page) {
  await accessibilityFixtures(page);
  await page.addInitScript((value) => {
    const key = 'inventory.auth.session.local';
    if (!localStorage.getItem(key)) localStorage.setItem(key, JSON.stringify(value));
  }, session());

  const state = { security: false, writes: 0 };
  let cancelled: typeof CANCELLED_SALE | null = null;
  const requests: FixtureRequest[] = [];
  const log = (route: Route, outcome: FixtureRequest['outcome'], payload?: CancelSaleRequest) => {
    requests.push({ method: route.request().method(), path: new URL(route.request().url()).pathname, outcome, payload });
  };

  await page.route('**/api/licensing/shops/*/trading-authority', (route) =>
    json(route, { status: 403, code: state.security ? 'Licence.SecuritySuspended' : 'Licence.Expired', enforced: true }, 403));
  await installSaleReads(page, () => cancelled ?? original);
  await page.route('**/api/sales/*/cancellation-preview', (route) => {
    const eligible = new URL(route.request().url()).pathname.includes(original.saleId);
    log(route, 200);
    return json(route, preview(eligible ? original : external, eligible));
  });
  await page.route('**/api/sales/*/cancellations', (route) => {
    const payload = route.request().postDataJSON() as CancelSaleRequest;
    if (new URL(route.request().url()).pathname !== cancelPath) {
      log(route, 409, payload);
      return json(route, refusal, 409);
    }
    if (!requests.some((entry) => entry.path === cancelPath)) {
      log(route, 'connection lost', payload);
      return route.abort('failed');
    }
    if (state.security) {
      log(route, 403, payload);
      return json(route, { code: 'Licence.SecuritySuspended' }, 403);
    }
    log(route, 200, payload);
    state.writes++;
    cancelled = {
      ...CANCELLED_SALE,
      soldAt: original.soldAt,
      businessDate: original.businessDate,
      cancellation: { ...CANCELLED_SALE.cancellation!, reason: payload.reason },
    };
    return json(route, cancelled.cancellation);
  });
  await page.route('**' + recoveryPath + '*', (route) => {
    log(route, state.security ? 403 : 404);
    return state.security ? json(route, { code: 'Licence.SecuritySuspended' }, 403)
      : json(route, { code: 'Operation.NotFound' }, 404);
  });
  return { state, requests };
}

async function openCancellation(page: Page, invoice: string) {
  await page.getByRole('row').filter({ hasText: invoice }).getByRole('button', { name: /view receipt/i }).click();
  await page.getByRole('button', { name: /Cancel sale$/ }).click();
  return page.getByTestId('sale-cancellation-dialog');
}

async function verifyIneligibleReceipt(page: Page, screenshotPath: string) {
  const refused = await openCancellation(page, external.invoiceNumber);
  await expect(refused.getByText(HINT)).toBeVisible();
  await expect(refused.getByText(refusal.code)).toBeVisible();
  await expect(refused.getByRole('button', { name: 'Confirm cancellation' })).toBeDisabled();
  await page.screenshot({ animations: 'disabled', path: screenshotPath });
}

async function sendEligibleCancellation(page: Page, screenshotPath: string) {
  await page.reload();
  const dialog = await openCancellation(page, original.invoiceNumber);
  await expect(dialog.getByText(HINT)).toBeVisible();
  await expect(dialog).toContainText('120.00');
  await expect(dialog).toContainText('80.00');
  const submit = dialog.getByRole('button', { name: 'Confirm cancellation' });
  await dialog.locator('textarea').fill('Duplicate online receipt');
  const [goods, cash] = await dialog.getByRole('checkbox').all();
  await goods.check();
  await expect(submit).toBeDisabled();
  await cash.check();
  await expect(submit).toBeEnabled();
  await page.screenshot({ animations: 'disabled', path: screenshotPath });
  await submit.click();
  await expect(dialog).toContainText('The outcome is unknown.');
}

async function expectShopRestriction(page: Page) {
  await expect(page.getByTestId('shop-data-restricted')).toHaveText(SECURITY);
  await expect(page.getByTestId('sale-cancellation-dialog')).toHaveCount(0);
  await expect(page.locator('.sale-detail-shell')).toHaveCount(0);
}

async function reopenAfterRelease(page: Page) {
  const [authority] = await Promise.all([
    page.waitForResponse((response) => new URL(response.url()).pathname.endsWith('/trading-authority')),
    page.evaluate(() => window.dispatchEvent(new Event('online'))),
  ]);
  expect(authority.status()).toBe(403);
  expect(await authority.json()).toMatchObject({ code: 'Licence.Expired', enforced: true });
  await expect(page.getByTestId('shop-data-restricted')).toHaveCount(0);
  const pending = await openCancellation(page, original.invoiceNumber);
  await expect(pending).toContainText('Duplicate online receipt');
  return pending.getByRole('button', { name: 'Retry same operation', exact: true });
}

function verifySameKey(requests: FixtureRequest[], posts: FixtureRequest[]) {
  expect(posts.map(({ outcome }) => outcome)).toEqual(['connection lost', 403, 200]);
  const [first, denied, retried] = posts;
  expect(first.payload?.operationKey).toBeTruthy();
  expect(denied.payload).toEqual(first.payload);
  expect(retried.payload).toEqual(first.payload);
  expect(requests.filter(({ path }) => path.endsWith('/cancellation-preview'))).toHaveLength(2);
  expect(requests.filter(({ path }) => path === recoveryPath + first.payload?.operationKey)
    .map(({ outcome }) => outcome)).toEqual([404, 403]);
}

test('restricted receipt eligibility and same-key cancellation recovery', async ({ page, browser }, info) => {
  const server = await startPurchaseReceivingServer(BUILD);
  const { state, requests } = await installFixtures(page);
  const posts = () => requests.filter((entry) => entry.path.endsWith('/cancellations'));
  try {
    await page.goto(server.origin + '/sales');
    await verifyIneligibleReceipt(page, info.outputPath('restricted-ineligible.png'));
    expect(posts()).toHaveLength(0);

    await sendEligibleCancellation(page, info.outputPath('restricted-eligible.png'));
    expect(state.writes).toBe(0);

    await page.reload();
    const pending = await openCancellation(page, original.invoiceNumber);
    await expect(pending).toContainText('Duplicate online receipt');
    const check = pending.getByRole('button', { name: 'Check status', exact: true });
    await Promise.all([
      page.waitForResponse((response) => new URL(response.url()).pathname.startsWith(recoveryPath) &&
        response.status() === 404),
      check.click(),
    ]);
    await expect(check).toBeEnabled();
    state.security = true;
    await Promise.all([
      page.waitForResponse((response) => new URL(response.url()).pathname.startsWith(recoveryPath) &&
        response.status() === 403),
      check.click(),
    ]);
    await expectShopRestriction(page);
    await page.screenshot({ animations: 'disabled', path: info.outputPath('security-recovery.png') });

    state.security = false;
    const retry = await reopenAfterRelease(page);
    state.security = true;
    await Promise.all([
      page.waitForResponse((response) => new URL(response.url()).pathname === cancelPath &&
        response.status() === 403),
      retry.click(),
    ]);
    await expectShopRestriction(page);

    state.security = false;
    await (await reopenAfterRelease(page)).click();
    const visibleCancellation = page.locator('.sale-cancellation-record');
    await expect(visibleCancellation).toContainText('SC-frozen');
    await expect(visibleCancellation).toContainText('Duplicate online receipt');
    expect(state.writes).toBe(1);
    verifySameKey(requests, posts());
    await page.screenshot({ animations: 'disabled', path: info.outputPath('restricted-cancelled.png') });
  } finally {
    writeFileSync(info.outputPath('fixture-cancellation-http.json'), JSON.stringify({
      source: 'Chromium with production-built merchant assets, local HTTP fixtures and browser IndexedDB',
      browser: browser.version(), viewport: page.viewportSize(), writes: state.writes, requests,
    }, null, 2) + '\n');
    await server.close();
  }
});
