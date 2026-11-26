import { createHash } from 'node:crypto';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page } from '@playwright/test';
import { fillSecret, otp } from './p01-12-fixture';

function required(name: string) {
  const value = process.env['P0613_' + name];
  if (!value) throw new Error('Missing P0613 variable ' + name);
  return value;
}

const adminWeb = required('ADMIN_WEB');
const merchantWeb = required('MERCHANT_WEB');
const scratch = required('TEMPORARY');
const evidence = required('EVIDENCE_DIR');
const cfg = JSON.parse(readFileSync(join(scratch, 'fixture.json'), 'utf8'));
const v2Pdf = readFileSync(join(scratch, 'fixture-invoice-v2.pdf'));
const adminApi = `/api/admin/accounts/${cfg.sourceAccount}/orders/${cfg.order}/billing-documents`;
const adminDocsUrl = `${adminWeb}/payments/documents?account=${cfg.sourceAccount}&order=${cfg.order}`;
const sourceCheckout = `${merchantWeb}/billing/accounts/${cfg.sourceAccount}/checkout?order=${cfg.order}`;
const reason = 'Correct GSTIN and legal name on customer accounting invoice.';

function sha256(bytes: Buffer): string {
  return createHash('sha256').update(bytes).digest('hex');
}

async function signInAdmin(page: Page) {
  await page.goto(`${adminWeb}/auth/sign-in`);
  await page.getByLabel('Email', { exact: true }).fill(cfg.financeEmail);
  await fillSecret(page, 'Password', cfg.financePassword);
  await fillSecret(page, 'Authenticator code', otp(cfg.financeSeed, Math.floor(cfg.financeTime / 30)));
  const reply = page.waitForResponse(r => r.url().endsWith('/api/admin/auth/sign-in') && r.request().method() === 'POST');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  expect((await reply).status()).toBe(200);
  await expect(page).toHaveURL(`${adminWeb}/`);
}

async function signInMerchant(browser: Browser, email: string) {
  const context = await browser.newContext();
  const page = await context.newPage();
  await page.goto(`${merchantWeb}/login`);
  await page.locator('#identifier').fill(email);
  await page.locator('#password').evaluate((input: HTMLInputElement, value: string) => {
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }, cfg.merchantPassword);
  await page.locator('button[type=submit]').click();
  await expect(page).not.toHaveURL(/\/login/);
  const state = await context.storageState();
  await context.close();
  return state;
}

async function previewFinanceCorrection(page: Page) {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await signInAdmin(page);
  await page.goto(adminDocsUrl);
  await expect(page.getByRole('region', { name: 'Accepted order' })).toContainText(cfg.order);
  await expect(page.locator('[data-version="1"]')).toContainText('Current');
  await expect(page.locator('[data-version="1"]')).toContainText(cfg.v1Sha256);

  await page.getByRole('button', { name: 'Supersede current invoice' }).click();
  await page.getByLabel('Corrective PDF (up to 10 MiB)').setInputFiles({
    name: 'fixture-invoice-v2.pdf', mimeType: 'application/pdf', buffer: v2Pdf,
  });
  await page.getByLabel('Issuer name').fill('Fixture Accounting LLP');
  await page.getByLabel('Issuing reference').fill('FIXTURE-INV-114-CORR-1');
  await page.getByLabel('Issue date').fill('2027-03-10');
  await page.getByLabel('Correction reason (finance only)').fill(reason);
  await page.getByRole('button', { name: 'Preview correction' }).click();

  const preview = page.getByRole('region', { name: 'Correction preview' });
  await expect(preview).toContainText('FIXTURE-INV-114');
  await expect(preview).toContainText('FIXTURE-INV-114-CORR-1');
  await expect(preview).toContainText(cfg.v2Sha256);
  await expect(preview).toContainText(reason);
  await expect(page.locator('iframe, embed, object')).toHaveCount(0);
  await page.screenshot({ path: join(evidence, 'desktop-correction-preview.png'), fullPage: true });
}

