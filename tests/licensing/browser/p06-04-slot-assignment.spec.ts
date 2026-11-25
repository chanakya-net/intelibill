import { existsSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser } from '@playwright/test';

function env(name: string) {
  const value = process.env['P0604_' + name];
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

test('payer selects North and the shop owner accepts the remaining paid term', async ({ browser }) => {
  const payer = await login(browser, 'PAYER');
  await payer.goto(env('WEB') + '/billing/accounts/' + env('ACCOUNT') + '/status');
  const card = payer.locator('[data-testid="account-term"]:has([data-term-id="' + env('TERM') + '"])');
  await card.getByTestId('slot-open').click();
  await payer.getByTestId('slot-shop').selectOption({ label: 'North' });
  await expect(payer.getByTestId('slot-shared-end')).toHaveText('29 Feb 2028, 10:00:00 IST');
  await expect(payer.getByTestId('slot-confirm')).toBeDisabled();
  await payer.getByRole('checkbox', { name: 'I accept the package end date shown above.' }).check();
  await expect(payer.getByTestId('slot-confirm')).toBeDisabled();
  await payer.getByRole('checkbox', { name: 'No additional fee is due.' }).check();
  const request = payer.waitForRequest(r => r.method() === 'POST' && r.url().endsWith('/slot-assignments'));
  await payer.getByTestId('slot-confirm').click();
  const payerRequest = await request;
  expect(payerRequest.postDataJSON()).toMatchObject({
    shopId: env('SHOP'), observedCapacity: 3, acknowledgedSharedEnd: true, acknowledgedNoNewFee: true,
  });
  await expect(payer.getByTestId('slot-status')).toHaveText('Awaiting shop owner consent');
  await payer.reload();
  const link = await payer.getByTestId('slot-consent-link').getAttribute('href');
  const owner = await login(browser, 'OWNER');
  await owner.goto(new URL(link!, env('WEB')).href);
  await expect(owner.getByRole('heading', { name: 'North' })).toBeVisible();
  await owner.getByTestId('slot-grant').click();
  await owner.getByTestId('slot-accept').click();
  await expect(owner.getByRole('status')).toHaveText('Activating coverage');
  writeFileSync(join(evidence, 'accepted.txt'), 'accepted');

  // Stage 1: before merchant delivery -> both views show Activating coverage.
  await expect.poll(() => existsSync(join(evidence, 'licensed.txt'))).toBe(true);
  await payer.getByTestId('slot-refresh').click();
  await expect(payer.getByTestId('slot-status')).toHaveText('Activating coverage');
  await owner.getByTestId('slot-refresh').click();
  await expect(owner.getByRole('status')).toHaveText('Activating coverage');
  writeFileSync(join(evidence, 'applying.txt'), 'observed');

  // Stage 2: after merchant commit, BEFORE return receipt reaches licensing.
  await expect.poll(() => existsSync(join(evidence, 'merchant-committed.txt'))).toBe(true);
  await payer.getByTestId('slot-refresh').click();
  await expect(payer.getByTestId('slot-status')).toHaveText('Activating coverage');
  await owner.getByTestId('slot-refresh').click();
  await expect(owner.getByRole('status')).toHaveText('Applied on this shop, awaiting billing confirmation');
  writeFileSync(join(evidence, 'committed-observed.txt'), 'observed');

  // Stage 3: after licensing consumes the return receipt -> both views show Active, confirmed by the shop.
  await expect.poll(() => existsSync(join(evidence, 'delivered.txt'))).toBe(true);
  await payer.getByTestId('slot-refresh').click();
  await expect(payer.getByTestId('slot-status')).toHaveText('Active, confirmed by the shop');
  await owner.getByTestId('slot-refresh').click();
  await expect(owner.getByRole('status')).toHaveText('Active, confirmed by the shop');

  const response = await payer.request.post(env('WEB') + '/api/shops/switch', { data: { shopId: env('SHOP') },
    headers: { Authorization: (await payerRequest.headerValue('authorization'))! } });
  expect(response.status()).toBe(403);
  await payer.screenshot({ path: join(evidence, 'acknowledged.png'), fullPage: true });
});
