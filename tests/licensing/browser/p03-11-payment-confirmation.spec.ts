import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { fillSecret, otp } from './p01-12-fixture';

function required(name: string) {
  const value = process.env['P0311_' + name];
  if (!value) throw new Error('Missing confirmation variable ' + name);
  return value;
}
const merchant = required('MERCHANT_WEB');
const admin = required('ADMIN_WEB');
const scratch = required('TEMPORARY');
const evidence = required('EVIDENCE_DIR');
const account = required('ACCOUNT');
const order = required('ORDER');
const other = required('OTHER_ACCOUNT');
const checkout = merchant + '/billing/accounts/' + account + '/checkout?order=' + order;
const secrets = JSON.parse(readFileSync(join(scratch, 'sessions.json'), 'utf8'));
const observations: object[] = [];
let merchantAuthorization = "";

async function screenshots(page: Page, name: string) {
  for (const [label, width, height] of [['desktop', 1440, 1000], ['mobile', 390, 844]] as const) {
    await page.setViewportSize({ width, height });
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: join(evidence, name + '-' + label + '.png'), fullPage: true });
  }
}
async function signInMerchant(page: Page) {
  await page.goto(merchant + '/login');
  await page.locator('#identifier').fill(secrets.merchant);
  await page.locator('#password').evaluate((input: HTMLInputElement, value: string) => {
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }, secrets.merchantPassword);
  await page.locator('button[type=submit]').click();
  await expect(page).not.toHaveURL(/\/login/);
}
async function signInAdmin(page: Page) {
  await page.goto(admin + '/auth/sign-in');
  await page.getByLabel('Email', { exact: true }).fill(secrets.admin);
  await fillSecret(page, 'Password', secrets.adminPassword);
  await fillSecret(page, 'Authenticator code', otp(secrets.seed, Math.floor(Number(readFileSync(join(scratch, 'mutated'), 'utf8')) / 30)));
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Manual payments', exact: true })).toBeVisible();
}
async function download(page: Page, testId: string, file: string) {
  const response = page.waitForResponse(r => r.url().includes('/document') && r.request().method() === 'GET');
  const downloaded = page.waitForEvent('download');
  await page.getByTestId(testId).click();
  const attachment = await downloaded;
  expect(attachment.suggestedFilename()).toMatch(/^payment-confirmation-[\da-f-]+\.txt$/);
  await attachment.saveAs(join(evidence, file));
  const received = await response;
  expect(received.status()).toBe(200);
  if (received.url().startsWith(merchant)) merchantAuthorization = received.request().headers()['authorization'];
  expect(received.headers()['cache-control']).toContain('no-store');
  expect(received.headers()['content-disposition']).toContain('attachment');
  expect(received.headers()['x-content-type-options'].split(',').map(value => value.trim()).every(value => value === 'nosniff')).toBe(true);
  observations.push({ file, status: 200, noStore: true, attachment: attachment.suggestedFilename() });
  return readFileSync(join(evidence, file));
}

test('authenticated immutable confirmation from simulator fixture through real services', async ({ browser }) => {
  const login = await browser.newContext();
  const loginPage = await login.newPage();
  await signInMerchant(loginPage);
  const state = await login.storageState();
  await login.close();
  const customer = await browser.newContext({ storageState: state, recordVideo: { dir: join(evidence, 'demo-merchant') } });
  const payer = await customer.newPage();
  await payer.goto(checkout);
  await expect(payer.getByTestId('payment-confirmed')).toBeVisible();
  await payer.getByTestId('confirmation-list').click();
  await expect(payer.getByTestId('confirmation-download')).toBeVisible();
  const original = await download(payer, 'confirmation-download', 'merchant-original.txt');
  expect(original.toString('utf8')).toContain('PAYMENT CONFIRMATION\n');
  expect(original.toString('utf8')).toContain('not a tax invoice');
  expect(original.toString('utf8')).toContain('not a merchant sale receipt');
  await screenshots(payer, 'merchant-before');
  writeFileSync(join(scratch, 'downloaded'), '');
  await expect.poll(() => existsSync(join(scratch, 'mutated')), { timeout: 90_000 }).toBe(true);
  await payer.reload();
  await payer.getByTestId('confirmation-list').click();
  expect(await download(payer, 'confirmation-download', 'merchant-after-edits.txt')).toEqual(original);
  await expect(payer.getByTestId('payment-applied')).toBeVisible();
  await screenshots(payer, 'merchant-after');
  const id = /Confirmation: ([\da-f-]+)/.exec(original.toString('utf8'))![1];
  // Same authenticated billing member owns another account: a foreign document under that account still fails.
  expect(merchantAuthorization).toMatch(/^Bearer /);
  const refusal = await payer.request.get(`${merchant}/api/billing/accounts/${other}/orders/${order}/payment/confirmations/${id}/document`,
    { headers: { Authorization: merchantAuthorization } });
  const foreign = refusal.status();
  expect(foreign).toBe(404);
  await payer.goto(merchant + '/billing/accounts/' + other + '/checkout?order=' + order);
  await expect(payer.getByTestId('confirmation-download')).toHaveCount(0);
  observations.push({ crossAccountStatus: foreign });
  await customer.close();

  const adminLogin = await browser.newContext();
  const adminLoginPage = await adminLogin.newPage();
  await signInAdmin(adminLoginPage);
  const adminState = await adminLogin.storageState();
  await adminLogin.close();
  const operator = await browser.newContext({ storageState: adminState, recordVideo: { dir: join(evidence, 'demo-admin') } });
  const page = await operator.newPage();
  await page.goto(admin + '/payments?account=' + account + '&order=' + order);
  await page.getByTestId('admin-confirmation-list').click();
  expect(await download(page, 'admin-confirmation-download', 'admin-identical.txt')).toEqual(original);
  await screenshots(page, 'admin-confirmation');
  await operator.close();
  writeFileSync(join(evidence, 'observable-demo.json'), JSON.stringify({ simulatorFixture: true, observations }, null, 2));
});
