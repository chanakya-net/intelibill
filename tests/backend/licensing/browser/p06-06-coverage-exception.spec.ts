import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page } from '@playwright/test';
import { fillSecret, otp } from './p01-12-fixture';

function env(name: string): string {
  const v = process.env['P0606_' + name];
  if (!v) throw new Error('Missing P0606_' + name);
  return v;
}

const merchantWeb = env('MERCHANT_WEB');
const adminWeb = env('ADMIN_WEB');
const scratch = env('TEMPORARY');
const evidence = env('EVIDENCE_DIR');
const account = env('ACCOUNT');
const term = env('TERM');
const northId = env('OUTGOING');
const southId = env('INCOMING');
const secrets = JSON.parse(readFileSync(join(scratch, 'sessions.json'), 'utf8')) as Record<string, string>;

function signal(name: string) { writeFileSync(join(scratch, name + '.txt'), name); }
async function waitFor(name: string) {
  await expect.poll(() => existsSync(join(scratch, name + '.txt')), { timeout: 60_000 }).toBe(true);
}

async function loginMerchant(browser: Browser, email: string): Promise<Page> {
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'en-IN', timezoneId: 'Asia/Kolkata' });
  const page = await context.newPage();
  await page.goto(merchantWeb + '/login');
  await page.locator('#identifier').fill(email);
  await page.locator('#password').fill(secrets['merchantPassword']);
  await page.locator('button[type=submit]').click();
  await expect(page).not.toHaveURL(/\/login/);
  return page;
}

async function loginAdmin(browser: Browser): Promise<Page> {
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'en-IN', timezoneId: 'Asia/Kolkata' });
  const page = await context.newPage();
  await page.goto(adminWeb + '/auth/sign-in');
  await page.getByLabel('Email', { exact: true }).fill(secrets['admin']);
  await fillSecret(page, 'Password', secrets['adminPassword']);
  await fillSecret(page, 'Authenticator code', otp(secrets['seed'], Math.floor(Number(secrets['adminTime']) / 30)));
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).not.toHaveURL(/\/auth\/sign-in/);
  return page;
}

