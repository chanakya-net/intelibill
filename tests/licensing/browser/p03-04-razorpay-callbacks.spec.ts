import { join } from 'node:path';
import { expect, test } from '@playwright/test';
import {
  evidence, handOff, observe, open, options, savedState, verifyAnswer, type FixtureWindow,
} from './p03-04-razorpay-fixture';

/**
 * R010/R014 Chromium phases: a reload that loses the hosted window's callback, and success callbacks without valid
 * proof. The provider is the xUnit side's fixture transport; flag files in the evidence directory hand control over.
 */
test('@reload-opened a reload after the window opened rechecks the original order before Pay', async ({ browser }) => {
  const { page } = await open(browser);
  const started = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/payment/attempts'));
  await page.getByRole('button', { name: 'Continue to payment' }).click();
  expect((await started).status()).toBe(201);
  await page.evaluate(() => { (window as unknown as FixtureWindow).__razorpayHold = true; });
  await page.getByTestId('payment-razorpay-pay').click();
  await expect.poll(() => page.evaluate(() => typeof (window as unknown as FixtureWindow).__razorpayComplete)).toBe('function');
  expect(await savedState(page)).toBe('pending');
  const launched = await options(page);
  await handOff('reload-window-opened', 'reload-authorized');

  // The provider has authorized a payment; the page reloads without the handler or ondismiss ever firing.
  let release: () => void = () => undefined;
  const gate = new Promise<void>(resolve => { release = resolve; });
  const verifies: unknown[] = [];
  await page.route('**/attempts/*/verify', async held => {
    verifies.push(held.request().postDataJSON());
    await gate;
    await held.continue();
  });
  const read = page.waitForResponse(r => r.request().method() === 'GET' && r.url().endsWith('/payment'));
  await page.reload();
  const before = await (await read).json();
  await expect.poll(() => verifies.length).toBe(1);
  await expect(page.getByTestId('payment-pending')).toBeVisible();
  await expect(page.getByTestId('payment-razorpay-pay')).toHaveCount(0);
  await page.screenshot({ path: join(evidence, 'reload-opened-before-verification-desktop.png'), fullPage: true });
  const recheck = verifyAnswer(page);
  release();
  const answer = await (await recheck).json();
  expect([answer.payment, answer.launch]).toEqual(['PendingVerification', null]);
  await expect(page.getByTestId('payment-pending')).toBeVisible();
  await expect(page.getByTestId('payment-razorpay-pay')).toHaveCount(0);
  await page.screenshot({ path: join(evidence, 'reload-opened-authorized-pending-desktop.png'), fullPage: true });
  await page.unroute('**/attempts/*/verify');
  await handOff('reload-pending', 'reload-captured');

  const settled = verifyAnswer(page);
  await page.getByTestId('payment-check').click();
  expect((await settled).request().postDataJSON()).toEqual({ launcherOutcome: null });
  await expect(page.getByTestId('payment-confirmed')).toHaveText('Payment confirmed');
  await expect(page.getByTestId('payment-applying')).toContainText('Applying access to 0 of 1 shops');
  await page.screenshot({ path: join(evidence, 'reload-opened-captured-desktop.png'), fullPage: true });
  observe('reload-opened', { launchedOrder: launched?.['order_id'], statusReadBeforeVerification: before.payment,
    launcherOfferedByStaleRead: before.launch !== null, recheckBody: verifies[0], afterAuthorization: answer.payment,
    payOfferedBeforeVerification: false, payOfferedWhileAuthorized: false, confirmed: true });
});

test('@malformed-start the attempt is started and offered before the fixture order is captured', async ({ browser }) => {
  const { page } = await open(browser);
  const started = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/payment/attempts'));
  await page.getByRole('button', { name: 'Continue to payment' }).click();
  expect((await started).status()).toBe(201);
  await expect(page.getByTestId('payment-razorpay-pay')).toBeVisible();
  observe('malformed-start', { payOffered: true });
});

/** R014: success callbacks without valid proof stay success claims the server refuses, even on a captured order. */
test('@malformed-callbacks unsigned and wrong-order success callbacks grant nothing on an already captured order', async ({ browser }) => {
  // Two separate browser contexts, both showing Pay for the same attempt before either callback fires.
  const tabs = [await open(browser), await open(browser)];
  for (const { page } of tabs) await expect(page.getByTestId('payment-razorpay-pay')).toBeVisible();
  const answers: Record<string, unknown>[] = [];
  for (const [index, mode] of (['unsigned', 'wrong-order'] as const).entries()) {
    const { page } = tabs[index];
    await page.evaluate(value => { (window as unknown as FixtureWindow).__razorpayMalformed = value; }, mode);
    const claim = verifyAnswer(page);
    await page.getByTestId('payment-razorpay-pay').click();
    const response = await claim;
    expect(response.status()).toBe(200);
    expect(response.request().postDataJSON()).toEqual({ launcherOutcome: 'Success' });
    const answer = await response.json();
    expect([answer.payment, answer.fulfilment, answer.launch]).toEqual(['PendingVerification', 'NotFulfilled', null]);
    await expect(page.getByTestId('payment-pending')).toBeVisible();
    await expect(page.getByTestId('payment-confirmed')).toHaveCount(0);
    await page.screenshot({ path: join(evidence, 'malformed-' + mode + '-pending-desktop.png'), fullPage: true });
    answers.push({ mode, body: response.request().postDataJSON(), payment: answer.payment, fulfilment: answer.fulfilment });
  }
  observe('malformed', { callbacks: answers, confirmed: false });
});

test('@malformed-recheck Check again settles the original captured order once', async ({ browser }) => {
  const { page } = await open(browser);
  await expect(page.getByTestId('payment-pending')).toBeVisible();
  await expect(page.getByTestId('payment-razorpay-pay')).toHaveCount(0);
  const check = verifyAnswer(page);
  await page.getByTestId('payment-check').click();
  const answer = await check;
  expect(answer.request().postDataJSON()).toEqual({ launcherOutcome: null });
  await expect(page.getByTestId('payment-confirmed')).toHaveText('Payment confirmed');
  await page.screenshot({ path: join(evidence, 'malformed-recheck-confirmed-desktop.png'), fullPage: true });
  observe('malformed-recheck', { confirmed: true, originalOrder: (await answer.json()).attemptId !== null });
});
