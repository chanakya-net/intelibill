import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page } from '@playwright/test';

function required(name: string): string {
  const value = process.env['P0301_' + name];
  if (!value) throw new Error('Missing checkout browser fixture variable ' + name);
  return value;
}

const origin = required('MERCHANT_WEB');
const account = required('ACCOUNT');
const otherAccount = required('OTHER_ACCOUNT');
const evidence = required('EVIDENCE_DIR');
const shops = ['Fixture Central', 'Fixture East', 'Fixture North', 'Fixture West'];
const desktop = { width: 1440, height: 900 };
const mobile = { width: 390, height: 844 };
const timezoneId = 'America/Los_Angeles';
const route = (id = account) => origin + '/billing/accounts/' + id + '/checkout';

async function open(browser: Browser, viewport = desktop) {
  const context = await browser.newContext({ viewport, locale: 'en-IN', timezoneId,
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

async function review(page: Page) {
  const total = page.getByTestId('checkout-total');
  await expect(total).toHaveText('₹270.00');
  await expect(page.getByTestId('checkout-expiry')).toContainText('10 Feb 2027, 05:45:00.123456 IST');
  await expect(page.getByTestId('checkout-covered-shop')).toHaveCount(4);
  for (const shop of shops) await expect(page.getByTestId('checkout-covered-shop').getByText(shop)).toBeVisible();
  await expect(page.getByTestId('checkout-capacity')).toHaveText('4');
  await expect(page.getByTestId('checkout-member-limit')).toHaveText('5');
  await expect(page.getByTestId('checkout-policies')).toContainText('customer-local-1');
  await expect(page.getByTestId('checkout-policies')).toContainText('refund-local-1');
  await expect(page.getByText(/starts when the purchase can be fulfilled/i)).toBeVisible();
  await expect(page.getByRole('button', { name: /pay now|instalment|split/i })).toHaveCount(0);
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
}

test('@review desktop and mobile review the entire quote and recover the same acceptance', async ({ browser }) => {
  const page = await open(browser);
  await page.goto(origin + '/billing');
  await page.locator('[data-account-id="' + account + '"]').getByRole('link', { name: 'Review a quote' }).click();
  await page.getByLabel('Plan').selectOption({ label: 'Local growth' });
  for (const shop of shops) await page.getByRole('checkbox', { name: shop }).check();
  const created = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/orders'));
  await page.getByRole('button', { name: 'Create quote' }).click();
  const response = await created;
  expect(response.status()).toBe(201);
  expect(response.headers()['cache-control']).toContain('no-store');
  const quote = await response.json();
  const quotePage = route() + '?order=' + quote.orderId;
  await review(page);
  const accept = page.getByRole('button', { name: 'Accept quote' });
  await expect(accept).toBeDisabled();
  await page.screenshot({ path: join(evidence, 'quote-desktop.png'), fullPage: true });

  const phone = await open(browser, mobile);
  await phone.goto(quotePage);
  await review(phone);
  await expect(phone.getByRole('button', { name: 'Accept quote' })).toBeDisabled();
  await phone.screenshot({ path: join(evidence, 'quote-mobile.png'), fullPage: true });

  let sent = 0;
  const requestBodies: string[] = [];
  const acceptanceRequests: string[] = [];
  await page.route('**/orders/*/accept', async intercepted => {
    const request = intercepted.request();
    expect(request.postDataJSON().confirmed).toBe(true);
    acceptanceRequests.push(request.postDataJSON().operationKey);
    requestBodies.push(request.postData()!);
    if (acceptanceRequests.length > 1) {
      const result = await intercepted.fetch();
      expect(result.status()).toBe(200);
      sent++;
    }
    await intercepted.abort();
  });
  await page.getByRole('checkbox', { name: /accept the full amount/ }).check();
  await accept.click();
  await expect(page.getByRole('alert')).toContainText('The quote outcome is unknown');
  const operationKey = acceptanceRequests[0];
  expect(operationKey).toBeTruthy();

  const lookup = page.waitForResponse(r => r.url().endsWith('/operations/accept/' + operationKey));
  await page.reload();
  expect((await lookup).status()).toBe(404);
  await page.getByRole('button', { name: 'Retry original request' }).click();
  await expect.poll(() => sent).toBe(1);
  await expect(page.getByRole('alert')).toContainText('The quote outcome is unknown');
  expect(acceptanceRequests).toEqual([operationKey, operationKey]);
  expect(requestBodies[0]).toBe(requestBodies[1]);

  await page.goto(route(otherAccount));
  await expect(page.getByTestId('checkout-total')).toHaveCount(0);
  await page.goto(quotePage);
  await expect(page.getByTestId('checkout-accepted')).toHaveText('Quote accepted. Payment has not started. Access is unchanged.');
  await expect(page.getByTestId('checkout-total')).toHaveText('₹270.00');
  expect(sent).toBe(1);
  await phone.reload();
  await expect(phone.getByTestId('checkout-accepted')).toBeVisible();
  writeFileSync(join(evidence, 'review.json'), JSON.stringify({
    desktop, mobile, timezoneId, shops, orderId: quote.orderId, snapshotHash: quote.snapshotHash, operationKey,
    amountPaise: quote.amountPaise, quoteExpiry: quote.expiresAt, creationCacheControl: response.headers()['cache-control'],
    acceptedPosts: sent, fullCoverageReviewed: true, confirmationInitiallyDisabled: true, recoveredAcrossAccountNavigation: true,
    independentMobileReload: true, missingLookupPreservedExactRequest: true,
  }, null, 2));
  await Promise.all([page.context().close(), phone.context().close()]);
});

test('@expiry accepted facts survive expiry and a separate unaccepted quote is refused', async ({ browser }) => {
  const page = await open(browser, mobile);
  const observation = JSON.parse(readFileSync(join(evidence, 'review.json'), 'utf-8'));
  await page.goto(route() + '?order=' + observation.orderId);
  await review(page);
  await expect(page.getByTestId('checkout-accepted')).toBeVisible();

  await page.goto(route() + '?order=' + required('EXPIRED_ORDER'));
  await expect(page.getByTestId('checkout-total')).toBeVisible();
  const alert = page.getByRole('alert');
  const accept = page.getByRole('button', { name: 'Accept quote' });
  await page.getByRole('checkbox', { name: /accept the full amount/ }).check();
  const refusal = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/accept'));
  await accept.click();
  expect((await refusal).status()).toBe(409);
  await expect(accept).toBeDisabled();
  await expect(alert).toContainText('This quote expired before acceptance');
  writeFileSync(join(evidence, 'expiry.json'), JSON.stringify({
    acceptedAmountRetained: true, acceptedSnapshotHash: observation.snapshotHash,
    exactBoundary: observation.quoteExpiry, expiredUnacceptedRefused: true,
  }, null, 2));
  await page.context().close();
});
