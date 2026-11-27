import { createHash } from 'node:crypto';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page } from '@playwright/test';
import { fillSecret, otp } from './p01-12-fixture';

const scratch = process.env['P0607_TEMPORARY']!;
const merchant = process.env['P0607_MERCHANT_WEB']!;
const admin = process.env['P0607_ADMIN_WEB']!;
const evidence = process.env['P0607_EVIDENCE_DIR']!;
const f = JSON.parse(readFileSync(join(scratch, 'sessions.json'), 'utf8'));
async function checkpoint(signal: string, ack: string) {
  writeFileSync(join(scratch, signal), signal);
  await expect.poll(() => existsSync(join(scratch, ack)), { timeout: 60_000 }).toBe(true);
}
async function login(browser: Browser, email: string) {
  const page = await browser.newPage();
  await page.goto(merchant + '/login');
  await page.locator('#identifier').fill(email);
  await page.locator('#password').fill(f.merchantPassword);
  await page.locator('button[type=submit]').click();
  await expect(page).not.toHaveURL(/\/login/);
  return page;
}

async function consent(browser: Browser, payer: Page, receiver: Page) {
  await payer.goto(merchant + '/billing/accounts/' + f.sourceId + '/subscriptions/' + f.term + '/transfers');
  await payer.getByTestId('transfer-shop').selectOption(f.shop);
  await payer.getByTestId('transfer-destination').fill(f.destinationId);
  await payer.getByTestId('transfer-treatment').selectOption('ReviewedAllocation');
  await payer.getByTestId('transfer-cutover').fill(new Date(f.cutover).toISOString());
  await payer.getByTestId('transfer-preview').click();
  await expect(payer.getByTestId('transfer-review-shop')).toContainText('North');
  await payer.getByTestId('transfer-reason').fill('Transfer North to Destination Company');
  for (const box of await payer.getByRole('checkbox').all()) await box.check();
  await payer.getByTestId('transfer-submit').click();
  await payer.getByTestId('transfer-accept-SourceBillingOwner').click();
  await expect(payer.getByTestId('transfer-progress')).toContainText('1 of 3');
  const link = (await payer.getByTestId('transfer-shop-link').getAttribute('href'))!;
  await receiver.goto(merchant + '/billing/accounts/' + f.destinationId + '/transfers');
  await receiver.getByTestId('transfer-accept-DestinationBillingOwner').click();
  await expect(receiver.getByTestId('transfer-progress')).toContainText('2 of 3');
  const owner = await login(browser, f.shopOwner);
  await owner.goto(merchant + link);
  await expect(owner.getByTestId('transfer-parties')).toContainText('Destination Company');
  await owner.getByTestId('transfer-accept-ShopOwner').click();
  await expect(owner.getByTestId('transfer-owner-status')).toHaveAttribute('data-status', 'Accepted');
  await owner.screenshot({ path: join(evidence, '01-shop-owner.png') });
  await checkpoint('consented', 'ready');
  await payer.reload();
  await expect(payer.getByTestId('transfer-progress')).toContainText('3 of 3');
  return link.split('/').at(-1)!;
}

async function approve(browser: Browser, transfer: string) {
  const page = await browser.newPage();
  await page.goto(admin + '/auth/sign-in');
  await page.getByLabel('Email', { exact: true }).fill(f.admin);
  await fillSecret(page, 'Password', f.adminPassword);
  await fillSecret(page, 'Authenticator code', otp(f.seed, Math.floor(Number(f.adminTime) / 30)));
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).not.toHaveURL(/\/auth\/sign-in/);
  await page.goto(admin + '/accounts/' + f.sourceId + '/coverage-transfers?transfer=' + transfer);
  const allocation = page.getByTestId('transfer-allocation');
  for (const text of ['North', 'Renewal Fixture', 'Destination Company', '29 Feb', '14 Mar', 'Historical bills'])
    await expect(allocation).toContainText(text);
  await page.getByLabel('Approval reason').fill('All named owners agreed to the funded interval');
  await page.getByRole('checkbox').check();
  await page.screenshot({ path: join(evidence, '02-manager-review.png'), fullPage: true });
  await page.route('**/approve', async route => {
    const response = await route.fetch();
    expect(response.status()).toBe(201);
    await route.abort('connectionreset');
  });
  await page.getByRole('button', { name: 'Approve transfer' }).click();
  await expect(page.getByRole('alert')).toContainText('Outcome unconfirmed');
  const key = await page.getByText(/Saved approval ·/).textContent();
  await page.reload();
  await expect(page.getByText(/Saved approval ·/)).toHaveText(key!);
  const check = page.getByRole('button', { name: 'Check saved decision' });
  await check.click();
  await expect(page.getByTestId('transfer-outcome')).toContainText('Preparing');
  await checkpoint('preparing', 'scheduled');
  await check.click();
  await expect(page.getByTestId('transfer-outcome')).toContainText('Scheduled');
  await checkpoint('advance', 'activated');
  await check.click();
  await expect(page.getByTestId('transfer-outcome')).toContainText('Applied');
  await page.screenshot({ path: join(evidence, '03-applied.png'), fullPage: true });
}

test('three owners consent, the manager recovers the approved transfer, and original bills stay private', async ({ browser }) => {
  const payer = await login(browser, f.payer);
  const receiver = await login(browser, f.destination);
  await approve(browser, await consent(browser, payer, receiver));
  await payer.goto(merchant + '/billing/accounts/' + f.sourceId + '/checkout?order=' + f.order);
  await payer.getByTestId('billing-documents-list').click();
  await expect(payer.getByTestId('billing-document-hash')).toHaveText(f.hash);
  const downloaded = payer.waitForResponse(r => r.url().endsWith(f.content));
  await payer.getByTestId('billing-document-download').click();
  const pdf = await downloaded;
  expect(pdf.status()).toBe(200);
  expect(pdf.headers()['cache-control']).toContain('no-store');
  expect(createHash('sha256').update(await pdf.body()).digest('hex')).toBe(f.hash);
  const listing = receiver.waitForRequest(r => r.url().includes('/api/billing/accounts/' + f.destinationId + '/coverage-transfers'));
  await receiver.reload();
  const authorization = (await listing).headers()['authorization'];
  await expect(receiver.getByTestId('transfer-state')).toHaveAttribute('data-state', 'Applied');
  await expect(receiver.getByTestId('transfer-parties')).toContainText('Destination Company');
  await receiver.screenshot({ path: join(evidence, '04-receiving-owner.png'), fullPage: true });
  const blocked = await receiver.request.get(merchant + f.content, { headers: { authorization } });
  expect(blocked.status()).toBe(403);
  const guessed = await receiver.request.get(merchant + f.content.replace(f.sourceId, f.destinationId), { headers: { authorization } });
  expect(guessed.status()).toBe(404);
  expect(await guessed.text()).not.toContain('%PDF');
  writeFileSync(join(evidence, 'document-access.json'), JSON.stringify({ original: 200, receiver: 403, guessed: 404, sha256: f.hash }));
});
