import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page } from '@playwright/test';

function required(name: string): string {
  const value = process.env['P0308_' + name];
  if (!value) throw new Error('Missing renewal fixture variable ' + name);
  return value;
}

const origin = required('MERCHANT_WEB');
const account = required('ACCOUNT');
const term = required('TERM');
const evidence = required('EVIDENCE_DIR');
const total = required('TOTAL');
const shops = ['Renewal Central', 'Renewal East'];
const desktop = { width: 1440, height: 900 };
const mobile = { width: 390, height: 844 };
const checkout = origin + '/billing/accounts/' + account + '/checkout';

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

function observe(name: string, facts: Record<string, unknown>) {
  writeFileSync(join(evidence, name + '-observations.json'), JSON.stringify(facts, null, 2));
}

async function noOverflow(page: Page) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
}

async function preview(page: Page) {
  await expect(page.getByTestId('renewal-preview')).toBeVisible();
  await expect(page.getByTestId('renewal-previous-end')).toHaveText('30 Apr 2027, 10:00:00 IST');
  await expect(page.getByTestId('renewal-new-end')).toHaveText('31 May 2027, 10:00:00 IST');
  await expect(page.getByTestId('renewal-grace')).toHaveText(
    'This renewal includes 3 grace days already elapsed since the previous paid-through. ' +
    'The new period still starts at the previous paid-through, so grace is not added again.');
  await expect(page.getByTestId('checkout-total')).toHaveText(total);
  await expect(page.getByTestId('checkout-covered-shop')).toHaveCount(shops.length);
  for (const shop of shops) await expect(page.getByTestId('checkout-covered-shop').getByText(shop)).toBeVisible();
}

test('@renew in-grace renewal previews the anchored dates, then pays through the simulator', async ({ browser }) => {
  const page = await open(browser);
  await page.goto(checkout);
  await page.getByLabel('Plan').selectOption({ index: 1 });
  await page.locator('#checkout-renewal').selectOption(term);
  const created = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/orders'));
  await page.getByRole('button', { name: 'Create quote' }).click();
  const response = await created;
  expect(response.status()).toBe(201);
  const quote = await response.json();
  expect(quote.startRule).toBe('PriorPaidThrough');
  await preview(page);
  await noOverflow(page);
  await page.screenshot({ path: join(evidence, 'renewal-preview-desktop.png'), fullPage: true });

  const phone = await open(browser, mobile);
  await phone.goto(checkout + '?order=' + quote.orderId);
  await preview(phone);
  await noOverflow(phone);
  await phone.screenshot({ path: join(evidence, 'renewal-preview-mobile.png'), fullPage: true });
  await phone.context().close();

  await page.getByRole('checkbox', { name: /I accept the full amount/ }).check();
  const accepted = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/accept'));
  await page.getByRole('button', { name: 'Accept quote' }).click();
  expect((await accepted).status()).toBe(200);
  await expect(page.getByTestId('checkout-accepted')).toBeVisible();
  await expect(page.getByRole('button', { name: /pay now|instalment|split/i })).toHaveCount(0);
  const started = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/payment/attempts'));
  await page.getByRole('button', { name: 'Continue to payment' }).click();
  expect((await started).status()).toBe(201);
  await expect(page.getByTestId('payment-simulator-notice')).toHaveText(/Development only/);
  await page.getByTestId('payment-simulator-capture').click();
  await expect(page.getByTestId('payment-confirmed')).toHaveText('Payment confirmed');
  await expect(page.getByTestId('payment-applying')).toHaveText('Applying access to 0 of 2 shops');
  await expect(page.getByTestId('payment-term')).toContainText('30 Apr 2027, 10:00:00 IST');
  await expect(page.getByTestId('payment-term')).toContainText('31 May 2027, 10:00:00 IST');
  await page.reload();
  await expect(page.getByTestId('payment-confirmed')).toHaveText('Payment confirmed');
  await expect(page.getByTestId('renewal-preview')).toBeVisible();
  await page.screenshot({ path: join(evidence, 'renewal-applying-desktop.png'), fullPage: true });
  writeFileSync(join(evidence, 'order.txt'), quote.orderId);
  observe('renew', { order: quote.orderId, startRule: quote.startRule, previousEnd: quote.previewStartsAt,
    newEnd: quote.previewEndsAt, graceDaysIncluded: quote.graceDaysIncluded, accepted: true, confirmed: true,
    applying: '0 of 2' });
});

test('@applied merchant acknowledgement applies the renewed access to both shops', async ({ browser }) => {
  const page = await open(browser);
  const order = readFileSync(join(evidence, 'order.txt'), 'utf8').trim();
  await page.goto(checkout + '?order=' + order);
  await expect(page.getByTestId('payment-confirmed')).toHaveText('Payment confirmed');
  await expect(page.getByTestId('payment-applied')).toHaveText('Access applied to all 2 shops');
  await expect(page.getByTestId('payment-applying')).toHaveCount(0);
  await page.screenshot({ path: join(evidence, 'renewal-applied-desktop.png'), fullPage: true });
  observe('applied', { order, applied: '2 of 2' });
});
