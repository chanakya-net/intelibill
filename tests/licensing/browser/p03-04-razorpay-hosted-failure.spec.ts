import { join } from 'node:path';
import { expect, test } from '@playwright/test';
import { evidence, handOff, observe, open, options, verifyAnswer, type FixtureWindow } from './p03-04-razorpay-fixture';

/**
 * R018 Chromium phase: the hosted checkout reports a failed payment. Its own retry is disabled, the window closes and
 * the original order is checked proof-free; a recent failure keeps Pay hidden until the late payment is captured.
 */
test('@hosted-failure a hosted payment failure is checked server-side and never offers a second debit', async ({ browser }) => {
  const { page } = await open(browser);
  const started = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/payment/attempts'));
  await page.getByRole('button', { name: 'Continue to payment' }).click();
  expect((await started).status()).toBe(201);
  await page.evaluate(() => { (window as unknown as FixtureWindow).__razorpayFail = true; });
  await page.getByTestId('payment-razorpay-pay').click();
  await expect.poll(() => page.evaluate(() => typeof (window as unknown as FixtureWindow).__razorpayFailNow)).toBe('function');
  const launched = await options(page);
  expect(launched?.['retry']).toEqual({ enabled: false });
  await handOff('hosted-failure-open', 'hosted-failure-failed');

  // The provider recorded a failed payment on the order; the hosted window reports it.
  const check = verifyAnswer(page);
  await page.evaluate(() => (window as unknown as FixtureWindow).__razorpayFailNow!());
  const failed = await check;
  expect(failed.request().postDataJSON()).toEqual({ launcherOutcome: null });
  const answer = await failed.json();
  expect([answer.payment, answer.fulfilment, answer.launch]).toEqual(['PendingVerification', 'NotFulfilled', null]);
  expect(await page.evaluate(() => (window as unknown as FixtureWindow).__razorpayClosed)).toBe(true);
  await expect(page.getByTestId('payment-pending')).toBeVisible();
  await expect(page.getByTestId('payment-razorpay-pay')).toHaveCount(0);
  await page.screenshot({ path: join(evidence, 'hosted-failure-pending-desktop.png'), fullPage: true });

  // A later proof-free check while the failure is recent still offers nothing.
  const again = verifyAnswer(page);
  await page.getByTestId('payment-check').click();
  const recheck = await again;
  expect(recheck.request().postDataJSON()).toEqual({ launcherOutcome: null });
  expect((await recheck.json()).launch).toBeNull();
  await expect(page.getByTestId('payment-razorpay-pay')).toHaveCount(0);
  await handOff('hosted-failure-checked', 'hosted-failure-authorized');

  const authorized = verifyAnswer(page);
  await page.getByTestId('payment-check').click();
  const held = await (await authorized).json();
  expect([held.payment, held.launch]).toEqual(['PendingVerification', null]);
  await expect(page.getByTestId('payment-razorpay-pay')).toHaveCount(0);
  await handOff('hosted-failure-authorized-checked', 'hosted-failure-captured');

  const settled = verifyAnswer(page);
  await page.getByTestId('payment-check').click();
  expect((await settled).request().postDataJSON()).toEqual({ launcherOutcome: null });
  await expect(page.getByTestId('payment-confirmed')).toHaveText('Payment confirmed');
  await page.screenshot({ path: join(evidence, 'hosted-failure-captured-desktop.png'), fullPage: true });
  observe('hosted-failure', { retry: launched?.['retry'], launchedOrder: launched?.['order_id'],
    failureCheckBody: failed.request().postDataJSON(), afterFailure: answer.payment, windowClosed: true,
    payOfferedAfterFailure: false, afterLateAuthorization: held.payment, confirmed: true });
});
