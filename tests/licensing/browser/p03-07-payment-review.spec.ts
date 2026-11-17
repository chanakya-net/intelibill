import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { fillSecret, otp } from './p01-12-fixture';

function required(name: string) {
  const value = process.env['P0307_' + name];
  if (!value) throw new Error('Missing payment review fixture variable ' + name);
  return value;
}

const admin = required('ADMIN_WEB');
const merchant = required('MERCHANT_WEB');
const scratch = required('TEMPORARY');
const evidence = required('EVIDENCE_DIR');
const account = required('ACCOUNT');
const order = required('ORDER');
const queue = admin + '/payments/review?account=' + account;
const secrets = JSON.parse(readFileSync(join(scratch, 'sessions.json'), 'utf8'));

async function signInAdmin(page: Page) {
  await page.goto(admin + '/auth/sign-in');
  await enterAdminCredentials(page, secrets.adminTime);
  await expect(page.getByRole('link', { name: 'Review payment funds' })).toBeVisible();
}

async function enterAdminCredentials(page: Page, epoch: number) {
  await page.getByLabel('Email', { exact: true }).fill(secrets.admin);
  await fillSecret(page, 'Password', secrets.adminPassword);
  await fillSecret(page, 'Authenticator code', otp(secrets.seed, Math.floor(epoch / 30)));
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
}

async function signInMerchant(page: Page) {
  await page.goto(merchant + '/login');
  await page.locator('#identifier').fill(secrets.merchant);
  await page.locator('#password').evaluate((input: HTMLInputElement, password: string) => {
    input.value = password;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }, secrets.merchantPassword);
  const login = page.waitForResponse(r => new URL(r.url()).pathname === '/api/auth/login' && r.request().method() === 'POST');
  await page.locator('button[type=submit]').click();
  expect((await login).status()).toBe(200);
  await expect(page).not.toHaveURL(/\/login/);
}

test('Finance recovers one held payment and merchant sees acknowledged access', async ({ browser, page, context }) => {
  const merchantContext = await browser.newContext({ locale: 'en-IN', timezoneId: 'Asia/Kolkata' });
  const payer = await merchantContext.newPage();
  await signInMerchant(payer);
  await payer.goto(merchant + '/billing/accounts/' + account + '/checkout?order=' + order);
  await expect(payer.getByTestId('payment-review')).toContainText(/needs review/);
  await expect(payer.getByTestId('payment-start')).toHaveCount(0);
  await expect(payer.getByTestId('payment-simulator-capture')).toHaveCount(0);
  await expect(payer.getByRole('button', { name: 'Review a new quote' })).toHaveCount(0);
  await expect(payer.getByTestId('payment-review-check')).toBeVisible();
  await payer.screenshot({ path: join(evidence, 'merchant-paid-review.png'), fullPage: true });

  await signInAdmin(page);
  await page.goto(queue);
  await page.getByTestId('payment-review-case').filter({ hasText: 'Coverage revision changed' }).click();
  await expect(page.getByTestId('review-blocker')).toContainText('Coverage revision changed');
  await expect(page.getByText('Expected amount', { exact: true })).toBeVisible();
  await expect(page.getByRole('option', { name: 'Recover original accepted terms' })).toHaveCount(0);
  writeFileSync(join(scratch, 'held'), '');
  await expect.poll(() => existsSync(join(scratch, 'withdrawn'))).toBe(true);
  await page.getByRole('button', { name: 'Refresh case' }).click();
  await expect(page.getByTestId('review-term-preview')).toBeVisible();
  await page.getByLabel('Decision', { exact: true }).selectOption('RecoverAcceptedTerms');
  await page.getByLabel('Reason').fill('Cancellation withdrawn on the original package');
  await page.getByLabel('Evidence reference').fill('FIXTURE-ORIGINAL-RECEIPT');

  writeFileSync(join(scratch, 'age'), '');
  const aged = join(scratch, 'aged');
  await expect.poll(() => existsSync(aged)).toBe(true);
  const decisionPath = page.url();
  await page.getByRole('button', { name: 'Record finance decision' }).click();
  await expect(page.getByRole('alert')).toContainText('Sign in with MFA');
  const saved = await page.getByTestId('review-operation').textContent();
  await page.getByRole('link', { name: 'Sign in with MFA' }).click();
  await enterAdminCredentials(page, Number(readFileSync(aged, 'utf8')));
  await expect(page).toHaveURL(decisionPath);
  await expect(page.getByTestId('review-operation')).toHaveText(saved!);

  const decisions = '**/payment-reviews/*/decisions';
  let committed = 0;
  await page.route(decisions, async route => {
    committed = (await route.fetch()).status();
    await route.abort();
  });
  await page.getByRole('button', { name: 'Retry original decision' }).click();
  await expect(page.getByRole('alert')).toContainText('unresolved');
  expect(committed).toBe(201);
  const second = await context.newPage();
  await second.goto(page.url());
  await expect(second.getByTestId('review-operation')).toHaveText(saved!);

  await page.reload();
  await expect(page.getByTestId('review-operation')).toHaveText(saved!);
  await page.route('**/payment-reviews/*/operations/*', route => route.fulfill({ status: 404, body: '{}' }));
  await page.getByRole('button', { name: 'Check original decision' }).click();
  await expect(page.getByRole('alert')).toContainText('unresolved');
  await expect(page.getByTestId('review-operation')).toHaveText(saved!);
  await page.unroute(decisions);
  const replay = page.waitForResponse(response => response.url().endsWith('/decisions') && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Retry original decision' }).click();
  expect((await replay).status()).toBe(200);
  await expect(page.getByTestId('review-result')).toContainText('Original accepted terms recovered');
  await expect(page.getByTestId('review-operation')).toHaveCount(0);
  await page.screenshot({ path: join(evidence, 'finance-decision.png'), fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: join(evidence, 'finance-mobile.png'), fullPage: true });

  await payer.getByTestId('payment-review-check').click();
  await expect(payer.getByTestId('payment-applying')).toBeVisible();
  await expect(payer.getByTestId('payment-start')).toHaveCount(0);
  writeFileSync(join(scratch, 'applying'), '');
  await expect.poll(() => existsSync(join(scratch, 'delivered'))).toBe(true);
  await payer.reload();
  await expect(payer.getByTestId('payment-applied')).toBeVisible();
  await expect(payer.getByTestId('payment-start')).toHaveCount(0);
  await payer.setViewportSize({ width: 390, height: 844 });
  await payer.screenshot({ path: join(evidence, 'merchant-applied.png'), fullPage: true });
  await merchantContext.close();
});
