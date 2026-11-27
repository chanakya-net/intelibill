import { createHash } from 'node:crypto';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page } from '@playwright/test';

function required(name: string) {
  const value = process.env['P0612_' + name];
  if (!value) throw new Error('Missing P0612 variable ' + name);
  return value;
}

const merchant = required('MERCHANT_WEB');
const scratch = required('TEMPORARY');
const evidence = required('EVIDENCE_DIR');
const account = required('ACCOUNT');
const order = required('ORDER');
const other = required('OTHER_ACCOUNT');
const versionId = required('VERSION_ID');
const expectedSha256 = required('EXPECTED_SHA256');
const checkout = `${merchant}/billing/accounts/${account}/checkout?order=${order}`;
const secrets = JSON.parse(readFileSync(join(scratch, 'sessions.json'), 'utf8'));
const observations: object[] = [];
let merchantAuthorization = '';

function sha256(bytes: Buffer): string {
  return createHash('sha256').update(bytes).digest('hex');
}

async function screenshots(page: Page, name: string) {
  for (const [label, width, height] of [['desktop', 1440, 1000], ['mobile', 390, 844]] as const) {
    await page.setViewportSize({ width, height });
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: join(evidence, `${name}-${label}.png`), fullPage: true });
  }
}

async function signInMerchant(browser: Browser) {
  const login = await browser.newContext();
  const page = await login.newPage();
  await page.goto(`${merchant}/login`);
  await page.locator('#identifier').fill(secrets.merchant);
  await page.locator('#password').evaluate((input: HTMLInputElement, value: string) => {
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }, secrets.merchantPassword);
  await page.locator('button[type=submit]').click();
  await expect(page).not.toHaveURL(/\/login/);
  const state = await login.storageState();
  await login.close();
  return state;
}

async function downloadInvoice(page: Page, file: string): Promise<Buffer> {
  const responsePromise = page.waitForResponse(
    (r) => r.url().includes('/billing-documents/') && r.url().endsWith('/content') && r.request().method() === 'GET',
  );
  const downloadPromise = page.waitForEvent('download');
  await page.getByTestId('billing-document-download').click();
  const attachment = await downloadPromise;
  expect(attachment.suggestedFilename()).toBe(`accounting-invoice-${versionId}.pdf`);
  await attachment.saveAs(join(evidence, file));
  const response = await responsePromise;
  expect(response.status()).toBe(200);
  if (response.url().startsWith(merchant)) {
    merchantAuthorization = response.request().headers()['authorization'];
  }
  expect(response.headers()['content-type']).toContain('application/pdf');
  expect(response.headers()['cache-control']).toContain('no-store');
  expect(response.headers()['content-disposition']).toContain('attachment');
  expect(
    response.headers()['x-content-type-options']
      .split(',')
      .map((value) => value.trim())
      .every((value) => value === 'nosniff'),
  ).toBe(true);
  expect(response.headers()['content-security-policy']).toContain("default-src 'none'; sandbox");
  const bytes = readFileSync(join(evidence, file));
  expect(sha256(bytes)).toBe(expectedSha256);
  observations.push({ file, status: 200, noStore: true, attachment: attachment.suggestedFilename(), sha256: sha256(bytes) });
  return bytes;
}

async function verifyMetadataAndConfirmation(page: Page) {
  await expect(page.getByTestId('payment-confirmed')).toBeVisible();
  await expect(page.getByTestId('confirmation-list')).toBeVisible();
  await expect(page.getByTestId('billing-documents-heading')).toHaveText('Accounting invoice');
  await expect(page.getByTestId('billing-documents-purpose')).toContainText('separate from your automatic payment confirmation');
  await page.getByTestId('billing-documents-list').click();
  await expect(page.getByTestId('billing-document-download')).toBeVisible();
  await expect(page.getByTestId('billing-document-issuer')).toHaveText('Fixture Accounting');
  await expect(page.getByTestId('billing-document-reference')).toHaveText('FIXTURE-INV-114');
  await expect(page.getByTestId('billing-document-issued-on')).toHaveText('2027-03-10');
  await expect(page.getByTestId('billing-document-version')).toHaveText('1');
  await expect(page.getByTestId('billing-document-hash')).toHaveText(expectedSha256);
}

