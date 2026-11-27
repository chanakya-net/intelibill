import { createHash } from 'node:crypto';
import { join } from 'node:path';
import { readFileSync, writeFileSync } from 'node:fs';
import { expect, test, type Page } from '@playwright/test';
import { fillSecret, otp } from './p01-12-fixture';

const origin = process.env['P0611_ADMIN_WEB']!;
const scratch = process.env['P0611_TEMPORARY']!;
const evidence = process.env['P0611_EVIDENCE_DIR']!;
const account = process.env['P0611_ACCOUNT']!;
const order = process.env['P0611_ORDER']!;
const foreignAccount = process.env['P0611_FOREIGN_ACCOUNT']!;
const pdf = readFileSync(join(scratch, 'fixture-invoice.pdf'));
const sha256 = createHash('sha256').update(pdf).digest('hex');
const api = `/api/admin/accounts/${account}/orders/${order}/billing-documents`;
const documents = `${origin}/payments/documents?account=${account}&order=${order}`;

async function signIn(page: Page, identity: string) {
  const { email, password, seed, time } = JSON.parse(readFileSync(join(scratch, identity + '.json'), 'utf8'));
  await page.goto(origin + '/auth/sign-in');
  await page.getByLabel('Email', { exact: true }).fill(email);
  await fillSecret(page, 'Password', password);
  await fillSecret(page, 'Authenticator code', otp(seed, Math.floor(time / 30)));
  const signedIn = page.waitForResponse(reply => reply.url().endsWith('/api/admin/auth/sign-in')
    && reply.request().method() === 'POST');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  const reply = await signedIn;
  expect(reply.status(), 'Fixture sign-in must succeed').toBe(200);
  await expect(page).toHaveURL(origin + '/');
}

async function preview(page: Page, data: Buffer, accept = true) {
  await page.getByLabel('Invoice PDF (up to 10 MiB)').setInputFiles({
    name: 'fixture-invoice.pdf', mimeType: 'application/pdf', buffer: data,
  });
  const button = page.getByRole('button', { name: 'Preview invoice metadata' });
  await button.click();
  if (!accept) return;
  const summary = page.getByRole('region', { name: 'Invoice metadata preview' });
  await expect(summary).toContainText('FIXTURE-INV-114');
  await expect(summary).toContainText('Fixture Accounting');
  await expect(summary).toContainText('2027-03-10');
  await expect(page.locator('iframe, embed, object')).toHaveCount(0);
  const attach = page.getByRole('button', { name: 'Attach accounting invoice' });
  await expect(attach).toBeDisabled();
  await page.getByRole('checkbox').check();
  return attach;
}

async function fillIssuer(page: Page) {
  await page.getByLabel('Issuer name').fill('Fixture Accounting');
  await page.getByLabel('Issuing reference').fill('FIXTURE-INV-114');
  await page.getByLabel('Issue date').fill('2027-03-10');
}

test('Billing reviews metadata, rejects bad content and recovers the saved invoice @attach', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await signIn(page, 'finance');
  await expect(page.getByRole('link', { name: 'Accounting invoices' })).toBeVisible();
  await page.goto(documents);
  const target = page.getByRole('region', { name: 'Accepted order' });
  await expect(target).toContainText(account);
  await expect(target).toContainText(order);
  await rejectFiles(page);
  await fillIssuer(page);
  const attach = await preview(page, pdf);
  await expect(page.getByRole('region', { name: 'Invoice metadata preview' })).toContainText(sha256);
  await page.screenshot({ path: join(evidence, 'desktop-preview.png'), fullPage: true });
  await recoverLostReply(page, () => attach!.click());
  const saved = page.getByRole('region', { name: 'Attached accounting invoice' });
  await expect(saved).toContainText(sha256);
  await expect(saved).toContainText('FIXTURE-INV-114');
  await page.reload();
  await expect(saved).toContainText('fixture-invoice.pdf');
  await expect(saved).toContainText(sha256);
  await expect(page.locator('iframe, embed, object')).toHaveCount(0);
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
  await page.screenshot({ path: join(evidence, 'mobile-saved.png'), fullPage: true, animations: 'disabled' });
  await assertMismatch(page);
});

