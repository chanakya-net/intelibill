import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test, type Page, type Route } from '@playwright/test';

import { accessibilityFixtures } from './accessibility-localization-fixtures';
import { session, SHOP_A, startPurchaseReceivingServer } from './purchase-receiving-server';

const BUILD = resolve(__dirname, '../../../dist/INVENTORY/browser');
const EXPIRED = 'This shop’s trading access has ended.';
const SECURITY = 'Security suspension is enforced for this shop.';
const INELIGIBLE = /must keep the original payee/;
const DESKTOP = { width: 1440, height: 900 };
const MOBILE = { width: 390, height: 844 };
const DATE = '2026-11-10';

type Phase = 'verified' | 'expired' | 'security';
type Event = { method: string; path: string; status: number; code?: string };

test.use({ serviceWorkers: 'block', reducedMotion: 'reduce' });

const json = (route: Route, body: unknown, status = 200) =>
  route.fulfill({ status, json: body, headers: { 'Cache-Control': 'no-store' } });

async function installFixture(page: Page) {
  await accessibilityFixtures(page);
  const state = { phase: 'verified' as Phase };
  const original = {
    id: 'expense-1', shopId: SHOP_A, categoryId: 'cat-1', categoryName: 'Rent', amount: 100,
    paidTo: 'Landlord', description: null, expenseDate: DATE, actorUserId: 'u1', isVoided: false,
    originalExpenseId: null, createdAt: `${DATE}T09:00:00Z`, treatment: 'Operating',
  };
  const expenses = [original];
  const requests: Event[] = [];
  const respond = (route: Route, body: unknown, status: number, code?: string) => {
    requests.push({ method: route.request().method(), path: new URL(route.request().url()).pathname, status, code });
    return json(route, body, status);
  };

  await page.route('**/api/licensing/shops/*/trading-authority', (route) => {
    if (state.phase === 'verified')
      return respond(route, {
        schemaVersion: 1, shopId: SHOP_A, enforced: true, authorityVerifiedAt: new Date().toISOString(),
        validUntil: new Date(Date.now() + 3600_000).toISOString(),
      }, 200);
    const code = state.phase === 'security' ? 'Licence.SecuritySuspended' : 'Licence.Expired';
    return respond(route, { status: 403, code, enforced: true }, 403, code);
  });
  await page.route('**/api/expenses/categories', (route) => respond(route, [{ id: 'cat-1', name: 'Rent' }], 200));
  await page.route(/\/api\/expenses(?:\?.*)?$/, (route) => {
    if (route.request().method() === 'POST') {
      // A new independent expense is refused once the licence has ended.
      if (state.phase === 'expired')
        return respond(route, { status: 403, code: 'Licence.Expired', title: 'Licence.Expired' }, 403, 'Licence.Expired');
      const body = route.request().postDataJSON();
      const created = { ...original, ...body, id: `expense-${expenses.length + 1}` };
      expenses.push(created);
      return respond(route, created, 201);
    }
    return respond(route, { items: expenses, totalCount: expenses.length, pageNumber: 1, pageSize: 20 }, 200);
  });
  await page.route('**/api/expenses/expense-1', (route) => respond(route, original, 200));
  await page.route('**/api/expenses/expense-1/correct', (route) => {
    const body = route.request().postDataJSON();
    if (state.phase === 'security')
      return respond(route, { status: 403, code: 'Licence.SecuritySuspended' }, 403, 'Licence.SecuritySuspended');
    // Mirrors the server rule: same date, never above the original amount, while restricted.
    if (state.phase === 'expired' && (body.amount > original.amount || body.expenseDate !== original.expenseDate))
      return respond(route, { status: 409, code: 'Expense.CorrectionSourceIneligible', title: 'Expense.CorrectionSourceIneligible' }, 409, 'Expense.CorrectionSourceIneligible');
    const corrected = { ...original, ...body, id: `expense-${expenses.length + 1}`, originalExpenseId: original.id };
    expenses.push(corrected);
    return respond(route, corrected, 200);
  });

  return { state, requests, posts: () => requests.filter(({ method }) => method === 'POST'), writes: () => expenses.length - 1 };
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
  writeFileSync(info.outputPath('fixture-expense-http.json'), JSON.stringify({
    source: 'Local API fixtures with production-built merchant assets (not real PostgreSQL)',
    viewport: page.viewportSize(), browser: browser.version(), shop: SHOP_A,
    requests: fixture.requests, fixtureWrites: fixture.writes(),
  }, null, 2) + '\n');
  await server.close();
});

const record = (page: Page) => page.getByRole('button', { name: /Record Expense$/ });

test('P04-09 expired shop disables Record expense but keeps correction available', async ({ page }, info) => {
  fixture.state.phase = 'expired';
  await page.setViewportSize(DESKTOP);
  await page.goto(server.origin + '/expenses');
  await expect(page.getByRole('alert').filter({ hasText: EXPIRED })).toBeVisible();
  await expect(record(page)).toBeDisabled();
  await expect(page.getByRole('button', { name: /Correct$/ }).first()).toBeEnabled();
  expect(fixture.posts()).toHaveLength(0);
  await page.screenshot({ path: info.outputPath('expired-expenses-desktop.png') });
});

