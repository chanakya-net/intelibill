/** Shared Chromium fixture for the P03-04 Razorpay browser phases: login, fixture provider script and evidence. */
import { existsSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, type Browser, type Page } from '@playwright/test';

export function required(name: string): string {
  const value = process.env['P0304_' + name];
  if (!value) throw new Error('Missing Razorpay fixture variable ' + name);
  return value;
}

export const origin = required('MERCHANT_WEB');
export const account = required('ACCOUNT');
export const order = required('ORDER');
export const evidence = required('EVIDENCE_DIR');
export const checkoutPath = (forAccount: string, forOrder: string) => '/billing/accounts/' + forAccount + '/checkout?order=' + forOrder;
export const route = origin + checkoutPath(account, order);
export const SCRIPT = 'https://checkout.razorpay.com/v1/checkout.js';

/**
 * Fixture stand-in for Razorpay's hosted script, served only for the fixed provider URL. Its checkout "pays" by
 * calling the page handler with a forged signature: the browser can only ever make a claim. With
 * `window.__razorpayHold` set, the callback stays outstanding until `window.__razorpayComplete()` fires it. With
 * `window.__razorpayDismiss` set, the customer closes the window instead, through the documented `modal.ondismiss`.
 * `window.__razorpayMalformed` makes the success handler fire with no signature (`unsigned`) or for another order
 * (`wrong-order`), as a tampered or broken checkout result would. With `window.__razorpayFail` set, the window waits
 * until `window.__razorpayFailNow()` reports a failed payment through the documented `payment.failed` event; the
 * page's `close()` is recorded in `window.__razorpayClosed`.
 */
export const FIXTURE_SCRIPT = `
window.Razorpay = class {
  constructor(options) { this.options = options; this.handlers = {}; window.__razorpayOptions = options; }
  on(event, handler) { this.handlers[event] = handler; }
  close() { window.__razorpayClosed = true; }
  open() {
    if (window.__razorpayDismiss) { this.options.modal.ondismiss(); return; }
    if (window.__razorpayFail) {
      window.__razorpayFailNow = () => this.handlers['payment.failed']({ error: {
        code: 'BAD_REQUEST_ERROR', reason: 'payment_failed', metadata: { order_id: this.options.order_id } } });
      return;
    }
    const result = {
      razorpay_payment_id: 'pay_forged000001',
      razorpay_order_id: this.options.order_id,
      razorpay_signature: 'a'.repeat(64),
    };
    if (window.__razorpayMalformed === 'unsigned') delete result.razorpay_signature;
    if (window.__razorpayMalformed === 'wrong-order') result.razorpay_order_id = 'order_other000001';
    const complete = () => this.options.handler(result);
    if (window.__razorpayHold) window.__razorpayComplete = complete; else complete();
  }
};`;

export type FixtureWindow = {
  __razorpayOptions?: Record<string, unknown>; __razorpayHold?: boolean; __razorpayDismiss?: boolean; __razorpayComplete?: () => void;
  __razorpayMalformed?: 'unsigned' | 'wrong-order';
  __razorpayFail?: boolean; __razorpayFailNow?: () => void; __razorpayClosed?: boolean;
};

export async function open(browser: Browser, target = route) {
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

export const options = (page: Page) => page.evaluate(() => (window as unknown as FixtureWindow).__razorpayOptions);

export function verifyAnswer(page: Page) {
  return page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/verify'));
}

export function savedState(page: Page) {
  return page.evaluate(({ forAccount, forOrder }) => {
    const key = Object.keys(localStorage).find(k => k.startsWith('intelibill.billing.checkout.v1:payment:') &&
      k.includes(forAccount) && k.includes(forOrder));
    return key ? JSON.parse(localStorage.getItem(key) ?? '{}').state : null;
  }, { forAccount: account, forOrder: order });
}

/** Navigates inside the running app (the router follows popstate), so an outstanding modal callback survives. */
export async function navigateInApp(page: Page, path: string) {
  await page.evaluate(target => {
    history.pushState({}, '', target);
    window.dispatchEvent(new PopStateEvent('popstate', { state: {} }));
  }, path);
}

export function observe(name: string, facts: Record<string, unknown>) {
  writeFileSync(join(evidence, name + '-observations.json'), JSON.stringify(facts, null, 2));
}

export async function noOverflow(page: Page) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
}

/** Hands control to the xUnit side, which changes the fixture provider and answers with its own flag file. */
export async function handOff(signal: string, answer: string) {
  writeFileSync(join(evidence, signal + '.flag'), '');
  await expect.poll(() => existsSync(join(evidence, answer + '.flag')), { timeout: 60_000 }).toBe(true);
}
