import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test, type Locator, type Page, type Route } from '@playwright/test';

import { supplierDirectoryResponse } from '../support/party-directory-mock';
import { accessibilityFixtures } from './accessibility-localization-fixtures';
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
const SUPPLIER = {
  supplierId: 'existing-supplier', name: 'Existing supplier',
  contactPersonName: null, contactPersonPhone: null, address: '42 MG Road',
  city: 'Bengaluru', state: 'Karnataka', pin: '560001',
  isSystem: false, isActive: true, isPreferred: false, balanceDue: 800,
};
const draft = {
  name: 'Prepared supplier', contactPersonName: 'Sales', contactPersonPhone: '+919811111111',
  address: '42 MG Road', city: 'Bengaluru', state: 'Karnataka', pin: '560001',
};

type Denial = keyof typeof REFUSALS;
type Phase = 'verified' | 'observe-only' | Denial;
type Event = { method: string; path: string; status: number; code?: string };

test.use({ serviceWorkers: 'block', reducedMotion: 'reduce' });

const json = (route: Route, body: unknown, status = 200) =>
  route.fulfill({ status, json: body, headers: { 'Cache-Control': 'no-store' } });

async function installFixture(page: Page) {
  await accessibilityFixtures(page);
  const state = { hint: 'verified' as Phase, writeDenial: null as Denial | 'busy' | null };
  const suppliers = [{ ...SUPPLIER }];
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
  await page.route(/\/api\/suppliers\/directory(?:\?|$)/, (route) =>
    respond(route, supplierDirectoryResponse(suppliers, new URL(route.request().url())), 200));
  await page.route('**/api/suppliers', (route) => {
    if (route.request().method() !== 'POST') return route.fallback();
    if (state.writeDenial) {
      const denial = state.writeDenial === 'busy' ? { status: 409, code: 'Licence.Busy' } : REFUSALS[state.writeDenial];
      return respond(route, denial, denial.status, denial.code);
    }

    const payload = route.request().postDataJSON();
    const supplier = {
      ...SUPPLIER, ...payload, supplierId: 'fixture-' + (suppliers.length + 1), balanceDue: 0,
    };
    suppliers.push(supplier);
    return respond(route, supplier, 201);
  });

  return {
    state, requests, posts: () => requests.filter(({ method }) => method === 'POST'),
    writes: () => suppliers.length - 1,
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
  writeFileSync(info.outputPath('fixture-supplier-http.json'), JSON.stringify({
    source: 'Local API fixtures with production-built merchant assets',
    viewport: page.viewportSize(), browser: browser.version(), shop: SHOP_A,
    requests: fixture.requests, fixtureWrites: fixture.writes(),
  }, null, 2) + '\n');
  await server.close();
});

const addSupplier = (container: Page | Locator) =>
  container.getByRole('button', { name: /Add Supplier$/ });
const notice = (container: Page | Locator) =>
  container.locator('app-definition-access-notice').getByRole('alert');
const refresh = (form: Locator) =>
  form.getByRole('button', { name: 'Check again', exact: true });
const field = (form: Locator, name: string) => form.locator(`input[formControlName="${name}"]`);
const checkbox = (form: Locator, name: 'isActive' | 'isPreferred') =>
  form.locator(`p-checkbox[formControlName="${name}"] input`);

async function openForm(page: Page, viewport: typeof DESKTOP) {
  await page.setViewportSize(viewport);
  await page.goto(server.origin + '/suppliers');
  await expect(addSupplier(page)).toBeEnabled();
  await addSupplier(page).click();
  const form = page.getByRole('dialog');
  for (const [key, value] of Object.entries(draft)) await field(form, key).fill(value);
  await checkbox(form, 'isPreferred').check();
  await checkbox(form, 'isActive').uncheck();
  return form;
}

async function assertRefusedDraft(form: Locator, denial: Denial) {
  await expect(notice(form)).toContainText(MESSAGES[denial]);
  await expect(addSupplier(form)).toBeDisabled();
  for (const [key, value] of Object.entries(draft)) await expect(field(form, key)).toHaveValue(value);
  await expect(checkbox(form, 'isPreferred')).toBeChecked();
  await expect(checkbox(form, 'isActive')).not.toBeChecked();
  expect(fixture.writes()).toBe(0);
}

for (const [layout, viewport] of [['desktop', DESKTOP], ['mobile', MOBILE]] as const) {
  test(`P04-08c pending shop keeps the directory readable and Add Supplier disabled on ${layout}`, async ({ page }, info) => {
    fixture.state.hint = 'pending';
    await page.setViewportSize(viewport);
    await page.goto(server.origin + '/suppliers');
    await expect(notice(page)).toContainText(MESSAGES.pending);
    await expect(addSupplier(page)).toBeDisabled();
    await expect(page.getByText(SUPPLIER.name, { exact: true }).filter({ visible: true })).toBeVisible();
    expect(fixture.posts()).toHaveLength(0);
    await page.screenshot({ path: info.outputPath(`pending-directory-${layout}.png`) });
  });
}

test('P04-08c stale form retains values through expiry, outage and verified recovery', async ({ page }, info) => {
  const form = await openForm(page, DESKTOP);
  fixture.state.writeDenial = 'expired';
  await addSupplier(form).click();
  await assertRefusedDraft(form, 'expired');
  await page.screenshot({ path: info.outputPath('expired-supplier-desktop.png') });

  fixture.state.hint = 'outage';
  await refresh(form).click();
  await assertRefusedDraft(form, 'outage');
  await page.screenshot({ path: info.outputPath('outage-supplier-desktop.png') });

  fixture.state.hint = 'verified';
  fixture.state.writeDenial = null;
  await refresh(form).click();
  await expect(addSupplier(form)).toBeEnabled();
  await addSupplier(form).click();
  await expect(form).toHaveCount(0);
  await expect(page.getByText(draft.name, { exact: true }).filter({ visible: true })).toBeVisible();
  await page.screenshot({ path: info.outputPath('created-after-verification-desktop.png') });
  expect(fixture.posts()).toMatchObject([
    { path: '/api/suppliers', status: 403, code: 'Licence.Expired' },
    { path: '/api/suppliers', status: 201 },
  ]);
  expect(fixture.writes()).toBe(1);
});

test('P04-08c mobile form retains the refused draft and observe-only permits creation', async ({ page }, info) => {
  const form = await openForm(page, MOBILE);
  fixture.state.writeDenial = 'busy';
  await addSupplier(form).click();
  await expect(addSupplier(form)).toBeEnabled();
  await expect(form.locator('.ib-overlay__error')).toBeVisible();
  await expect(field(form, 'name')).toHaveValue(draft.name);
  fixture.state.writeDenial = 'restricted';
  await addSupplier(form).click();
  await assertRefusedDraft(form, 'restricted');
  await notice(form).scrollIntoViewIfNeeded();
  await page.screenshot({ path: info.outputPath('restricted-supplier-mobile.png') });

  fixture.state.hint = 'observe-only';
  fixture.state.writeDenial = null;
  await refresh(form).click();
  await expect(addSupplier(form)).toBeEnabled();
  await addSupplier(form).click();
  await expect(form).toHaveCount(0);
  expect(fixture.posts()).toMatchObject([
    { path: '/api/suppliers', status: 409, code: 'Licence.Busy' },
    { path: '/api/suppliers', status: 403, code: 'Licence.TradingRestricted' },
    { path: '/api/suppliers', status: 201 },
  ]);
  expect(fixture.writes()).toBe(1);
});
