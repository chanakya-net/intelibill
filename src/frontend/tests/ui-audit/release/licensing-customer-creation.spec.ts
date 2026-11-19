import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test, type Locator, type Page, type Route } from '@playwright/test';

import { customerDirectoryResponse } from '../support/party-directory-mock';
import { accessibilityFixtures, CUSTOMER } from './accessibility-localization-fixtures';
import { session, SHOP_A, startPurchaseReceivingServer } from './purchase-receiving-server';

const BUILD = resolve(__dirname, '../../../dist/INVENTORY/browser');
const MESSAGES = {
  pending: 'This shop is waiting for trading access.',
  expired: 'This shop’s trading access has ended.',
  restricted: 'Trading is restricted for this shop.',
  outage: 'Trading access could not be verified.',
};
const REFUSALS = {
  pending: { status: 403, code: 'Licence.PendingActivation' },
  expired: { status: 403, code: 'Licence.Expired' },
  restricted: { status: 403, code: 'Licence.TradingRestricted' },
  outage: { status: 503, code: 'Licence.VerificationUnavailable' },
};
const DESKTOP = { width: 1440, height: 900 };
const MOBILE = { width: 390, height: 844 };
const draft = { name: 'Prepared customer', phone: '+919811111111' };

type Denial = keyof typeof REFUSALS;
type Phase = 'verified' | 'observe-only' | Denial;
type Event = { method: string; path: string; status: number; code?: string };

test.use({ serviceWorkers: 'block', reducedMotion: 'reduce' });

const json = (route: Route, body: unknown, status = 200) =>
  route.fulfill({ status, json: body, headers: { 'Cache-Control': 'no-store' } });

async function installFixture(page: Page) {
  await accessibilityFixtures(page);
  const state = { hint: 'verified' as Phase, writeDenial: null as Denial | null };
  const customers = [{ ...CUSTOMER }];
  const requests: Event[] = [];
  const respond = (route: Route, body: unknown, status: number, code?: string) => {
    requests.push({
      method: route.request().method(), path: new URL(route.request().url()).pathname, status, code,
    });
    return json(route, body, status);
  };

  await page.route('**/api/licensing/shops/*/trading-authority', (route) => {
    if (state.hint === 'verified')
      return respond(route, {
        schemaVersion: 1, shopId: SHOP_A, enforced: true,
        authorityVerifiedAt: new Date().toISOString(),
        validUntil: new Date(Date.now() + 3600_000).toISOString(),
      }, 200);
    const enforced = state.hint !== 'observe-only';
    const denial = REFUSALS[enforced ? state.hint as Denial : 'expired'];
    return respond(route, { ...denial, enforced }, denial.status, denial.code);
  });
  await page.route(/\/api\/customers\/directory(?:\?|$)/, (route) =>
    respond(route, customerDirectoryResponse(customers, new URL(route.request().url())), 200));
  await page.route('**/api/customers', (route) => {
    if (route.request().method() !== 'POST') return route.fallback();
    if (state.writeDenial) {
      const denial = REFUSALS[state.writeDenial];
      return respond(route, denial, denial.status, denial.code);
    }

    const payload = route.request().postDataJSON();
    const customer = {
      ...CUSTOMER, ...payload, customerId: 'fixture-' + (customers.length + 1), outstandingDue: 0,
    };
    customers.push(customer);
    return respond(route, customer, 201);
  });

  return {
    state, requests, posts: () => requests.filter(({ method }) => method === 'POST'),
    writes: () => customers.length - 1,
  };
}

let server: Awaited<ReturnType<typeof startPurchaseReceivingServer>>;
let fixture: Awaited<ReturnType<typeof installFixture>>;

test.beforeEach(async ({ page }) => {
  server = await startPurchaseReceivingServer(BUILD);
  fixture = await installFixture(page);
  await page.addInitScript((value) => {
    const key = 'inventory.auth.session.local';
    if (!localStorage.getItem(key)) localStorage.setItem(key, JSON.stringify(value));
  }, session());
});

test.afterEach(async ({ page, browser }, info) => {
  mkdirSync(info.outputDir, { recursive: true });
  writeFileSync(info.outputPath('fixture-customer-http.json'), JSON.stringify({
    source: 'Local API fixtures with production-built merchant assets',
    viewport: page.viewportSize(), browser: browser.version(), shop: SHOP_A,
    requests: fixture.requests, fixtureWrites: fixture.writes(),
  }, null, 2) + '\n');
  await server.close();
});

const addCustomer = (container: Page | Locator) =>
  container.getByRole('button', { name: /Add Customer$/ });
const notice = (container: Page | Locator) =>
  container.locator('app-definition-access-notice').getByRole('alert');
const refresh = (form: Locator) =>
  form.getByRole('button', { name: 'Check again', exact: true });
const fields = (form: Locator) => ({
  name: form.locator('input[formControlName="name"]'),
  phone: form.locator('input[formControlName="phoneNumber"]'),
});

