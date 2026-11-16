import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page } from '@playwright/test';

function setting(name: string): string {
  const value = process.env['P0303_' + name];
  if (!value) throw new Error('Missing checkout recovery fixture variable ' + name);
  return value;
}

const origin = setting('MERCHANT_WEB');
const account = setting('ACCOUNT');
const order = setting('ORDER');
const otherAccount = setting('OTHER_ACCOUNT');
const otherOrder = setting('OTHER_ORDER');
const evidence = setting('EVIDENCE_DIR');
const checkout = (accountId: string, orderId: string) => origin + '/billing/accounts/' + accountId + '/checkout?order=' + orderId;
const primary = checkout(account, order);
const other = checkout(otherAccount, otherOrder);
const payment = '/orders/' + order + '/payment';
const desktop = { width: 1440, height: 900 };
const mobile = { width: 390, height: 844 };

async function signIn(browser: Browser, viewport = desktop) {
  const context = await browser.newContext({ viewport, locale: 'en-IN', timezoneId: 'Asia/Kolkata',
    isMobile: viewport === mobile, hasTouch: viewport === mobile });
  const page = await context.newPage();
  await page.goto(origin + '/login');
  await page.locator('#identifier').fill(setting('OWNER'));
  await page.locator('#password').evaluate((input: HTMLInputElement, password) => {
    input.value = password;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }, setting('PASSWORD'));
  const login = page.waitForResponse(r => new URL(r.url()).pathname === '/api/auth/login' && r.request().method() === 'POST');
  await page.locator('button[type=submit]').click();
  expect((await login).status()).toBe(200);
  await expect(page).not.toHaveURL(/\/login/);
  return page;
}

async function assertPending(page: Page) {
  await expect(page.getByTestId('payment-pending')).toBeVisible();
  await expect(page.getByTestId('payment-start')).toHaveCount(0);
  await expect(page.getByTestId('payment-confirmed')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'New quote' })).toHaveCount(0);
}

function record(name: string, facts: Record<string, unknown>) {
  writeFileSync(join(evidence, name + '-observations.json'), JSON.stringify(facts, null, 2));
}

async function screenshot(page: Page, name: string) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: join(evidence, name + '.png'), fullPage: true });
}

test('@uncertain reload, second tab and billing account navigation keep one uncertain operation', async ({ browser }) => {
  const page = await signIn(browser);
  const starts: string[] = [];
  page.context().on('request', request => {
    if (request.method() === 'POST' && request.url().endsWith(payment + '/attempts'))
      starts.push(request.postDataJSON().operationKey);
  });
  let originalAttempt: string | undefined;
  await page.route('**' + payment + '/attempts', async route => {
    const committed = await route.fetch();
    expect(committed.status()).toBe(201);
    originalAttempt = (await committed.json()).attemptId;
    await route.abort('timedout');
  }, { times: 1 });
  await page.goto(primary);
  await page.getByTestId('payment-start').click();
  await expect.poll(() => originalAttempt).toBeTruthy();
  await assertPending(page);

  // The original attempt exists in PostgreSQL. Only this browser GET is replaced with a synthetic 404.
  let lookup404 = 0;
  await page.route('**' + payment, async route => {
    lookup404++;
    await route.fulfill({ status: 404, json: { code: 'Billing.PaymentNotFound' } });
  }, { times: 1 });
  await page.reload();
  await expect.poll(() => lookup404).toBe(1);
  await assertPending(page);

  const second = await page.context().newPage();
  await second.goto(primary);
  await expect(second.getByTestId('payment-simulator')).toBeVisible();
  const verify = second.waitForResponse(r => r.url().endsWith(payment + '/attempts/' + originalAttempt + '/verify'));
  await second.getByTestId('payment-simulator-timeout').click();
  const observed = await verify;
  expect(observed.status()).toBe(200);
  expect((await observed.json()).payment).toBe('PendingVerification');
  await assertPending(second);
  await page.reload();
  await assertPending(page);
  await screenshot(page, 'pending-desktop');

  const leaked: string[] = [];
  const onOther = (request: import('@playwright/test').Request) => {
    const path = new URL(request.url()).pathname;
    if (path.includes(payment)) leaked.push(request.method() + ' ' + path);
  };
  await second.goto('about:blank');
  second.on('request', onOther);
  await second.goto(other);
  await expect(second.getByTestId('payment-start')).toBeVisible();
  await expect(second.getByTestId('payment-pending')).toHaveCount(0);
  await expect(second.getByTestId('payment-simulator')).toHaveCount(0);
  expect(leaked).toEqual([]);
  second.off('request', onOther);
  await second.goto(primary);
  await assertPending(second);

  const retained = await second.evaluate(({ account, order }) => {
    const key = Object.keys(localStorage).find(k => k.startsWith('intelibill.billing.checkout.v1:payment:') &&
      k.includes(account) && k.includes(order));
    const saved = key && JSON.parse(localStorage.getItem(key) ?? '{}');
    return saved && { key: saved.command.operationKey, attempt: saved.attempt, state: saved.state };
  }, { account, order });
  expect(retained).toEqual({ key: starts[0], attempt: originalAttempt, state: 'pending' });
  expect(starts).toHaveLength(1);
  record('uncertain', { starts, originalAttempt, retained, lookup404, leaked, simulatorVerify: 'PendingVerification' });
});

test('@applying confirmed payment awaits signed access acknowledgement', async ({ browser }) => {
  const page = await signIn(browser, mobile);
  await page.goto(primary);
  await expect(page.getByTestId('payment-confirmed')).toBeVisible();
  await expect(page.getByTestId('payment-applying')).toContainText('0 of 1 shops');
  await screenshot(page, 'applying-mobile');
  record('applying', { payment: 'Verified', access: 'Applying', acknowledgedShops: 0 });
});

test('@applied after acknowledgement and definitive failure on a separate order', async ({ browser }) => {
  const page = await signIn(browser);
  await page.goto(primary);
  await expect(page.getByTestId('payment-applied')).toContainText('all 1 shops');
  await screenshot(page, 'applied-desktop');

  await page.goto(other);
  await page.getByTestId('payment-start').click();
  await page.getByTestId('payment-simulator-failed').click();
  await expect(page.getByTestId('payment-failed')).toBeVisible();
  await expect(page.getByTestId('payment-pending')).toHaveCount(0);
  await expect(page.getByTestId('payment-start')).toHaveCount(0);
  await page.reload();
  await expect(page.getByTestId('payment-failed')).toBeVisible();
  await screenshot(page, 'failed-desktop');
  record('applied', { access: 'Applied', acknowledgedShops: 1, separateOrder: 'Failed', failedSurvivedReload: true });
});
