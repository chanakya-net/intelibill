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
const checkoutPath = (forAccount: string, forOrder: string) => '/billing/accounts/' + forAccount + '/checkout?order=' + forOrder;
const route = origin + checkoutPath(account, order);
const SCRIPT = 'https://checkout.razorpay.com/v1/checkout.js';

/**
 * Fixture stand-in for Razorpay's hosted script, served only for the fixed provider URL. Its checkout "pays" by
 * calling the page handler with a forged signature: the browser can only ever make a claim. With
 * `window.__razorpayHold` set, the callback stays outstanding until `window.__razorpayComplete()` fires it. With
 * `window.__razorpayDismiss` set, the customer closes the window instead, through the documented `modal.ondismiss`.
 */
const FIXTURE_SCRIPT = `
window.Razorpay = class {
  constructor(options) { this.options = options; window.__razorpayOptions = options; }
  open() {
    if (window.__razorpayDismiss) { this.options.modal.ondismiss(); return; }
    const complete = () => this.options.handler({
      razorpay_payment_id: 'pay_forged000001',
      razorpay_order_id: this.options.order_id,
      razorpay_signature: 'a'.repeat(64),
    });
    if (window.__razorpayHold) window.__razorpayComplete = complete; else complete();
  }
};`;

type FixtureWindow = {
  __razorpayOptions?: Record<string, unknown>; __razorpayHold?: boolean; __razorpayDismiss?: boolean; __razorpayComplete?: () => void;
};

async function open(browser: Browser, target = route) {
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
  if (target) await page.goto(target);
  return { page, loads };
}

const options = (page: Page) => page.evaluate(() => (window as unknown as FixtureWindow).__razorpayOptions);

function verifyAnswer(page: Page) {
  return page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/verify'));
}

function savedState(page: Page) {
  return page.evaluate(({ forAccount, forOrder }) => {
    const key = Object.keys(localStorage).find(k => k.startsWith('intelibill.billing.checkout.v1:payment:') &&
      k.includes(forAccount) && k.includes(forOrder));
    return key ? JSON.parse(localStorage.getItem(key) ?? '{}').state : null;
  }, { forAccount: account, forOrder: order });
}

/** Navigates inside the running app (the router follows popstate), so an outstanding modal callback survives. */
async function navigateInApp(page: Page, path: string) {
  await page.evaluate(target => {
    history.pushState({}, '', target);
    window.dispatchEvent(new PopStateEvent('popstate', { state: {} }));
  }, path);
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
  await page.screenshot({ path: join(evidence, 'forged-pending-desktop.png'), fullPage: true });
  const launched = await options(page);
  expect(launched?.['order_id']).toBe(answer.request().postDataJSON().launcherProof.razorpay_order_id);
  expect(launched?.['config']).toEqual({ display: {
    blocks: { pinned: { name: 'Pay the full amount', instruments: [{ method: 'card' }, { method: 'upi' }] } },
    sequence: ['block.pinned'], preferences: { show_default_blocks: false },
    hide: [{ method: 'emi' }, { method: 'paylater' }, { method: 'cardless_emi' }],
  } });

  // Reload rechecks without proof: a fresh provider read proves the same order unpaid, so it is offered again.
  const recheck = verifyAnswer(page);
  await page.reload();
  const rechecked = await recheck;
  expect(rechecked.request().postDataJSON()).toEqual({ launcherOutcome: null });
  expect((await rechecked.json()).launch.parameters.orderId).toBe(launched?.['order_id']);
  await expect(page.getByTestId('payment-razorpay-pay')).toBeVisible();
  await expect(page.getByTestId('payment-confirmed')).toHaveCount(0);
  expect(await savedState(page)).toBe('awaiting');
  observe('forged', { scriptOrigins: loads, options: launched, pendingAfterForgedCallback: true, confirmed: false,
    sameOrderOfferedAfterUnpaidRecheck: true });
});

test('@authorized provider authorization alone stays pending', async ({ browser }) => {
  const { page } = await open(browser);
  await expect(page.getByTestId('payment-razorpay-pay')).toBeVisible();
  await page.getByTestId('payment-razorpay-check').click();
  await expect(page.getByTestId('payment-pending')).toBeVisible();
  await expect(page.getByTestId('payment-confirmed')).toHaveCount(0);
  await page.screenshot({ path: join(evidence, 'authorized-pending-desktop.png'), fullPage: true });
  observe('authorized', { pendingWhileAuthorized: true, confirmed: false });
});

