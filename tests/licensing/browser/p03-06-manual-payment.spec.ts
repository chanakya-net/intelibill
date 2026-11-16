import { randomUUID } from 'node:crypto';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { expect, test, type Page } from '@playwright/test';
import { fillSecret, origin, otp } from './p01-12-fixture';

const scratch = process.env['P0306_TEMPORARY']!;
const evidence = process.env['P0306_EVIDENCE_DIR']!;
const account = process.env['P0306_ACCOUNT']!;
const order = process.env['P0306_ORDER']!;
const amount = Number(process.env['P0306_AMOUNT']);
const payments = `${origin}/payments?account=${account}&order=${order}`;
const api = `/api/admin/accounts/${account}/orders/${order}/manual-payments`;

async function signIn(page: Page, marker: string) {
  await expect.poll(() => existsSync(scratch + '/' + marker)).toBe(true);
  const epoch = Number(readFileSync(scratch + '/' + marker, 'utf8'));
  const { email, password, seed } = JSON.parse(readFileSync(scratch + '/admin.json', 'utf8'));
  await page.getByLabel('Email', { exact: true }).fill(email);
  await fillSecret(page, 'Password', password);
  await fillSecret(page, 'Authenticator code', otp(seed, Math.floor(epoch / 30)));
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
}

async function preview(page: Page) {
  await page.getByLabel('Received amount in paise').fill(String(amount));
  await page.getByLabel('Received at (IST)').fill('2027-02-10T05:30');
  await page.getByLabel('Bank payment reference').fill('BANK-' + randomUUID().replaceAll('-', ''));
  await page.getByLabel('Evidence reference').fill('STATEMENT-FIXTURE-ONLY');
  await page.getByLabel('Reason').fill('Matched full amount against the fixture statement');
  await page.getByRole('button', { name: 'Preview full receipt' }).click();
  const shops = page.getByTestId('preview-shop');
  await expect(shops).toHaveCount(3);
  expect((await shops.allTextContents()).sort()).toEqual(['Manual Central', 'Manual East', 'Manual North']);
  await expect(page.getByRole('button', { name: 'Confirm verified receipt' })).toBeDisabled();
  await expect(page.getByText(new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' }).format(amount / 100), { exact: false }).last()).toBeVisible();
  await page.getByRole('checkbox').check();
  await page.locator('section[aria-labelledby="preview-heading"]').screenshot({ path: evidence + '/full-order-preview.png' });
}

test('Billing reauthenticates, settles one full receipt and recovers the same operation', async ({ page, context }) => {
  await page.goto(origin + '/auth/sign-in');
  await signIn(page, 'clock-initial');
  await expect(page.getByRole('link', { name: 'Manual payments' })).toBeVisible();
  await page.goto(payments);
  await preview(page);
  writeFileSync(scratch + '/age', '');
  await expect.poll(() => existsSync(scratch + '/clock-aged')).toBe(true);

  const stale = page.waitForResponse(response => response.url().endsWith(api) && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Confirm verified receipt' }).click();
  expect((await stale).status()).toBe(403);
  const saved = page.getByText(/^Operation /);
  const operation = (await saved.textContent())!.trim();
  await page.getByRole('link', { name: 'Sign in with MFA' }).click();
  await signIn(page, 'clock-aged');
  await expect(saved).toHaveText(operation);

  let committed = 0;
  await page.route('**' + api, async route => {
    if (route.request().method() !== 'POST') return route.continue();
    committed = (await route.fetch()).status();
    await route.abort();
  });
  await page.getByRole('button', { name: 'Retry original confirmation' }).click();
  await expect(page.getByRole('alert')).toContainText('unresolved');
  expect(committed).toBe(201);
  await page.unroute('**' + api);

  const second = await context.newPage();
  await second.goto(payments);
  await expect(second.getByText(/^Operation /)).toHaveText(operation);
  await page.reload();
  await expect(saved).toHaveText(operation);
  await page.route('**' + api + '/operations/*', route => route.fulfill({ status: 404, contentType: 'application/json', body: '{}' }));
  await page.getByRole('button', { name: 'Check original confirmation' }).click();
  await expect(page.getByRole('alert')).toContainText('unresolved');
  await expect(saved).toHaveText(operation);

  const replay = page.waitForResponse(response => response.url().endsWith(api) && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Retry original confirmation' }).click();
  expect((await replay).status()).toBe(200);
  await expect(page.getByTestId('receipt-result')).toContainText('Manual payment Verified · Fulfilled');
  await expect(page.getByTestId('receipt-audit')).toHaveCount(1);
  await expect(page.getByTestId('order-access')).toHaveText('Access · Applying');
  const evidenceReference = page.getByText('STATEMENT-FIXTURE-ONLY', { exact: true });
  await expect(evidenceReference).toHaveCount(0);
  await page.getByRole('button', { name: 'View verified evidence' }).click();
  await expect(evidenceReference).toBeVisible();
  await page.getByRole('button', { name: 'Hide receipt evidence' }).click();
  await expect(evidenceReference).toHaveCount(0);
  await page.screenshot({ path: evidence + '/desktop-applying.png', fullPage: true });
  writeFileSync(scratch + '/applying', '');

  await expect.poll(() => existsSync(scratch + '/delivered')).toBe(true);
  await page.getByRole('button', { name: 'Refresh order' }).click();
  await expect(page.getByTestId('order-access')).toHaveText('Access · Applied');
  await expect(page.getByTestId('receipt-audit')).toHaveCount(1);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: evidence + '/mobile-applied.png', fullPage: true });
  writeFileSync(evidence + '/observations.json', JSON.stringify({
    frozenShops: 3, exactAmountPaise: amount, staleMfa: 403, committedBeforeResponseLoss: committed,
    lookup404PreservedOperation: true, samePayloadReplay: 200, money: 'Verified', originalSource: 'Manual',
    fulfilment: 'Fulfilled', accessBeforeAck: 'Applying', accessAfterAck: 'Applied',
  }, null, 2));
});
