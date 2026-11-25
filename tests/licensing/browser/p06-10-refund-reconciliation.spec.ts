import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { fillSecret, otp } from './p01-12-fixture';

const env = (name: string) => process.env['P0610_' + name]!;
const scratch = env('TEMPORARY');
const request = env('REQUEST');
const account = env('ACCOUNT');
const secrets = JSON.parse(readFileSync(join(scratch, 'sessions.json'), 'utf8'));
const signal = (name: string) => writeFileSync(join(scratch, name), '');
const wait = (name: string) => expect.poll(() => existsSync(join(scratch, name)), { timeout: 30000 }).toBe(true);

async function views(page: Page, prefix: string) {
  for (const width of [1440, 390]) {
    await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    const sidebar = page.locator('.adm-sidebar');
    if (width === 390 && await sidebar.count())
      await expect.poll(() => sidebar.evaluate(el => el.getBoundingClientRect().right <= 1)).toBe(true);
    await page.screenshot({ path: join(env('EVIDENCE_DIR'), prefix + '-' + width + '.png'), fullPage: true,
      mask: [page.locator('input[type=email],input[type=password],.adm-user-email')] });
  }
}

async function approve(page: Page) {
  await page.goto(env('ADMIN_WEB') + '/auth/sign-in');
  await page.getByLabel('Email', { exact: true }).fill(secrets.admin);
  await fillSecret(page, 'Password', secrets.adminPassword);
  await fillSecret(page, 'Authenticator code', otp(secrets.seed, Math.floor(secrets.adminTime / 30)));
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).not.toHaveURL(/sign-in/);
  await page.goto(env('ADMIN_WEB') + '/payments/refunds/' + account + '/' + request + '/approve');
  await page.locator('#refund-approval-amount').fill('45.00');
  await page.locator('#refund-approval-reason').fill('Duplicate coverage');
  await page.locator('input[value=EndAt]').check();
  await page.locator('#refund-approval-effective').fill('2028-02-01T10:00');
  await page.getByTestId('refund-approval-preview').click();
  await page.locator('#refund-approval-confirm').check();
  await page.getByTestId('refund-approval-submit').click();
  await expect(page.getByTestId('refund-approval-receipt')).toBeVisible();
  signal('approved');
}

async function merchant(page: Page) {
  await page.goto(env('MERCHANT_WEB') + '/login');
  await page.locator('#identifier').fill(secrets.merchant);
  await page.locator('#password').evaluate((input: HTMLInputElement, value: string) => {
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }, secrets.merchantPassword);
  await page.locator('button[type=submit]').click();
  await expect(page).not.toHaveURL(/\/login/);
  await page.goto(env('MERCHANT_WEB') + '/billing/accounts/' + account + '/refunds');
  const row = page.locator('[data-refund-id="' + request + '"]');
  await expect(row).toHaveAttribute('data-status', 'Confirmed');
  await expect(row).toContainText('45.00');
  await expect(row).toContainText('60.00');
  await expect(row).toContainText('Applied');
  await expect(page.locator('[data-refund-id="' + env('FAILED') + '"]')).toHaveAttribute('data-status', 'Failed');
  await views(page, 'merchant');
}

test('@recovery original provider resolves the lost response', async ({ browser }) => {
  const context = await browser.newContext({ locale: 'en-IN', timezoneId: 'Asia/Kolkata' });
  const page = await context.newPage();
  await approve(page);
  await wait('lost');
  await page.reload();
  await expect(page.getByTestId('refund-provider-outcome')).toHaveText('Unknown');
  const checked = page.waitForResponse(r => r.url().endsWith('/reconcile') && r.request().method() === 'POST');
  await page.getByRole('button', { name: 'Check original provider' }).click();
  const response = await checked;
  expect(response.status()).toBe(200);
  expect(response.headers()['cache-control']).toContain('no-store');
  await views(page, 'finance-unknown');
  signal('rechecked');
  await wait('settled');
  await page.reload();
  await expect(page.getByTestId('refund-provider-outcome')).toHaveText('Confirmed');
  await views(page, 'finance-confirmed');
  await merchant(await context.newPage());
  await context.close();
});
