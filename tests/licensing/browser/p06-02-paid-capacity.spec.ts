import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page } from '@playwright/test';

function required(name: string): string {
  const value = process.env['P0602_' + name];
  if (!value) throw new Error('Missing capacity quote fixture variable ' + name);
  return value;
}

const origin = required('MERCHANT_WEB');
const account = required('ACCOUNT');
const term = required('TERM');
const target = required('TARGET');
const evidence = required('EVIDENCE_DIR');
const amount = required('AMOUNT');
const review = required('OUTCOME') === 'review';
const desktop = { width: 1440, height: 900 };
const mobile = { width: 390, height: 844 };
const status = origin + '/billing/accounts/' + account + '/status';
const quotes = '/subscriptions/' + term + '/capacity-change-quotes';

async function open(browser: Browser, viewport = desktop) {
  const context = await browser.newContext({ viewport, locale: 'en-IN', timezoneId: 'Asia/Kolkata',
    isMobile: viewport === mobile, hasTouch: viewport === mobile });
  const page = await context.newPage();
  await page.goto(origin + '/login');
  await page.locator('#identifier').fill(required('OWNER'));
  await page.locator('#password').evaluate((input: HTMLInputElement, password) => {
    input.value = password;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }, required('PASSWORD'));
  const response = page.waitForResponse(result => new URL(result.url()).pathname === '/api/auth/login'
    && result.request().method() === 'POST');
  await page.locator('button[type=submit]').click();
  expect((await response).status()).toBe(200);
  await expect(page).not.toHaveURL(/\/login/);
  return page;
}

async function noOverflow(page: Page) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
}

function card(page: Page) {
  return page.locator(`[data-testid="account-term"]:has([data-term-id="${term}"])`);
}

test('owner recovers verified capacity payment and its original deadline', async ({ browser }) => {
  const page = await open(browser, review ? mobile : desktop);
  await page.goto(status);
  await card(page).getByTestId('capacity-change-open').click();
  await card(page).getByTestId('capacity-change-plan').selectOption(target);
  await card(page).getByTestId('capacity-change-capacity').fill('4');
  const created = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith(quotes));
  await card(page).getByTestId('capacity-change-submit').click();
  const quote = await (await created).json();
  await expect(card(page).getByTestId('capacity-paid-through')).toHaveText('29 Feb 2028, 10:00:00 IST');
  await card(page).getByTestId('capacity-purchase').click();
  await expect(page.getByTestId('checkout-total')).toHaveText(amount);
  await expect(page).toHaveURL(new RegExp(quote.quoteId + '$'));
  await page.getByRole('checkbox').check();
  await page.getByRole('button', { name: /accept/i }).click();
  await expect(page.getByTestId('checkout-accepted')).toBeVisible();
  await page.getByTestId('payment-start').click();
  await expect(page.getByTestId('payment-simulator-notice')).toBeVisible();
  await page.getByTestId('payment-simulator-capture').click();
  await expect(page.getByTestId('payment-confirmed')).toHaveText('Payment confirmed');
  await expect(page.getByTestId('capacity-pending')).toBeVisible();
  await page.reload();
  await expect(page.getByTestId('capacity-pending')).toBeVisible();
  writeFileSync(join(evidence, 'captured.txt'), quote.quoteId);
  const decision = page.getByTestId(review ? 'payment-review' : 'payment-applied');
  await expect(decision).toBeVisible({ timeout: 40_000 });
  await page.goto(status);
  await expect(card(page).getByTestId('paid-through')).toHaveText('29 Feb 2028, 10:00 IST');
  await card(page).getByTestId('capacity-order').click();
  await expect(decision).toBeVisible();
  await page.getByTestId('confirmation-list').click();
  await expect(page.getByTestId('confirmation-download')).toBeVisible();
  if (review) await expect(page.getByTestId('confirmation-request-refund')).toBeVisible();
  await expect(page.getByTestId('payment-confirmed')).toBeVisible();
  await expect(page.getByTestId('payment-start')).toHaveCount(0);
  await expect(page.getByTestId('payment-simulator')).toHaveCount(0);
  await noOverflow(page);
  await page.screenshot({ path: join(evidence, 'paid-capacity.png'), fullPage: true });
});