async function rejectFiles(page: Page) {
  await fillIssuer(page);
  await preview(page, Buffer.from('<html>Example</html>'), false);
  await expect(page.getByRole('alert')).toHaveText('Select a valid PDF.');
  await preview(page, Buffer.alloc(10 * 1024 * 1024 + 1), false);
  await expect(page.getByRole('alert')).toHaveText('PDFs must be no larger than 10 MiB.');
  const attach = await preview(page, Buffer.from('%PDF-1.7\nFixture text\n%%EOF\n'));
  const rejected = page.waitForResponse(response => response.url().endsWith(api) && response.request().method() === 'POST');
  await attach!.click();
  const response = await rejected;
  expect(response.status()).toBe(400);
  expect((await response.json()).code).toBe('BillingDocument.ContentInvalid');
  await expect(page.getByRole('alert')).toHaveText('Select a valid PDF.');
  await page.screenshot({ path: join(evidence, 'invalid-structure.png'), fullPage: true });
}

async function recoverLostReply(page: Page, submit: () => Promise<void>) {
  let created = 0;
  await page.route('**' + api, async route => {
    if (route.request().method() !== 'POST') return route.continue();
    const response = await route.fetch();
    created = response.status();
    expect(response.headers()['cache-control']).toBe('no-store');
    await route.abort();
  });
  await submit();
  await expect(page.getByRole('alert')).toContainText('unresolved');
  expect(created).toBe(201);
  await page.unroute('**' + api);

  const pending = page.getByRole('region', { name: 'Saved upload operation' });
  const key = await pending.locator('code').textContent();
  await page.reload();
  await expect(pending.locator('code')).toHaveText(key!);
  const recovered = page.waitForResponse(response => response.url().endsWith(api + '/operations/' + key));
  await page.getByRole('button', { name: 'Check original upload' }).click();
  const response = await recovered;
  expect(response.status()).toBe(200);
  const record = await response.json();
  expect(record).toMatchObject({ accountId: account, orderId: order, sha256, version: 1 });
  expect(record.content).toBeUndefined();
  await expect(page.getByRole('status')).toHaveText('Accounting invoice attached.');
}

async function assertMismatch(page: Page) {
  const mismatched = `/api/admin/accounts/${foreignAccount}/orders/${order}/billing-documents`;
  const unavailable = page.waitForResponse(response => response.url().endsWith(mismatched));
  await page.goto(documents.replace(account, foreignAccount));
  const response = await unavailable;
  expect(response.status()).toBe(404);
  expect((await response.json()).code).toBe('BillingDocument.OrderNotFound');
  await expect(page.getByRole('alert')).toContainText('accepted order was not found for this account');
  await expect(page.getByRole('region', { name: 'Attached accounting invoice' })).toHaveCount(0);
  writeFileSync(join(evidence, 'browser-observations.json'), JSON.stringify({
    account, order, sha256, invalidPdfStatus: 400, oversizedRefusedLocally: true, iframesEmbedsObjects: 0,
    uploadCommittedStatus: 201, recoveredStatus: 200, mismatchedAccountStatus: 404,
  }, null, 2));
}

test('Support has neither the Billing page nor API access @denied', async ({ page }) => {
  await signIn(page, 'support');
  await page.goto(documents);
  await expect(page.getByRole('alert')).toHaveText('Billing capability is required.');
  const denied = await page.request.get(origin + api);
  expect(denied.status()).toBe(403);
  expect((await denied.json()).code).toBe('ManualPayment.AccessDenied');
  expect(denied.headers()['cache-control']).toBe('no-store');
  await expect(page.getByLabel('Invoice PDF (up to 10 MiB)')).toHaveCount(0);
  await page.screenshot({ path: join(evidence, 'support-denied.png'), fullPage: true });
});