async function openForm(page: Page, viewport: typeof DESKTOP) {
  await page.setViewportSize(viewport);
  await page.goto(server.origin + '/customers');
  await expect(addCustomer(page)).toBeEnabled();
  await addCustomer(page).click();
  const form = page.getByRole('dialog');
  await fields(form).name.fill(draft.name);
  await fields(form).phone.fill(draft.phone);
  return form;
}

async function assertRefusedDraft(form: Locator, denial: Denial) {
  await expect(notice(form)).toContainText(MESSAGES[denial]);
  await expect(addCustomer(form)).toBeDisabled();
  await expect(fields(form).name).toHaveValue(draft.name);
  await expect(fields(form).phone).toHaveValue(draft.phone);
  expect(fixture.writes()).toBe(0);
}

test('P04-08b pending shop keeps the directory readable and Add Customer disabled', async ({ page }, info) => {
  fixture.state.hint = 'pending';
  await page.setViewportSize(DESKTOP);
  await page.goto(server.origin + '/customers');
  await expect(notice(page)).toContainText(MESSAGES.pending);
  await expect(addCustomer(page)).toBeDisabled();
  await expect(page.getByRole('row').getByText(CUSTOMER.name, { exact: true })).toBeVisible();
  expect(fixture.posts()).toHaveLength(0);
  await page.screenshot({ path: info.outputPath('pending-directory-desktop.png') });
});

test('P04-08b stale form retains values through expiry, outage and verified recovery', async ({ page }, info) => {
  const form = await openForm(page, DESKTOP);
  fixture.state.writeDenial = 'expired';
  await addCustomer(form).click();
  await assertRefusedDraft(form, 'expired');
  await page.screenshot({ path: info.outputPath('expired-customer-desktop.png') });

  fixture.state.hint = 'outage';
  await refresh(form).click();
  await assertRefusedDraft(form, 'outage');
  await page.screenshot({ path: info.outputPath('outage-customer-desktop.png') });

  fixture.state.hint = 'verified';
  fixture.state.writeDenial = null;
  await refresh(form).click();
  await expect(addCustomer(form)).toBeEnabled();
  await addCustomer(form).click();
  await expect(form).toHaveCount(0);
  await expect(page.getByRole('row').getByText(draft.name, { exact: true })).toBeVisible();
  await page.screenshot({ path: info.outputPath('created-after-verification-desktop.png') });
  expect(fixture.posts()).toMatchObject([
    { path: '/api/customers', status: 403, code: 'Licence.Expired' },
    { path: '/api/customers', status: 201 },
  ]);
  expect(fixture.writes()).toBe(1);
});

test('P04-08b mobile form retains the refused draft and observe-only permits creation', async ({ page }, info) => {
  const form = await openForm(page, MOBILE);
  fixture.state.writeDenial = 'restricted';
  await addCustomer(form).click();
  await assertRefusedDraft(form, 'restricted');
  await notice(form).scrollIntoViewIfNeeded();
  await page.screenshot({ path: info.outputPath('restricted-customer-mobile.png') });

  fixture.state.hint = 'observe-only';
  fixture.state.writeDenial = null;
  await refresh(form).click();
  await expect(addCustomer(form)).toBeEnabled();
  await addCustomer(form).click();
  await expect(form).toHaveCount(0);
  expect(fixture.posts()).toMatchObject([
    { path: '/api/customers', status: 403, code: 'Licence.TradingRestricted' },
    { path: '/api/customers', status: 201 },
  ]);
  expect(fixture.writes()).toBe(1);
});

test('P04-08b expired checkout accepts walk-in text without creating a customer', async ({ page }, info) => {
  fixture.state.hint = 'expired';
  await page.setViewportSize(DESKTOP);
  await page.goto(server.origin + '/sales/new');
  const alert = page.getByRole('alert').filter({ hasText: MESSAGES.expired });
  await expect(alert).toBeVisible();
  const checkoutCustomer = page.locator('app-sale-customer-section');
  const name = checkoutCustomer.getByRole('combobox');
  const phone = checkoutCustomer.locator('input[type="tel"]');
  const search = page.waitForResponse((response) => {
    const url = new URL(response.url());
    return url.pathname === '/api/customers/directory' && url.searchParams.get('search') === 'Unknown buyer';
  });
  await name.fill('Unknown buyer');
  await search;
  await phone.fill(draft.phone);
  await expect(name).toHaveValue('Unknown buyer');
  await expect(phone).toHaveValue(draft.phone);
  await expect(addCustomer(page)).toHaveCount(0);
  await expect(page.locator('app-cart-checkout-summary button').last()).toBeDisabled();
  expect(fixture.posts()).toHaveLength(0);
  await page.screenshot({ path: info.outputPath('expired-walk-in-checkout.png'), fullPage: true });
});
