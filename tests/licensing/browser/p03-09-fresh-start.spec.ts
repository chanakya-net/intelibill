import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page } from '@playwright/test';

function required(name: string): string {
  const value = process.env['P0309_' + name];
  if (!value) throw new Error('Missing fresh-start fixture variable ' + name);
  return value;
}

const origin = required('MERCHANT_WEB');
const account = required('ACCOUNT');
const predecessor = process.env['P0309_TERM'];
const evidence = required('EVIDENCE_DIR');
const total = required('TOTAL');
const shop = 'Fresh Term Shop';
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

async function quoteAndPay(page: Page, trial: boolean) {
  await page.goto(checkout + (trial ? '' : '?new=1'));
  // The previous browser's durable order is per context. These are independent UI sessions.
  await page.getByLabel('Plan').selectOption({ index: 1 });
  if (trial) await page.getByRole('checkbox', { name: shop }).check();
  else await page.locator('#checkout-renewal').selectOption(predecessor!);
  const created = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/orders'));
  await page.getByRole('button', { name: 'Create quote' }).click();
  const response = await created;
  expect(response.status()).toBe(201);
  const quote = await response.json();
  expect(quote.startRule).toBe(trial ? 'Fulfilment' : 'RestoredAccess');
  expect(quote.freshStart.trialShopCount).toBe(trial ? 1 : 0);
  expect(quote.graceDaysIncluded).toBe(0);
  const note = page.getByTestId(trial ? 'checkout-trial-forfeiture' : 'checkout-restricted-gap');
  await expect(note).toBeVisible();
  await expect(page.getByTestId('checkout-total')).toHaveText(total);
  await expect(page.getByTestId('checkout-covered-shop').getByText(shop)).toBeVisible();
  await expect(page.getByRole('checkbox', { name: /I accept the full amount/ })).toBeEnabled();
  await noOverflow(page);
  const phase = trial ? 'trial' : 'fresh';
  await page.screenshot({ path: join(evidence, phase + '-preview.png'), fullPage: true });

  await page.getByRole('checkbox', { name: /I accept the full amount/ }).check();
  await page.getByRole('button', { name: 'Accept quote' }).click();
  await page.getByRole('button', { name: 'Continue to payment' }).click();
  await expect(page.getByTestId('payment-simulator-notice')).toHaveText(/Development only/);
  await page.getByTestId('payment-simulator-capture').click();
  await expect(page.getByTestId('payment-confirmed')).toHaveText('Payment confirmed');
  await expect(page.getByTestId('payment-applied')).toHaveCount(0);
  await page.reload();
  await expect(page.getByTestId('payment-confirmed')).toHaveText('Payment confirmed');
  writeFileSync(join(evidence, phase + '-order.txt'), quote.orderId);
  observe(phase, { orderId: quote.orderId, startRule: quote.startRule, amountPaise: quote.amountPaise,
    previewStartsAt: quote.previewStartsAt, previewEndsAt: quote.previewEndsAt, disclosure: await note.textContent() });
}

test('@trial converts trial to a full paid calendar month', async ({ browser }) => {
  const page = await open(browser);
  await quoteAndPay(page, true);
  await expect(page.getByTestId('payment-applying')).toHaveText('Applying access to 0 of 1 shops');
  await expect(page.getByTestId('payment-term')).toContainText('2 Apr 2027, 10:00:00 IST');
  await expect(page.getByTestId('payment-term')).toContainText('2 May 2027, 10:00:00 IST');
  await page.screenshot({ path: join(evidence, 'trial-applying.png'), fullPage: true });
});

test('@fresh confirms funds while trading suspension still holds', async ({ browser }) => {
  const page = await open(browser, mobile);
  await quoteAndPay(page, false);
  await expect(page.getByTestId('payment-restoration-hold')).toBeVisible();
  await expect(page.getByTestId('payment-term')).toHaveCount(0);
  await page.screenshot({ path: join(evidence, 'fresh-held.png'), fullPage: true });
});

test('@retry reuses the confirmed funds after an independent release', async ({ browser }) => {
  const page = await open(browser);
  const order = readFileSync(join(evidence, 'fresh-order.txt'), 'utf8').trim();
  await page.goto(checkout + '?order=' + order);
  await page.getByTestId('payment-check-restoration').click();
  await expect(page.getByTestId('payment-confirmed')).toBeVisible();
  await expect(page.getByTestId('payment-applying')).toHaveText('Applying access to 0 of 1 shops');
  await expect(page.getByTestId('payment-applied')).toHaveCount(0);
  await expect(page.getByTestId('payment-term')).toContainText('12 May 2027, 10:00:00 IST');
  await expect(page.getByTestId('payment-term')).toContainText('12 Jun 2027, 10:00:00 IST');
  await page.screenshot({ path: join(evidence, 'fresh-applying.png'), fullPage: true });
});

test('@applied observes acknowledged paid access and separate adjustment', async ({ browser }) => {
  const page = await open(browser, mobile);
  const order = readFileSync(join(evidence, 'fresh-order.txt'), 'utf8').trim();
  await page.goto(checkout + '?order=' + order);
  await expect(page.getByTestId('payment-confirmed')).toBeVisible();
  await expect(page.getByTestId('payment-applied')).toHaveText('Access applied to all 1 shops');
  await expect(page.getByTestId('payment-applying')).toHaveCount(0);
  await expect(page.getByTestId('payment-term')).toContainText('12 Jun 2027, 10:00:00 IST');
  const adjustment = page.getByTestId('payment-restoration-adjustment');
  await expect(adjustment).toContainText('12 Jun 2027, 10:00:00 IST');
  await expect(adjustment).toContainText('13 Jun 2027, 10:00:00 IST');
  await noOverflow(page);
  observe('applied', { order, paidPeriod: await page.getByTestId('payment-term').textContent(),
    adjustment: await adjustment.textContent(), applied: '1 of 1' });
  await page.screenshot({ path: join(evidence, 'fresh-applied-adjustment-mobile.png'), fullPage: true });
});