test('P04-09 refused larger correction explains the conflict and an eligible one posts', async ({ page }, info) => {
  await page.setViewportSize(MOBILE);
  await page.goto(server.origin + '/expenses');
  await expect(record(page)).toBeEnabled();
  fixture.state.phase = 'expired';
  await page.getByRole('button', { name: /Correct$/ }).first().click();
  const dialog = page.getByRole('dialog');
  const amount = dialog.locator('p-inputnumber[formcontrolname="amount"] input');
  await amount.fill('150');
  await dialog.getByRole('button', { name: /^Correct|Save|Submit/ }).last().click();
  await expect(dialog.getByText(INELIGIBLE)).toBeVisible();
  expect(fixture.writes()).toBe(0);
  await page.screenshot({ path: info.outputPath('refused-larger-correction-mobile.png') });

  await amount.fill('60');
  await dialog.getByRole('button', { name: /^Correct|Save|Submit/ }).last().click();
  await expect(dialog).toHaveCount(0);
  expect(fixture.writes()).toBe(1);
  expect(fixture.posts()).toMatchObject([
    { path: '/api/expenses/expense-1/correct', status: 409, code: 'Expense.CorrectionSourceIneligible' },
    { path: '/api/expenses/expense-1/correct', status: 200 },
  ]);
});

test('P04-09 stale form keeps its draft when the server refuses a new expense at expiry', async ({ page }, info) => {
  await page.setViewportSize(DESKTOP);
  await page.goto(server.origin + '/expenses');
  await record(page).click();
  const dialog = page.getByRole('dialog');
  await dialog.locator('input[formcontrolname="paidTo"]').fill('Landlord');
  await dialog.locator('p-inputnumber[formcontrolname="amount"] input').fill('90');
  await dialog.locator('p-select[formcontrolname="categoryName"] input').fill('Rent');
  fixture.state.phase = 'expired';
  await dialog.getByRole('button', { name: /Record Expense$/ }).click();
  await expect(page.getByRole('alert').filter({ hasText: EXPIRED }).first()).toBeVisible();
  await expect(dialog.getByRole('button', { name: /Record Expense$/ })).toBeDisabled();
  await expect(dialog.locator('input[formcontrolname="paidTo"]')).toHaveValue('Landlord');
  expect(fixture.writes()).toBe(0);
  await page.screenshot({ path: info.outputPath('stale-record-form-desktop.png') });
  await expect(dialog.locator('p-inputnumber[formcontrolname="amount"] input')).toHaveValue('90');
  expect(fixture.posts()).toHaveLength(1);
  await dialog.getByRole('button', { name: /Cancel/i }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page.getByRole('button', { name: /Correct$/ }).first()).toBeEnabled();
});

test('authority refresh keeps security suspension in force until release', async ({ page }, info) => {
  await page.goto(server.origin + '/expenses');
  const correct = page.getByRole('button', { name: /Correct$/ }).first();
  await correct.click();
  const dialog = page.getByRole('dialog');
  const cancel = dialog.getByRole('button', { name: /Cancel/i });
  const submit = dialog.getByRole('button', { name: /^Correct|Save|Submit/ }).last();
  const eligibility = dialog.getByTestId('restricted-correction-hint');
  const notice = page.locator('app-definition-access-notice');
  const refresh = () => {
    const response = page.waitForResponse((res) => res.url().endsWith('/trading-authority'));
    return Promise.all([response, notice.getByRole('button', { name: 'Check again' }).click()]);
  };

  fixture.state.phase = 'security';
  await submit.click();
  await expect(dialog.getByText(SECURITY)).toBeVisible();
  await expect(eligibility).toHaveCount(0);
  await expect(submit).toBeDisabled();
  await cancel.click();

  const [suspended] = await refresh();
  expect(await suspended.json()).toMatchObject({ code: 'Licence.SecuritySuspended', enforced: true });
  await expect(notice.getByRole('alert')).toHaveText(SECURITY);
  await correct.click();
  await expect(dialog.getByText(SECURITY)).toBeVisible();
  await expect(eligibility).toHaveCount(0);
  await expect(submit).toBeDisabled();
  expect(fixture.posts()).toHaveLength(1);
  await page.screenshot({ path: info.outputPath('security-suspension-after-refresh.png') });
  await cancel.click();

  fixture.state.phase = 'expired';
  const [released] = await refresh();
  expect(await released.json()).toMatchObject({ code: 'Licence.Expired', enforced: true });
  await expect(notice.getByRole('alert')).toHaveText(EXPIRED);
  await expect(record(page)).toBeDisabled();
  await correct.click();
  await expect(dialog.getByText(SECURITY)).toHaveCount(0);
  await expect(eligibility).toBeVisible();
  await expect(submit).toBeEnabled();
});

test('Hindi guidance and correction conflict are readable', async ({ page }) => {
  fixture.state.phase = 'expired';
  await page.addInitScript(() => localStorage.setItem('inventory.preferences.language', 'hi-IN'));
  await page.goto(server.origin + '/expenses');
  await page.getByRole('button', { name: 'सुधारें' }).first().click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByTestId('restricted-correction-hint')).toContainText('मौजूदा खर्च');
  await dialog.locator('p-inputnumber[formcontrolname="amount"] input').fill('150');
  await dialog.getByRole('button', { name: 'खर्च सुधारें' }).click();
  await expect(dialog.getByText(/मूल प्राप्तकर्ता/)).toBeVisible();
});
