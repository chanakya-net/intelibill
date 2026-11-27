import { join } from 'node:path';
import { expect, test } from '@playwright/test';
import { evidence, handOff, observe, open, verifyAnswer, type FixtureWindow } from './p03-04-razorpay-fixture';

test('@webhook-reload an already verified payment survives reload without resending payment', async ({ browser }) => {
  const { page } = await open(browser);
  const started = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/payment/attempts'));
  await page.getByRole('button', { name: 'Continue to payment' }).click();
  expect((await started).status()).toBe(201);
  await page.evaluate(() => { (window as unknown as FixtureWindow).__razorpayHold = true; });
  await page.getByTestId('payment-razorpay-pay').click();
  await expect.poll(() => page.evaluate(() => typeof (window as unknown as FixtureWindow).__razorpayComplete)).toBe('function');
  await handOff('webhook-opened', 'webhook-authorized');

  const recheck = verifyAnswer(page);
  await page.reload();
  expect((await (await recheck).json()).payment).toBe('PendingVerification');
  await expect(page.getByTestId('payment-pending')).toBeVisible();
  await expect(page.getByTestId('payment-razorpay-pay')).toHaveCount(0);
  await page.screenshot({ path: join(evidence, 'webhook-pending-desktop.png'), fullPage: true });

  // The server records the webhook and proves capture while this tab still has its pending draft.
  await handOff('webhook-pending', 'webhook-verified');
  const paymentPosts: string[] = [];
  page.on('request', request => {
    const path = new URL(request.url()).pathname;
    if (request.method() === 'POST' && path.includes('/payment/')) paymentPosts.push(path);
  });
  await page.reload();
  await expect(page.getByTestId('payment-confirmed')).toHaveText('Payment confirmed');
  await expect(page.getByTestId('payment-applying')).toContainText('Applying access to 0 of 1 shops');
  await expect(page.getByTestId('payment-razorpay-pay')).toHaveCount(0);
  await page.screenshot({ path: join(evidence, 'webhook-applying-desktop.png'), fullPage: true });

  await handOff('webhook-applying', 'webhook-applied');
  await page.reload();
  await expect(page.getByTestId('payment-applied')).toHaveText('Access applied to all 1 shops');
  await expect(page.getByTestId('payment-applying')).toHaveCount(0);
  await expect(page.getByTestId('payment-razorpay-pay')).toHaveCount(0);
  await page.screenshot({ path: join(evidence, 'webhook-applied-desktop.png'), fullPage: true });
  expect(paymentPosts).toEqual([]);
  observe('webhook-reload', { pendingBeforeWebhook: true, confirmedOnReload: true, applying: '0 of 1',
    applied: '1 of 1', paymentPostsAfterWebhook: paymentPosts });
});