async function submitAndRecoverCorrection(page: Page) {
  const submit = page.getByRole('button', { name: 'Attach corrective invoice' });
  await expect(submit).toBeDisabled();
  await page.getByRole('checkbox').check();
  await expect(submit).toBeEnabled();

  const supersedePath = `${adminApi}/${cfg.v1VersionId}/supersede`;
  let committedStatus = 0;
  await page.route('**' + supersedePath, async route => {
    if (route.request().method() !== 'POST') return route.continue();
    const response = await route.fetch();
    committedStatus = response.status();
    expect(response.headers()['cache-control']).toContain('no-store');
    await route.abort();
  });
  await submit.click();
  await expect(page.getByRole('alert')).toContainText('unresolved');
  expect(committedStatus).toBe(201);
  await page.unroute('**' + supersedePath);

  const pending = page.getByRole('region', { name: 'Saved correction operation' });
  const key = (await pending.locator('code').textContent())!.trim();
  await page.reload();
  await expect(pending.locator('code')).toHaveText(key);
  const recovery = page.waitForResponse(r => r.url().endsWith(`${adminApi}/operations/${key}`));
  await page.getByRole('button', { name: 'Check original correction' }).click();
  const recoveryResponse = await recovery;
  expect(recoveryResponse.status()).toBe(200);
  const record = await recoveryResponse.json();
  expect(record).toMatchObject({
    accountId: cfg.sourceAccount, orderId: cfg.order, version: 2,
    supersedesVersionId: cfg.v1VersionId, sha256: cfg.v2Sha256, supersessionReason: reason,
  });
  return { v2VersionId: record.versionId as string, committedStatus, recoveredStatus: recoveryResponse.status() };
}

async function runFinanceCorrection(page: Page) {
  await previewFinanceCorrection(page);
  const result = await submitAndRecoverCorrection(page);

  await expect(page.getByRole('status')).toHaveText('Corrective invoice version attached.');
  await expect(page.locator('[data-version="2"]')).toContainText('Current');
  await expect(page.locator('[data-version="2"]')).toContainText(cfg.v2Sha256);
  await expect(page.locator('[data-version="1"]')).toContainText('Historical');
  await expect(page.locator('[data-version="1"]')).toContainText(cfg.v1Sha256);
  await expect(page.locator('iframe, embed, object')).toHaveCount(0);

  await page.setViewportSize({ width: 390, height: 844 });
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: join(evidence, 'admin-history-mobile.png'), fullPage: true, animations: 'disabled' });
  return result;
}

async function downloadVersion(page: Page, version: number, versionId: string, expectedHash: string, file: string) {
  const item = page.locator(`[data-testid="billing-document-item"][data-version="${version}"]`);
  const responsePromise = page.waitForResponse(
    r => r.url().includes(`/billing-documents/${versionId}/content`) && r.request().method() === 'GET',
  );
  const downloadPromise = page.waitForEvent('download');
  await item.getByTestId('billing-document-download').click();
  const attachment = await downloadPromise;
  expect(attachment.suggestedFilename()).toBe(`accounting-invoice-${versionId}.pdf`);
  await attachment.saveAs(join(evidence, file));
  const response = await responsePromise;
  expect(response.status()).toBe(200);
  expect(response.headers()['content-type']).toContain('application/pdf');
  expect(response.headers()['cache-control']).toContain('no-store');
  expect(response.headers()['content-disposition']).toContain('attachment');
  expect(response.headers()['x-content-type-options']).toContain('nosniff');
  expect(response.headers()['content-security-policy']).toContain("default-src 'none'; sandbox");
  const bytes = readFileSync(join(evidence, file));
  expect(sha256(bytes)).toBe(expectedHash);
  return { version, versionId, file, status: response.status(), sha256: sha256(bytes), byteLength: bytes.length };
}

async function verifySourceMerchantDownloads(browser: Browser, v2VersionId: string) {
  const state = await signInMerchant(browser, cfg.sourcePayerEmail);
  const ctx = await browser.newContext({ storageState: state });
  const page = await ctx.newPage();
  await page.setViewportSize({ width: 1440, height: 1000 });
  let popups = 0;
  page.on('popup', () => { popups++; });

  await page.goto(sourceCheckout);
  await expect(page.getByTestId('payment-confirmed')).toBeVisible();
  await page.getByTestId('confirmation-list').click();
  const confDownload = page.waitForEvent('download');
  await page.getByTestId('confirmation-download').click();
  const confFile = await confDownload;
  await confFile.saveAs(join(evidence, 'merchant-confirmation.txt'));
  const confText = readFileSync(join(evidence, 'merchant-confirmation.txt'), 'utf8');
  expect(confText).toContain('PAYMENT CONFIRMATION\n');
  expect(confText).toContain('not a tax invoice');

  await page.getByTestId('billing-documents-list').click();
  const v2Item = page.locator('[data-testid="billing-document-item"][data-version="2"]');
  const v1Item = page.locator('[data-testid="billing-document-item"][data-version="1"]');
  await expect(v2Item.getByTestId('billing-document-status')).toHaveText('Current');
  await expect(v2Item.getByTestId('billing-document-reference')).toHaveText('FIXTURE-INV-114-CORR-1');
  await expect(v2Item.getByTestId('billing-document-hash')).toHaveText(cfg.v2Sha256);
  await expect(v1Item.getByTestId('billing-document-status')).toHaveText('Historical');
  await expect(v1Item.getByTestId('billing-document-reference')).toHaveText('FIXTURE-INV-114');
  await expect(v1Item.getByTestId('billing-document-hash')).toHaveText(cfg.v1Sha256);
  expect(await page.textContent('body')).not.toContain(reason);

  const dlV2 = await downloadVersion(page, 2, v2VersionId, cfg.v2Sha256, 'merchant-invoice-v2.pdf');
  const dlV1 = await downloadVersion(page, 1, cfg.v1VersionId, cfg.v1Sha256, 'merchant-invoice-v1.pdf');
  expect(popups).toBe(0);
  await expect(page.locator('iframe, embed, object')).toHaveCount(0);
  await page.screenshot({ path: join(evidence, 'merchant-history-desktop.png'), fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: join(evidence, 'merchant-history-mobile.png'), fullPage: true });
  await ctx.close();
  return { dlV1, dlV2, confirmationSaved: true, popups };
}