test('@captured independent server-verified capture confirms and shows applying access', async ({ browser }) => {
  const { page } = await open(browser);
  // Loading a pending attempt rechecks it once; Check again is only needed if that recheck has not answered yet.
  const confirmed = page.getByTestId('payment-confirmed');
  await expect(confirmed.or(page.getByTestId('payment-check'))).toBeVisible();
  if (!await confirmed.isVisible()) await page.getByTestId('payment-check').click({ timeout: 3000 }).catch(() => undefined);
  await expect(confirmed).toHaveText('Payment confirmed');
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

test('@lost a lost create and a failed first verification read recover the same unpaid order', async ({ browser }) => {
  const { page } = await open(browser);
  const started = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/payment/attempts'));
  await page.getByRole('button', { name: 'Continue to payment' }).click();
  const start = await started;
  expect(start.status()).toBe(201);
  expect((await start.json()).payment).toBe('PendingVerification');
  await expect(page.getByTestId('payment-pending')).toBeVisible();

  // Reload rechecks: the order lookup finds the original order, then the provider read fails once.
  const failedRead = verifyAnswer(page);
  await page.reload();
  const failed = await (await failedRead).json();
  expect([failed.payment, failed.launch]).toEqual(['PendingVerification', null]);
  await expect(page.getByTestId('payment-pending')).toBeVisible();
  await expect(page.getByTestId('payment-razorpay-pay')).toHaveCount(0);

  const recovered = verifyAnswer(page);
  await page.getByTestId('payment-check').click();
  const resumed = await (await recovered).json();
  expect(resumed.payment).toBe('AwaitingPayment');
  await expect(page.getByTestId('payment-razorpay-pay')).toBeVisible();
  await page.reload();
  await expect(page.getByTestId('payment-razorpay-pay')).toBeVisible();
  expect(await savedState(page)).toBe('awaiting');
  await page.screenshot({ path: join(evidence, 'lost-recovered-desktop.png'), fullPage: true });
  observe('lost', { startPayment: 'PendingVerification', afterFailedRead: failed.payment, recoveredOrder: resumed.launch.parameters.orderId,
    launcherAfterReload: true });
});

test('@tabs an older read cannot reopen a newer claim, and a callback outlives no account change', async ({ browser }) => {
  const otherAccount = required('OTHER_ACCOUNT');
  const otherOrder = required('OTHER_ORDER');
  const { page: tabA } = await open(browser);
  await expect(tabA.getByTestId('payment-razorpay-pay')).toBeVisible();
  const tabB = await tabA.context().newPage();
  let fetched = false;
  let release: () => void = () => undefined;
  const gate = new Promise<void>(resolve => { release = resolve; });
  await tabB.route('**/billing/accounts/' + account + '/orders/' + order + '/payment', async held => {
    const response = await held.fetch();
    fetched = true;
    await gate;
    await held.fulfill({ response });
  }, { times: 1 });
  await tabB.goto(route);
  await expect.poll(() => fetched).toBe(true);

  const claim = verifyAnswer(tabA);
  await tabA.getByTestId('payment-razorpay-pay').click();
  expect((await (await claim).json()).payment).toBe('PendingVerification');
  await expect(tabA.getByTestId('payment-pending')).toBeVisible();
  expect(await savedState(tabA)).toBe('pending');
  release();
  await expect(tabB.getByTestId('payment-pending')).toBeVisible();
  await expect(tabB.getByTestId('payment-razorpay-pay')).toHaveCount(0);
  expect(await savedState(tabB)).toBe('pending');
  await tabB.screenshot({ path: join(evidence, 'tabs-stale-read-pending-desktop.png'), fullPage: true });

  // A fresh proof-free recheck proves the order still unpaid and the same order can be launched again.
  const fresh = verifyAnswer(tabB);
  await tabB.getByTestId('payment-check').click();
  const freshAnswer = await (await fresh).json();
  expect(freshAnswer.payment).toBe('AwaitingPayment');
  await expect(tabB.getByTestId('payment-razorpay-pay')).toBeVisible();
  expect(await savedState(tabB)).toBe('awaiting');
  await tabB.evaluate(() => { (window as unknown as FixtureWindow).__razorpayHold = true; });
  await tabB.getByTestId('payment-razorpay-pay').click();
  await expect.poll(() => tabB.evaluate(() => typeof (window as unknown as FixtureWindow).__razorpayComplete)).toBe('function');
  const relaunched = await options(tabB);
  expect(relaunched?.['order_id']).toBe(freshAnswer.launch.parameters.orderId);

  // Change billing account with the callback outstanding, then let it fire on the new scope.
  await tabA.close();
  const verifies: string[] = [];
  tabB.on('request', request => {
    if (request.method() === 'POST' && request.url().endsWith('/verify')) verifies.push(new URL(request.url()).pathname);
  });
  await navigateInApp(tabB, checkoutPath(otherAccount, otherOrder));
  await expect(tabB).toHaveURL(new RegExp(otherAccount));
  await expect(tabB.getByTestId('payment-start')).toBeVisible();
  await tabB.evaluate(() => (window as unknown as FixtureWindow).__razorpayComplete?.());
  await tabB.waitForTimeout(2000);
  expect(verifies).toEqual([]);
  await expect(tabB.getByTestId('payment-pending')).toHaveCount(0);
  await tabB.screenshot({ path: join(evidence, 'tabs-other-account-desktop.png'), fullPage: true });
  observe('tabs', { staleReadKeptPending: true, freshRecheck: freshAnswer.payment, relaunchedOrder: relaunched?.['order_id'],
    otherAccount, staleVerifyPosts: verifies });
});

test('@recheck the original order fulfils through Check again after the account change', async ({ browser }) => {
  const { page } = await open(browser);
  await expect(page.getByTestId('payment-razorpay-pay')).toBeVisible();
  const check = verifyAnswer(page);
  await page.getByTestId('payment-razorpay-check').click();
  const answer = await check;
  expect(answer.url()).toContain('/billing/accounts/' + account + '/orders/' + order + '/');
  expect(answer.request().postDataJSON()).toEqual({ launcherOutcome: null });
  await expect(page.getByTestId('payment-confirmed')).toHaveText('Payment confirmed');
  await expect(page.getByTestId('payment-applying')).toContainText('Applying access to 0 of 1 shops');
  await page.screenshot({ path: join(evidence, 'recheck-confirmed-desktop.png'), fullPage: true });
  observe('recheck', { confirmed: true, originalOrder: true });
});

/** R016: a dismissed window is an unknown outcome; the original order is checked before Pay is offered again. */
async function dismiss(page: Page) {
  await page.evaluate(() => { (window as unknown as FixtureWindow).__razorpayDismiss = true; });
  const check = verifyAnswer(page);
  await page.getByTestId('payment-razorpay-pay').click();
  const answer = await check;
  expect(answer.status()).toBe(200);
  expect(answer.request().postDataJSON()).toEqual({ launcherOutcome: null });
  return { answer: await answer.json(), launched: await options(page) };
}

test('@dismiss-idle a window dismissed before any transaction resumes the same idle order', async ({ browser }) => {
  const { page } = await open(browser);
  const started = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/payment/attempts'));
  await page.getByRole('button', { name: 'Continue to payment' }).click();
  expect((await started).status()).toBe(201);
  const { answer, launched } = await dismiss(page);
  expect(answer.payment).toBe('AwaitingPayment');
  expect(answer.launch.parameters.orderId).toBe(launched?.['order_id']);
  await expect(page.getByTestId('payment-razorpay-pay')).toBeVisible();
  await expect(page.getByTestId('payment-pending')).toHaveCount(0);
  expect(await savedState(page)).toBe('awaiting');
  await page.screenshot({ path: join(evidence, 'dismiss-idle-desktop.png'), fullPage: true });
  observe('dismiss-idle', { recheck: answer.payment, sameOrder: answer.launch.parameters.orderId });
});

test('@dismiss-authorized a window dismissed with a provider payment authorized stays pending without Pay', async ({ browser }) => {
  const { page } = await open(browser);
  await expect(page.getByTestId('payment-razorpay-pay')).toBeVisible();
  const { answer } = await dismiss(page);
  expect([answer.payment, answer.launch]).toEqual(['PendingVerification', null]);
  await expect(page.getByTestId('payment-pending')).toBeVisible();
  await expect(page.getByTestId('payment-razorpay-pay')).toHaveCount(0);
  expect(await savedState(page)).toBe('pending');
  await page.reload();
  await expect(page.getByTestId('payment-pending')).toBeVisible();
  await expect(page.getByTestId('payment-razorpay-pay')).toHaveCount(0);
  await page.screenshot({ path: join(evidence, 'dismiss-authorized-pending-desktop.png'), fullPage: true });
  observe('dismiss-authorized', { recheck: answer.payment, launch: answer.launch, payOffered: false });
});

test('@dismiss-captured the same attempt is verified once its payment is captured', async ({ browser }) => {
  const { page } = await open(browser);
  const confirmed = page.getByTestId('payment-confirmed');
  await expect(confirmed.or(page.getByTestId('payment-check'))).toBeVisible();
  if (!await confirmed.isVisible()) await page.getByTestId('payment-check').click({ timeout: 3000 }).catch(() => undefined);
  await expect(confirmed).toHaveText('Payment confirmed');
  await expect(page.getByTestId('payment-applying')).toContainText('Applying access to 0 of 1 shops');
  await page.screenshot({ path: join(evidence, 'dismiss-captured-desktop.png'), fullPage: true });
  observe('dismiss-captured', { confirmed: true });
});