test('Review early coverage replacement end-to-end across merchant and admin shipping builds', async ({ browser }) => {
  // 1. Billing Owner requests early replacement with CourtesyBridge on shipping Merchant Web.
  const payer = await loginMerchant(browser, secrets['payer']);
  await payer.goto(`${merchantWeb}/billing/accounts/${account}/subscriptions/${term}/early-replacements`);
  await expect(payer.getByTestId('early-unknown-queues')).toContainText(/unknown/i);
  await payer.getByTestId('early-outgoing').selectOption(northId);
  await payer.getByTestId('early-incoming').selectOption(southId);
  await expect(payer.getByTestId('early-observed-permit')).toBeVisible();
  await payer.getByTestId('early-disposition').selectOption('CourtesyBridge');
  await payer.getByTestId('early-reason').fill('Branch relocating early before lease handback');
  for (const box of await payer.getByRole('checkbox').all()) {
    await expect(payer.getByTestId('early-submit')).toBeDisabled();
    await box.check();
  }
  await payer.screenshot({ path: join(evidence, '01-merchant-request.png'), fullPage: true });
  await payer.getByTestId('early-submit').click();
  await expect(payer.getByTestId('early-item-state')).toContainText('AwaitingConsents');

  const outLink = await payer.getByTestId('early-outgoing-consent-link').getAttribute('href');
  const inLink = await payer.getByTestId('early-incoming-consent-link').getAttribute('href');
  expect(outLink).toBeTruthy();
  expect(inLink).toBeTruthy();

  // 2. Outgoing Owner (North) and Incoming Owner (South) give distinct explicit consents.
  const north = await loginMerchant(browser, secrets['north']);
  await north.goto(new URL(outLink!, merchantWeb).href);
  await expect(north.getByTestId('early-owner-heading')).toContainText('Outgoing: North');
  await north.getByTestId('early-grant').click();
  await north.getByTestId('early-accept').click();
  await expect(north.getByTestId('early-owner-status')).toContainText('Accepted');

  const south = await loginMerchant(browser, secrets['south']);
  await south.goto(new URL(inLink!, merchantWeb).href);
  await expect(south.getByTestId('early-owner-heading')).toContainText('Incoming: South');
  await south.getByTestId('early-grant').click();
  await south.getByTestId('early-accept').click();
  await expect(south.getByTestId('early-owner-status')).toContainText('Accepted');
  await south.screenshot({ path: join(evidence, '02-owner-consent.png'), fullPage: true });

  signal('consented');
  await waitFor('ready-for-review');

  // 3. Licensing Manager signs in with real MFA on shipping Admin Web, previews, approves with dropped response,
  // reloads, and recovers the committed approval by its preserved original operation key.
  const admin = await loginAdmin(browser);
  await admin.goto(`${adminWeb}/accounts/${account}/coverage-exceptions`);
  await expect(admin.getByTestId('exception-row')).toHaveCount(1);
  await admin.getByTestId('exception-select').click();
  await expect(admin.getByTestId('exception-preview')).toBeVisible();
  await expect(admin.getByTestId('exception-unknown-queues')).toContainText(/unknown/i);
  await expect(admin.getByTestId('exception-disposition')).toContainText('Separate auditable courtesy grant');
  await expect(admin.getByTestId('exception-outgoing-consent')).toHaveText('Accepted');
  await expect(admin.getByTestId('exception-incoming-consent')).toContainText('Accepted');
  await admin.screenshot({ path: join(evidence, '03-admin-preview.png'), fullPage: true });

  await admin.getByTestId('exception-reason').fill('Verified relocation documents and all three consents');
  await admin.getByTestId('exception-confirm-check').check();

  // Drop the response ONLY after the real Licensing API commits it, simulating a lost response in transit.
  let droppedOnce = false;
  await admin.route('**/coverage-replacement-exceptions/*/approve', async route => {
    if (!droppedOnce) {
      droppedOnce = true;
      const upstream = await route.fetch();
      expect(upstream.status()).toBe(201);
      await route.abort('failed');
      return;
    }
    await route.continue();
  });

  await admin.getByTestId('exception-approve').click();
  await expect(admin.getByTestId('exception-pending')).toBeVisible();

  // Reload and recover using the preserved original operation key.
  await admin.reload();
  await admin.getByTestId('exception-select').click();
  await expect(admin.getByTestId('exception-pending')).toBeVisible();
  await admin.getByTestId('exception-check').click();
  await expect(admin.getByTestId('exception-receipt')).toBeVisible();
  await expect(admin.getByTestId('exception-pending')).toHaveCount(0);
  await admin.screenshot({ path: join(evidence, '04-admin-recovered.png'), fullPage: true });

  signal('approved');
  await waitFor('applying');

  // 4. Before signed merchant acknowledgements arrive, Admin Web shows Applying (not Acknowledged) plus linked courtesy grant ID.
  await admin.getByTestId('exception-refresh').click();
  await expect(admin.getByTestId('exception-receipt-state')).toHaveText('Applied');
  await expect(admin.getByTestId('exception-courtesy-grant')).toBeVisible();
  await expect(admin.getByTestId('exception-leg-courtesy')).toContainText('Applying');
  await admin.screenshot({ path: join(evidence, '05-admin-applying.png'), fullPage: true });

  signal('saw-applying');
  await waitFor('courtesy-acked');

  // 5. After acknowledgements arrive during [C, B): Outgoing + Courtesy Acknowledged, Incoming Paid Waiting.
  await admin.getByTestId('exception-refresh').click();
  await expect(admin.getByTestId('exception-leg-outgoing')).toContainText('Acknowledged');
  await expect(admin.getByTestId('exception-leg-courtesy')).toContainText('Acknowledged');
  await expect(admin.getByTestId('exception-leg-incoming')).toContainText('Waiting');

  signal('advance-cutover');
  await waitFor('cutover-acked');

  // 6. At cutover B: Incoming Paid Acknowledged, Courtesy Window ended.
  await admin.getByTestId('exception-refresh').click();
  await expect(admin.getByTestId('exception-leg-incoming')).toContainText('Acknowledged');
  await expect(admin.getByTestId('exception-leg-courtesy')).toContainText('Window ended');
  await admin.screenshot({ path: join(evidence, '06-admin-cutover-acked.png'), fullPage: true });
});