async function verifyConfirmationDownload(page: Page) {
  await page.getByTestId('confirmation-list').click();
  await expect(page.getByTestId('confirmation-download')).toBeVisible();
  const pending = page.waitForEvent('download');
  await page.getByTestId('confirmation-download').click();
  const attachment = await pending;
  expect(attachment.suggestedFilename()).toMatch(/^payment-confirmation-[\da-f-]+\.txt$/);
  await attachment.saveAs(join(evidence, 'merchant-confirmation.txt'));
  const text = readFileSync(join(evidence, 'merchant-confirmation.txt'), 'utf8');
  expect(text).toContain('PAYMENT CONFIRMATION\n');
  expect(text).toContain('not a tax invoice');
}

async function verifyCrossAccountRefusals(page: Page, popups: number) {
  expect(merchantAuthorization).toMatch(/^Bearer /);
  const foreignResp = await page.request.get(
    `${merchant}/api/billing/accounts/${other}/orders/${order}/billing-documents/${versionId}/content`,
    { headers: { Authorization: merchantAuthorization } },
  );
  expect(foreignResp.status()).toBe(404);
  expect(foreignResp.headers()['cache-control']).toContain('no-store');
  expect(await foreignResp.text()).not.toContain('%PDF');

  const guessedResp = await page.request.get(
    `${merchant}/api/billing/accounts/${account}/orders/${order}/billing-documents/00000000-0000-4000-8000-000000000099/content`,
    { headers: { Authorization: merchantAuthorization } },
  );
  expect(guessedResp.status()).toBe(404);
  expect(guessedResp.headers()['cache-control']).toContain('no-store');
  expect(await guessedResp.text()).not.toContain('%PDF');

  const otherOrderResponse = page.waitForResponse(
    (r) => r.url().includes(`/api/billing/accounts/${other}/orders/${order}`) && r.request().method() === 'GET',
  );
  await page.goto(`${merchant}/billing/accounts/${other}/checkout?order=${order}`);
  expect((await otherOrderResponse).status()).toBe(404);
  await expect(page.getByTestId('billing-document-download')).toHaveCount(0);
  await expect(page.getByTestId('billing-documents-list')).toHaveCount(0);
  observations.push({ foreignAccountStatus: foreignResp.status(), guessedVersionStatus: guessedResp.status(), popups, inlinePdfElements: 0 });
}

test('authorized member downloads private accounting invoice without inline PDF rendering', async ({ browser }) => {
  const state = await signInMerchant(browser);
  const customer = await browser.newContext({ storageState: state, recordVideo: { dir: join(evidence, 'demo-merchant') } });
  const payer = await customer.newPage();
  let popups = 0;
  payer.on('popup', () => { popups++; });

  await payer.goto(checkout);
  await verifyMetadataAndConfirmation(payer);
  const urlBefore = payer.url();
  const original = await downloadInvoice(payer, 'merchant-invoice-original.pdf');
  expect(popups).toBe(0);
  expect(payer.url()).toBe(urlBefore);
  await expect(payer.locator('iframe, embed, object')).toHaveCount(0);
  await verifyConfirmationDownload(payer);
  await screenshots(payer, 'merchant-before');

  writeFileSync(join(scratch, 'downloaded'), '');
  await expect.poll(() => existsSync(join(scratch, 'mutated')), { timeout: 90_000 }).toBe(true);

  await payer.reload();
  await expect(payer.getByTestId('payment-confirmed')).toBeVisible();
  await payer.getByTestId('billing-documents-list').click();
  await expect(payer.getByTestId('billing-document-download')).toBeVisible();
  expect(await downloadInvoice(payer, 'merchant-invoice-after-edits.pdf')).toEqual(original);
  await expect(payer.locator('iframe, embed, object')).toHaveCount(0);
  await screenshots(payer, 'merchant-after');
  await verifyCrossAccountRefusals(payer, popups);
  await customer.close();

  writeFileSync(
    join(evidence, 'observable-demo.json'),
    JSON.stringify(
      {
        localFixtureOnly: true,
        boundary:
          'Current merchant web build, real merchant and licensing APIs, real PostgreSQL, development simulator payment and local PDF fixture only. No live provider verification.',
        observations,
      },
      null,
      2,
    ),
  );
});