async function verifyDestinationPayerDenied(browser: Browser, v2VersionId: string) {
  const state = await signInMerchant(browser, cfg.destPayerEmail);
  const ctx = await browser.newContext({ storageState: state });
  const page = await ctx.newPage();
  let authHeader = '';
  page.on('request', req => {
    const auth = req.headers()['authorization'];
    if (req.url().startsWith(merchantWeb + '/api/') && auth?.startsWith('Bearer ')) authHeader = auth;
  });

  const checkoutResp = page.waitForResponse(
    r => r.url().includes(`/api/billing/accounts/${cfg.destAccount}/orders/${cfg.order}`) && r.request().method() === 'GET',
  );
  await page.goto(`${merchantWeb}/billing/accounts/${cfg.destAccount}/checkout?order=${cfg.order}`);
  const checkoutStatus = (await checkoutResp).status();
  expect(checkoutStatus).toBe(404);
  await expect(page.getByTestId('billing-document-download')).toHaveCount(0);
  expect(authHeader).toMatch(/^Bearer /);

  const check = async (path: string, expected: number) => {
    const resp = await page.request.get(`${merchantWeb}${path}`, { headers: { Authorization: authHeader } });
    expect(resp.status()).toBe(expected);
    expect(resp.headers()['cache-control']).toContain('no-store');
    expect(await resp.text()).not.toContain('%PDF');
    return resp.status();
  };
  const sourceList = `/api/billing/accounts/${cfg.sourceAccount}/orders/${cfg.order}/billing-documents`;
  const destList = `/api/billing/accounts/${cfg.destAccount}/orders/${cfg.order}/billing-documents`;
  const results = {
    checkoutStatus,
    sourceListStatus: await check(sourceList, 403),
    sourceV2ContentStatus: await check(`${sourceList}/${v2VersionId}/content`, 403),
    destListStatus: await check(destList, 404),
    destV1ContentStatus: await check(`${destList}/${cfg.v1VersionId}/content`, 404),
    destV2ContentStatus: await check(`${destList}/${v2VersionId}/content`, 404),
  };
  await ctx.close();
  return results;
}

test('finance supersedes invoice with recovery and source payer downloads both versions after transfer', async ({ browser }) => {
  const adminCtx = await browser.newContext();
  const adminPage = await adminCtx.newPage();
  const finance = await runFinanceCorrection(adminPage);
  await adminCtx.close();

  writeFileSync(join(scratch, 'v2-committed'), finance.v2VersionId);
  await expect.poll(() => existsSync(join(scratch, 'transfer-applied')), { timeout: 120_000 }).toBe(true);

  const merchant = await verifySourceMerchantDownloads(browser, finance.v2VersionId);
  const isolation = await verifyDestinationPayerDenied(browser, finance.v2VersionId);

  writeFileSync(
    join(evidence, 'browser-observations.json'),
    JSON.stringify(
      {
        localFixtureOnly: true,
        boundary: 'Shipping admin and merchant web builds, real merchant and licensing APIs, real PostgreSQL, development simulator payment and local PDF fixtures only. No live provider verification.',
        sourceAccount: cfg.sourceAccount,
        destinationAccount: cfg.destAccount,
        orderId: cfg.order,
        v1VersionId: cfg.v1VersionId,
        v2VersionId: finance.v2VersionId,
        committedStatus: finance.committedStatus,
        recoveredStatus: finance.recoveredStatus,
        merchant,
        isolation,
      },
      null,
      2,
    ),
  );
});
