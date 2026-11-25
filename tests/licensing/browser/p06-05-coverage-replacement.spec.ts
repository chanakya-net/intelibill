import { existsSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page } from '@playwright/test';

function env(name: string) {
  const value = process.env['P0605_' + name];
  if (!value) throw new Error('Missing fixture ' + name);
  return value;
}

const evidence = env('EVIDENCE_DIR');
async function login(browser: Browser, who: string) {
  const page = await browser.newPage({ locale: 'en-IN', timezoneId: 'Asia/Kolkata' });
  await page.goto(env('WEB') + '/login');
  await page.locator('#identifier').fill(env(who));
  await page.locator('#password').fill(env('PASSWORD'));
  await page.locator('button[type=submit]').click();
  await expect(page).not.toHaveURL(/\/login/);
  return page;
}

function signal(marker: string) { writeFileSync(join(evidence, marker + '.txt'), marker); }
async function waitFor(marker: string) {
  await expect.poll(() => existsSync(join(evidence, marker + '.txt')), { timeout: 30_000 }).toBe(true);
}

async function purchaseRenewal(payer: Page, billing: string) {
  await payer.goto(billing + '/checkout');
  await payer.getByLabel('Plan', {exact: true}).selectOption(env('PLAN'));
  await payer.locator('#checkout-renewal').selectOption(env('TERM'));
  await payer.getByRole('button', { name: 'Create quote' }).click();
  await expect(payer.getByTestId('replacement-quote')).toBeVisible();
  await expect(payer.getByTestId('checkout-covered-shop')).toHaveCount(3);
  await expect(payer.getByTestId('checkout-covered-shop').getByText('South')).toBeVisible();
  await expect(payer.getByTestId('checkout-covered-shop').getByText('North')).toHaveCount(0);
  await payer.getByRole('checkbox', { name: /I accept the full amount/ }).check();
  await payer.getByRole('button', { name: 'Accept quote' }).click();
  await payer.getByRole('button', { name: 'Continue to payment' }).click();
  await payer.getByTestId('payment-simulator-capture').click();
  await expect(payer.getByTestId('payment-confirmed')).toHaveText('Payment confirmed');
}

test('North keeps this term and South waits for renewal and merchant confirmation', async ({ browser }) => {
  const payer = await login(browser, 'PAYER');
  const billing = env('WEB') + '/billing/accounts/' + env('ACCOUNT');
  await payer.goto(billing + '/status');
  await payer.locator('[data-testid="account-term"]:has([data-term-id="' + env('TERM') + '"])').getByTestId('replacement-open').click();
  await expect(payer).toHaveURL(/\/replacements$/);
  const replacement = payer.url();
  await payer.getByTestId('replacement-outgoing').selectOption({ label: 'North' });
  await payer.getByTestId('replacement-incoming').selectOption({ label: 'South' });
  await expect(payer.getByTestId('replacement-permit')).toContainText('16 Feb 2028, 10:00:00 IST');
  await expect(payer.getByTestId('replacement-boundary')).toHaveText('29 Feb 2028, 10:00:00 IST');
  for (const box of await payer.getByRole('checkbox').all()) {
    await expect(payer.getByTestId('replacement-confirm')).toBeDisabled();
    await box.check();
  }
  await payer.screenshot({ path: join(evidence, 'selection.png'), fullPage: true });
  const request = payer.waitForRequest(r => r.method() === 'POST' && r.url().endsWith('/coverage-replacements'));
  await payer.getByTestId('replacement-confirm').click();
  expect((await request).postDataJSON()).toMatchObject({ outgoingShopId: env('OUTGOING'), incomingShopId: env('SHOP'),
    acknowledgedShopSelection: true, acknowledgedRenewal: true, acknowledgedPermitWait: true });
  const status = payer.getByTestId('replacement-status');
  await expect(status).toHaveText('Awaiting shop owner consent');
  await payer.reload();
  const link = await payer.getByTestId('replacement-consent-link').getAttribute('href');
  const owner = await login(browser, 'OWNER');
  await owner.goto(new URL(link!, env('WEB')).href);
  await expect(owner.getByRole('heading', { name: 'South' })).toBeVisible();
  await owner.getByTestId('replacement-grant').click();
  await owner.getByTestId('replacement-accept').click();
  await expect(owner.getByRole('status')).toHaveText(/Accepted|Scheduled for renewal/);
  signal('accepted');
  await waitFor('scheduled');
  await payer.getByTestId('replacement-refresh').click();
  await expect(status).toHaveText('Scheduled for renewal');
  await owner.getByTestId('replacement-refresh').click();
  await expect(owner.getByRole('status')).toHaveText('Scheduled for renewal');
  await purchaseRenewal(payer, billing);
  await payer.goto(replacement);
  await expect(status).toHaveText('Renewal verified. Waiting for existing permission to end.');
  await payer.screenshot({ path: join(evidence, 'pending.png'), fullPage: true });
  signal('paid');
  await waitFor('merchant-committed');
  await payer.getByTestId('replacement-refresh').click();
  await expect(status).toHaveText('Activating coverage');
  signal('applying');
  await waitFor('delivered');
  await payer.getByTestId('replacement-refresh').click();
  await expect(status).toHaveText('Active, confirmed by the shop');
  await expect(payer.getByText('North → South')).toBeVisible();
  await owner.getByTestId('replacement-refresh').click();
  await expect(owner.getByRole('status')).toHaveText('Active, confirmed by the shop');
  await payer.screenshot({ path: join(evidence, 'acknowledged.png'), fullPage: true });
});
