import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page } from '@playwright/test';

function required(name: string): string {
  const value = process.env['P0304_' + name];
  if (!value) throw new Error('Missing Razorpay fixture variable ' + name);
  return value;
}

const origin = required('MERCHANT_WEB');
const account = required('ACCOUNT');
const order = required('ORDER');
const evidence = required('EVIDENCE_DIR');
const route = origin + '/billing/accounts/' + account + '/checkout?order=' + order;
const SCRIPT = 'https://checkout.razorpay.com/v1/checkout.js';

/**
 * Fixture stand-in for Razorpay's hosted script, served only for the fixed provider URL. Its checkout "pays" by
 * calling the page handler with a forged signature: the browser can only ever make a claim.
 */
const FIXTURE_SCRIPT = `
window.Razorpay = class {
  constructor(options) { this.options = options; window.__razorpayOptions = options; }
  open() {
    this.options.handler({
      razorpay_payment_id: 'pay_forged000001',
      razorpay_order_id: this.options.order_id,
      razorpay_signature: 'a'.repeat(64),
    });
  }
};`;

async function open(browser: Browser) {
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'en-IN', timezoneId: 'Asia/Kolkata' });
  const loads: string[] = [];
  await context.route(SCRIPT, async handler => {
    loads.push(handler.request().url());
    await handler.fulfill({ contentType: 'application/javascript', body: FIXTURE_SCRIPT });
  });
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
  return { page, loads };
}

function observe(name: string, facts: Record<string, unknown>) {
  writeFileSync(join(evidence, name + '-observations.json'), JSON.stringify(facts, null, 2));
}

async function noOverflow(page: Page) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
}

test('@forged a forged browser callback stays pending and grants nothing', async ({ browser }) => {
  const { page, loads } = await open(browser);
  await expect(page.getByTestId('checkout-accepted')).toBeVisible();
  const started = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/payment/attempts'));
  await page.getByRole('button', { name: 'Continue to payment' }).click();
  expect((await started).status()).toBe(201);
  await expect(page.getByTestId('payment-razorpay-notice')).toHaveText(/full amount in one payment/);
  await expect(page.getByTestId('payment-simulator')).toHaveCount(0);
  await page.screenshot({ path: join(evidence, 'razorpay-launcher-desktop.png'), fullPage: true });

  const verified = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/verify'));
  await page.getByTestId('payment-razorpay-pay').click();
  const answer = await verified;
  expect(answer.status()).toBe(200);
  expect(answer.request().postDataJSON().launcherProof.razorpay_signature).toBe('a'.repeat(64));
  await expect(page.getByTestId('payment-pending')).toBeVisible();
  await expect(page.getByTestId('payment-confirmed')).toHaveCount(0);
  await expect(page.getByTestId('payment-applying')).toHaveCount(0);
  const options = await page.evaluate(() => (window as unknown as { __razorpayOptions: Record<string, unknown> }).__razorpayOptions);
  await page.reload();
  await expect(page.getByTestId('payment-pending')).toBeVisible();
  await expect(page.getByTestId('payment-confirmed')).toHaveCount(0);
  await page.screenshot({ path: join(evidence, 'forged-pending-desktop.png'), fullPage: true });
  observe('forged', { scriptOrigins: loads, options, pendingAfterForgedCallback: true, confirmed: false });
});

test('@authorized provider authorization alone stays pending', async ({ browser }) => {
  const { page } = await open(browser);
  await expect(page.getByTestId('payment-pending')).toBeVisible();
  await page.getByRole('button', { name: 'Check again' }).click();
  await expect(page.getByTestId('payment-pending')).toBeVisible();
  await expect(page.getByTestId('payment-confirmed')).toHaveCount(0);
  await page.screenshot({ path: join(evidence, 'authorized-pending-desktop.png'), fullPage: true });
  observe('authorized', { pendingWhileAuthorized: true, confirmed: false });
});

test('@captured independent server-verified capture confirms and shows applying access', async ({ browser }) => {
  const { page } = await open(browser);
  await page.getByRole('button', { name: 'Check again' }).click();
  await expect(page.getByTestId('payment-confirmed')).toHaveText('Payment confirmed');
  await expect(page.getByTestId('payment-applying')).toContainText('Applying access to 0 of 1 shops');
  await expect(page.getByTestId('payment-term')).toContainText('IST');
  await noOverflow(page);
  await page.screenshot({ path: join(evidence, 'captured-applying-desktop.png'), fullPage: true });
  observe('captured', { confirmed: true, applying: '0 of 1' });
});

test('@applied merchant acknowledgement clears applying access', async ({ browser }) => {
  const { page } = await open(browser);
  await expect(page.getByTestId('payment-confirmed')).toHaveText('Payment confirmed');
  await expect(page.getByTestId('payment-applied')).toHaveText('Access applied to all 1 shops');
  await expect(page.getByTestId('payment-applying')).toHaveCount(0);
  await page.screenshot({ path: join(evidence, 'applied-desktop.png'), fullPage: true });
  observe('applied', { applied: '1 of 1' });
});
