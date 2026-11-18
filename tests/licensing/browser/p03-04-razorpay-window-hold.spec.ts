import { join } from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { evidence, handOff, observe, open, route, savedState, verifyAnswer, type FixtureWindow } from './p03-04-razorpay-fixture';

/**
 * R010 Chromium phase: two pages of one browser context share the saved checkout and the window lock. Page A's hosted
 * window stays open (its callback held) while page B's proof-free recheck finds the provider order idle; the provider
 * then authorizes in A's window and A reloads with no callback. The provider is the xUnit side's fixture transport.
 */
test('@window-hold an open window keeps its hold against another page\'s idle recheck and a callback-less reload', async ({ browser }) => {
  const { page: pageA } = await open(browser);
  const started = pageA.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/payment/attempts'));
  await pageA.getByRole('button', { name: 'Continue to payment' }).click();
  expect((await started).status()).toBe(201);
  const pageB = await pageA.context().newPage();
  await pageB.goto(route);
  await expect(pageB.getByTestId('payment-razorpay-pay')).toBeVisible();

  // A opens the hosted window; its callback is held. B restores the pending claim and rechecks while A is open.
  await pageA.evaluate(() => { (window as unknown as FixtureWindow).__razorpayHold = true; });
  const idleRecheck = verifyAnswer(pageB);
  await pageA.getByTestId('payment-razorpay-pay').click();
  await expect.poll(() => pageA.evaluate(() => typeof (window as unknown as FixtureWindow).__razorpayComplete)).toBe('function');
  const idle = await idleRecheck;
  const idleAnswer = await idle.json();
  expect(idle.request().postDataJSON()).toEqual({ launcherOutcome: null });
  expect(idleAnswer.payment).toBe('AwaitingPayment');
  expect(await savedState(pageB)).toBe('pending');
  await expectNoPay(pageB, true);
  await pageB.screenshot({ path: join(evidence, 'window-hold-idle-recheck-desktop.png'), fullPage: true });
  await handOff('window-hold-open', 'window-hold-authorized');

  // The provider authorized a payment in A's window; A reloads before the handler or ondismiss fires.
  await expectNoPay(pageB, true);
  const reloadRecheck = verifyAnswer(pageA);
  await pageA.reload();
  const authorized = await reloadRecheck;
  const authorizedAnswer = await authorized.json();
  expect(authorized.request().postDataJSON()).toEqual({ launcherOutcome: null });
  expect([authorizedAnswer.payment, authorizedAnswer.launch]).toEqual(['PendingVerification', null]);
  for (const page of [pageA, pageB]) await expectNoPay(page, true);
  await pageA.screenshot({ path: join(evidence, 'window-hold-reloaded-authorized-desktop.png'), fullPage: true });
  await handOff('window-hold-pending', 'window-hold-captured');

  const settled = verifyAnswer(pageA);
  await pageA.getByTestId('payment-check').click();
  expect((await settled).request().postDataJSON()).toEqual({ launcherOutcome: null });
  for (const page of [pageA, pageB]) {
    await expect(page.getByTestId('payment-confirmed')).toHaveText('Payment confirmed');
    await expectNoPay(page, false);
  }
  await pageB.screenshot({ path: join(evidence, 'window-hold-captured-desktop.png'), fullPage: true });
  observe('window-hold', {
    idleRecheckBody: idle.request().postDataJSON(), idleRecheckAnswer: idleAnswer.payment, savedAfterIdleRecheck: 'pending',
    afterReloadWithoutCallback: authorizedAnswer.payment, payOfferedDuringLiveHold: false, payOfferedWhileAuthorized: false,
    confirmedOnBothPages: true,
  });
});

async function expectNoPay(page: Page, pending: boolean) {
  await expect(page.getByTestId('payment-razorpay-pay')).toHaveCount(0);
  if (pending) await expect(page.getByTestId('payment-pending')).toBeVisible();
}
