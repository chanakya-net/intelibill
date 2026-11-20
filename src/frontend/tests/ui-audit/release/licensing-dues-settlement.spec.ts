import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test, type Locator, type Page, type Route } from '@playwright/test';

import { supplierDirectoryResponse } from '../support/party-directory-mock';
import { accessibilityFixtures, CUSTOMER, SALE } from './accessibility-localization-fixtures';
import { session, startPurchaseReceivingServer } from './purchase-receiving-server';

const BUILD = resolve(__dirname, '../../../dist/INVENTORY/browser');
const SUPPLIER = {
  supplierId: 'supplier-settlement',
  name: 'Fixture Supplier',
  isActive: true,
  isSystem: false,
  isPreferred: false,
  balanceDue: 30.5,
  city: 'Bengaluru',
};
type Post = { operationKey: string; amount: number; paymentDate: string };
const json = (route: Route, body: unknown, status = 200) =>
  route.fulfill({ status, json: body, headers: { 'Cache-Control': 'no-store' } });

test.setTimeout(25_000);
test.use({
  serviceWorkers: 'block',
  reducedMotion: 'reduce',
  viewport: { width: 1440, height: 900 },
});

async function fixtures(page: Page) {
  await accessibilityFixtures(page);
  const state = {
    customerLimit: 30.5 as number | null,
    supplierLimit: 30.5 as number | null,
    staleSupplier: false,
    exposeOutcome: false,
  };
  const posts: Post[] = [];
  const outcomes = new Map<string, object>();
  const lookups: { key: string; status: number }[] = [];
  const previews: { partyId: string | null; date: string | null; limit: number | null }[] = [];

  await page.route(/\/api\/payments\/balances(?:\?|$)/, (route) => {
    const params = new URL(route.request().url()).searchParams;
    const partyId = params.get('partyId');
    const limit = partyId === CUSTOMER.customerId ? state.customerLimit : state.supplierLimit;
    previews.push({ partyId, date: params.get('paymentDate'), limit });
    return json(route, { remainingSettlementDue: limit });
  });
  await page.route(/\/api\/suppliers\/directory(?:\?|$)/, (route) =>
    json(route, supplierDirectoryResponse([SUPPLIER], new URL(route.request().url()))),
  );
  await page.route(`**/api/suppliers/${SUPPLIER.supplierId}/ledger`, (route) => json(route, []));
  await page.route(`**/api/suppliers/${SUPPLIER.supplierId}`, (route) => json(route, SUPPLIER));
  await page.route(`**/api/suppliers/${SUPPLIER.supplierId}/payments`, (route) => {
    const body = route.request().postDataJSON() as Post;
    posts.push(body);
    if (state.staleSupplier) {
      state.supplierLimit = 8.25;
      return json(route, { code: 'Payment.SettlementLimitExceeded', status: 409 }, 409);
    }
    return json(route, {
      id: 'supplier-entry',
      supplierId: SUPPLIER.supplierId,
      amount: body.amount,
    });
  });
  await page.route(`**/api/customers/${CUSTOMER.customerId}/account`, (route) => {
    const posted = posts.find((payment) => outcomes.has(payment.operationKey));
    const outstandingDue = posted ? 0 : 30.5;
    return json(route, {
      ...CUSTOMER,
      outstandingDue,
      openingBalance: 0,
      closingBalance: outstandingDue,
      sales: [{ ...SALE, dueAmount: 30.5, currentOutstandingAmount: 30.5 }],
      ledgerEntries: [],
      paymentHistory: [],
      unallocatedCredit: posted?.amount ?? 0,
      receipts: posted
        ? [
            {
              receiptId: 'receipt-settlement',
              amount: posted.amount,
              method: 'Cash',
              externalReference: null,
              businessDate: posted.paymentDate,
              isLegacy: false,
              isReversed: false,
              allocatedAmount: 0,
              unallocatedAmount: posted.amount,
              ledgerEntryId: 'entry-settlement',
            },
          ]
        : [],
    });
  });
  await page.route(`**/api/customers/${CUSTOMER.customerId}/payments`, (route) => {
    const body = route.request().postDataJSON() as Post;
    posts.push(body);
    const original = outcomes.get(body.operationKey);
    if (original) return json(route, original);
    outcomes.set(body.operationKey, {
      entryId: 'entry-settlement',
      receiptId: 'receipt-settlement',
      amount: body.amount,
      entryDate: body.paymentDate,
      runningBalance: 0,
    });
    state.customerLimit = 0;
    return route.abort('failed');
  });
  await page.route('**/api/financial-operations/customer-payment/*', (route) => {
    const key = new URL(route.request().url()).pathname.split('/').at(-1)!;
    const outcome = state.exposeOutcome ? outcomes.get(key) : undefined;
    lookups.push({ key, status: outcome ? 200 : 404 });
    return outcome
      ? json(route, {
          operationKey: key,
          operationType: 'customer-payment',
          targetId: CUSTOMER.customerId,
          response: outcome,
        })
      : json(route, { code: 'Operation.NotFound' }, 404);
  });
  return { state, posts, outcomes, lookups, previews };
}

let server: Awaited<ReturnType<typeof startPurchaseReceivingServer>>;
let api: Awaited<ReturnType<typeof fixtures>>;

