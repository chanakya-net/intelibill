import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { fillSecret, otp } from './p01-12-fixture';

function required(name: string): string {
  const value = process.env['P0608_' + name];
  if (!value) throw new Error('Missing refund-request fixture variable ' + name);
  return value;
}

const merchant = required('MERCHANT_WEB');
const admin = required('ADMIN_WEB');
const scratch = required('TEMPORARY');
const evidence = required('EVIDENCE_DIR');
const account = required('ACCOUNT');
const order = required('ORDER');
const confirmation = required('CONFIRMATION');
const secrets = JSON.parse(readFileSync(join(scratch, 'sessions.json'), 'utf8'));
const reason = 'Duplicate shop setup before go-live';

async function checkViewports(page: Page, name: string) {
  for (const [label, width, height] of [['desktop', 1440, 900], ['mobile', 390, 844]] as const) {
    await page.setViewportSize({ width, height });
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.screenshot({ path: join(evidence, `${name}-${label}.png`), fullPage: true });
  }
}

async function signInMerchant(page: Page) {
  await page.goto(merchant + '/login');
  await page.locator('#identifier').fill(secrets.merchant);
  await page.locator('#password').evaluate((input: HTMLInputElement, value: string) => {
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }, secrets.merchantPassword);
  const login = page.waitForResponse(r => new URL(r.url()).pathname === '/api/auth/login' && r.request().method() === 'POST');
  await page.locator('button[type=submit]').click();
  expect((await login).status()).toBe(200);
  await expect(page).not.toHaveURL(/\/login/);
}

async function signInAdmin(page: Page) {
  await page.goto(admin + '/auth/sign-in');
  await page.getByLabel('Email', { exact: true }).fill(secrets.admin);
  await fillSecret(page, 'Password', secrets.adminPassword);
  await fillSecret(page, 'Authenticator code', otp(secrets.seed, Math.floor(Number(secrets.adminTime) / 30)));
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Refund requests', exact: true })).toBeVisible();
}

test('member requests refund without changing access and billing operator sees intake', async ({ browser }) => {
  const merchantContext = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'en-IN', timezoneId: 'Asia/Kolkata' });
  const payer = await merchantContext.newPage();
  await signInMerchant(payer);

  await payer.goto(`${merchant}/billing/accounts/${account}/checkout?order=${order}`);
  await payer.getByTestId('confirmation-list').click();
  await expect(payer.getByTestId('confirmation-request-refund')).toBeVisible();
  await payer.getByTestId('confirmation-request-refund').click();

  await expect(payer.getByTestId('refund-request-notice')).toContainText('does not approve a refund');
  await expect(payer.getByTestId('refund-target-confirmation')).toHaveText(confirmation);
  await payer.getByTestId('refund-request-amount').fill('45.00');
  await payer.getByTestId('refund-request-reason').fill(reason);

  const createdPromise = payer.waitForResponse(
    r => r.request().method() === 'POST' && r.url().endsWith(`/api/billing/accounts/${account}/refund-requests`),
  );
  await payer.getByTestId('refund-request-submit').click();
  const createdResponse = await createdPromise;
  expect(createdResponse.status()).toBe(200);
  expect(createdResponse.headers()['cache-control']).toContain('no-store');
  const created = await createdResponse.json();
  expect(created.status).toBe('Requested');
  expect(created.amountPaise).toBe(4500);

  await expect(payer.getByTestId('refund-request-row')).toHaveCount(1);
  const firstId = await payer.getByTestId('refund-request-row').first().getAttribute('data-refund-id');
  expect(firstId).toBe(created.refundRequestId);
  await expect(payer.getByTestId('refund-request-status').first()).toHaveText('Requested');
  await expect(payer.getByTestId('refund-request-status-detail').first()).toContainText('not approved');
  await checkViewports(payer, 'merchant-refund-request');

  const reloadList = payer.waitForResponse(
    r => r.request().method() === 'GET' && r.url().includes(`/api/billing/accounts/${account}/refund-requests`),
  );
  await payer.reload();
  expect((await reloadList).status()).toBe(200);
  await expect(payer.getByTestId('refund-request-row')).toHaveCount(1);
  expect(await payer.getByTestId('refund-request-row').first().getAttribute('data-refund-id')).toBe(firstId);
  await merchantContext.close();

  const adminContext = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'en-IN', timezoneId: 'Asia/Kolkata' });
  const operator = await adminContext.newPage();
  await signInAdmin(operator);
  await operator.getByRole('link', { name: 'Refund requests', exact: true }).click();
  await operator.getByTestId('refund-intake-account').fill(account);
  const intakeResponse = operator.waitForResponse(
    r => r.request().method() === 'GET' && r.url().includes(`/api/admin/accounts/${account}/refund-requests`),
  );
  await operator.getByTestId('refund-intake-search').click();
  const intake = await intakeResponse;
  expect(intake.status()).toBe(200);
  expect(intake.headers()['cache-control']).toContain('no-store');

  await expect(operator.getByTestId('refund-intake-row')).toHaveCount(1);
  expect(await operator.getByTestId('refund-intake-row').first().getAttribute('data-refund-id')).toBe(firstId);
  await expect(operator.getByTestId('refund-intake-status').first()).toHaveText('Requested');
  await expect(operator.getByTestId('refund-intake-reason').first()).toHaveText(reason);
  await expect(operator.getByTestId('refund-intake-provider').first()).toContainText('development');
  await checkViewports(operator, 'admin-refund-intake');
  await adminContext.close();

  writeFileSync(join(evidence, 'refund-request-id.txt'), firstId!);
  writeFileSync(join(evidence, 'browser-observations.json'), JSON.stringify({
    refundRequestId: firstId,
    accountId: account,
    orderId: order,
    confirmationId: confirmation,
    status: 'Requested',
    amountPaise: 4500,
    singleRowAfterReload: true,
    adminIntakeObserved: true,
    boundary: 'Development simulator receipt fixture and shipping local builds only; no live payment provider verification.',
  }, null, 2));
});
