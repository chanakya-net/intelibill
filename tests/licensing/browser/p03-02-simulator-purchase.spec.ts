import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page } from '@playwright/test';

function required(name: string): string {
  const value = process.env['P0302_' + name];
  if (!value) throw new Error('Missing simulator purchase fixture variable ' + name);
  return value;
}

const origin = required('MERCHANT_WEB');
const account = required('ACCOUNT');
const order = required('ORDER');
const evidence = required('EVIDENCE_DIR');
const total = required('TOTAL');
const shops = ['Purchase Central', 'Purchase East', 'Purchase North'];
const desktop = { width: 1440, height: 900 };
const mobile = { width: 390, height: 844 };
const route = origin + '/billing/accounts/' + account + '/checkout?order=' + order;

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
  await page.goto(route);
  await expect(page.getByTestId('checkout-total')).toHaveText(total);
  await expect(page.getByTestId('checkout-covered-shop')).toHaveCount(shops.length);
  for (const shop of shops) await expect(page.getByTestId('checkout-covered-shop').getByText(shop)).toBeVisible();
  return page;
}

function observe(name: string, facts: Record<string, unknown>) {
  writeFileSync(join(evidence, name + '-observations.json'), JSON.stringify(facts, null, 2));
}

async function noOverflow(page: Page) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
}

test('@pending a browser success report alone stays pending verification', async ({ browser }) => {
  const page = await open(browser);
  await expect(page.getByTestId('checkout-accepted')).toBeVisible();
  await expect(page.getByRole('button', { name: /pay now|instalment|split/i })).toHaveCount(0);
  const started = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/payment/attempts'));
  await page.getByRole('button', { name: 'Continue to payment' }).click();
  expect((await started).status()).toBe(201);
  await expect(page.getByTestId('payment-simulator-notice')).toHaveText(/Development only/);
  await page.screenshot({ path: join(evidence, 'pending-simulator-desktop.png'), fullPage: true });
  // Reloading mid-request would abort the claim; a proof-free recheck of an unpaid order stays payable by design.
  const claim = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/verify'));
  await page.getByRole('button', { name: 'Return without capturing' }).click();
  expect((await claim).status()).toBe(200);
  await expect(page.getByTestId('payment-pending')).toBeVisible();
  await expect(page.getByTestId('payment-confirmed')).toHaveCount(0);
  await page.reload();
  await expect(page.getByTestId('payment-pending')).toBeVisible();
  await expect(page.getByTestId('payment-simulator')).toHaveCount(0);
  await page.screenshot({ path: join(evidence, 'pending-desktop.png'), fullPage: true });
  observe('pending', { pendingAfterClaim: true, pendingAfterReload: true, simulatorWithdrawn: true });
});

test('@capture server verification confirms payment and shows applying access', async ({ browser }) => {
  const page = await open(browser);
  await expect(page.getByTestId('payment-pending')).toBeVisible();
  await page.getByRole('button', { name: 'Check again' }).click();
  await expect(page.getByTestId('payment-confirmed')).toHaveText('Payment confirmed');
  await expect(page.getByTestId('payment-applying')).toHaveText('Applying access to 0 of 3 shops');
  await expect(page.getByTestId('payment-term')).toContainText('IST');
  await noOverflow(page);
  await page.screenshot({ path: join(evidence, 'applying-desktop.png'), fullPage: true });
  const phone = await open(browser, mobile);
  await expect(phone.getByTestId('payment-applying')).toHaveText('Applying access to 0 of 3 shops');
  await noOverflow(phone);
  await phone.screenshot({ path: join(evidence, 'applying-mobile.png'), fullPage: true });
  observe('capture', { confirmed: true, applying: '0 of 3' });
});

test('@applied merchant acknowledgement clears applying access', async ({ browser }) => {
  const page = await open(browser);
  await expect(page.getByTestId('payment-confirmed')).toHaveText('Payment confirmed');
  await expect(page.getByTestId('payment-applied')).toHaveText('Access applied to all 3 shops');
  await expect(page.getByTestId('payment-applying')).toHaveCount(0);
  await page.screenshot({ path: join(evidence, 'applied-desktop.png'), fullPage: true });
  observe('applied', { applied: '3 of 3' });
});