test.beforeEach(async ({ page }) => {
  server = await startPurchaseReceivingServer(BUILD);
  api = await fixtures(page);
  await page.addInitScript((fixtureSession) => {
    const key = 'inventory.auth.session.local';
    if (!localStorage.getItem(key)) localStorage.setItem(key, JSON.stringify(fixtureSession));
  }, session());
});

test.afterEach(async ({ browser }, info) => {
  mkdirSync(info.outputDir, { recursive: true });
  writeFileSync(
    info.outputPath('fixture-evidence.json'),
    JSON.stringify(
      {
        source: 'Local HTTP fixtures with production-built assets',
        browser: browser.version(),
        previews: api.previews,
        posts: api.posts,
        lookups: api.lookups,
        committedCustomerOutcomes: [...api.outcomes],
      },
      null,
      2,
    ) + '\n',
  );
  await server.close();
});

async function openCustomer(page: Page) {
  await page.locator('.customer-name-link:visible, .customer-card__identity:visible').first().click();
  return page.getByRole('dialog', { name: 'Customer Account', exact: true });
}

async function enterAmount(form: Locator, amount: string) {
  const input = form.locator('[formcontrolname="amount"] input');
  await input.fill(amount);
  await input.blur();
}

test('shows remaining due and recovers the original payment after reload', async ({
  page,
}, info) => {
  await page.goto(server.origin + '/customers');
  const dialog = await openCustomer(page);
  const submit = dialog.getByRole('button', { name: /Submit Payment$/ });
  const limit = dialog.getByTestId('settlement-limit');
  await expect(limit).toContainText(/Remaining eligible due.*30\.50/);
  await enterAmount(dialog, '30.51');
  await expect(submit).toBeDisabled();
  await page.screenshot({ path: info.outputPath('customer-limit.png') });
  await enterAmount(dialog, '30.5');
  await expect(submit).toBeEnabled();
  await submit.click();
  await expect(dialog.getByText('Payment not confirmed', { exact: true })).toBeVisible();

  await page.reload();
  const reopened = await openCustomer(page);
  const status = reopened.getByRole('button', { name: 'Check status', exact: true });
  await status.click();
  await expect(reopened.getByText(/No confirmed result yet/)).toBeVisible();
  await page.screenshot({ path: info.outputPath('customer-pending-after-reload.png') });
  api.state.exposeOutcome = true;
  await status.click();
  await expect(
    reopened.getByText(/Payment confirmed\. The ledger has been refreshed/),
  ).toBeVisible();
  await expect(limit).toContainText(/Remaining eligible due.*0\.00/);
  await expect(reopened.getByRole('row').filter({ hasText: 'Posted' })).toHaveCount(1);
  await enterAmount(reopened, '1');
  await expect(submit).toBeDisabled();
  await page.screenshot({ path: info.outputPath('customer-confirmed-at-zero.png') });

  expect(api.posts).toHaveLength(1);
  expect(api.outcomes.size).toBe(1);
  expect(api.lookups).toEqual(
    [404, 200].map((status) => ({ key: api.posts[0].operationKey, status })),
  );
});

test('both supplier forms show the limit and refresh after a stale refusal', async ({
  page,
}, info) => {
  await page.goto(server.origin + '/suppliers');
  await page.getByRole('button', { name: /Make Payment$/ }).click();
  const overlay = page.getByRole('dialog', { name: 'Payment form', exact: true });
  await expect(overlay.getByTestId('settlement-limit')).toContainText(/30\.50/);
  await enterAmount(overlay, '31');
  const submit = overlay.getByRole('button', { name: /Record Payment$/ });
  await expect(submit).toBeDisabled();
  await enterAmount(overlay, '20');
  api.state.staleSupplier = true;
  await submit.click();
  await expect(overlay.getByText(/The remaining due changed/)).toBeVisible();
  await expect(overlay.getByTestId('settlement-limit')).toContainText(/8\.25/);
  await expect(overlay.locator('[formcontrolname="amount"] input')).toHaveValue('20');
  await expect(submit).toBeDisabled();
  await page.screenshot({ path: info.outputPath('supplier-stale-cap.png') });
  await overlay.getByRole('button', { name: 'Close', exact: true }).click();

  await page.locator('.supplier-name-link').getByText(SUPPLIER.name).click();
  const detail = page.getByRole('dialog', { name: 'Ledger Entries', exact: true });
  await expect(detail.getByTestId('settlement-limit')).toContainText(/8\.25/);
  await enterAmount(detail, '9');
  await expect(detail.getByRole('button', { name: /Record Payment$/ })).toBeDisabled();
  await page.screenshot({ path: info.outputPath('supplier-ledger-limit.png') });
  expect(api.posts).toHaveLength(1);
});

test('an uncapped balance keeps ordinary advance entry available', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  api.state.customerLimit = null;
  api.state.supplierLimit = null;
  await page.goto(server.origin + '/customers');
  const dialog = await openCustomer(page);
  await enterAmount(dialog, '100');
  await expect(dialog.getByRole('button', { name: /Submit Payment$/ })).toBeEnabled();
  await expect(dialog.getByTestId('settlement-limit')).toHaveCount(0);
});
