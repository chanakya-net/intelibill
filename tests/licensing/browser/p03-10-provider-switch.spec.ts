import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import { fillSecret, otp } from './p01-12-fixture';

function required(name: string) {
  const value = process.env['P0310_' + name];
  if (!value) throw new Error('Missing provider switch variable ' + name);
  return value;
}
const admin = required('ADMIN_WEB');
const merchant = required('MERCHANT_WEB');
const scratch = required('TEMPORARY');
const evidence = required('EVIDENCE_DIR');
const account = required('ACCOUNT');
const order = required('ORDER');
const other = required('OTHER_ACCOUNT');
const checkout = merchant + '/billing/accounts/' + account + '/checkout?order=' + order;
const details = admin + '/payments?account=' + account + '&order=' + order;
const secrets = JSON.parse(readFileSync(join(scratch, 'sessions.json'), 'utf8'));

async function mark(name: string) { writeFileSync(join(scratch, name), ''); }
async function wait(name: string) { await expect.poll(() => existsSync(join(scratch, name)), { timeout: 60_000 }).toBe(true); }
async function capture(page: Page, name: string) {
  for (const [label, width, height] of [['desktop', 1440, 1000], ['mobile', 390, 844]] as const) {
    await page.setViewportSize({ width, height });
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
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
  const response = page.waitForResponse(r => new URL(r.url()).pathname === '/api/auth/login' && r.request().method() === 'POST');
  await page.locator('button[type=submit]').click();
  expect((await response).status()).toBe(200);
  await expect(page).not.toHaveURL(/\/login/);
}
async function signInAdmin(page: Page) {
  await page.goto(admin + '/auth/sign-in');
  await page.getByLabel('Email', { exact: true }).fill(secrets.admin);
  await fillSecret(page, 'Password', secrets.adminPassword);
  const epoch = Number(readFileSync(join(scratch, 'switched'), 'utf8'));
  await fillSecret(page, 'Authenticator code', otp(secrets.seed, Math.floor(epoch / 30)));
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Manual payments', exact: true })).toBeVisible();
}
async function selectB(page: Page) {
  await page.goto(admin + '/auth/sign-in');
  await page.getByLabel('Email', { exact: true }).fill(secrets.ownerEmail);
  await fillSecret(page, 'Password', secrets.ownerPassword);
  await fillSecret(page, 'Authenticator code', otp(secrets.ownerSeed, Math.floor(secrets.adminTime / 30)));
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).not.toHaveURL(/auth\/sign-in/);
  const csrf = await page.request.get(admin + '/api/admin/auth/sign-in/context');
  const selected = await page.request.post(admin + '/api/admin/payment-providers/selection', {
    headers: { 'X-Admin-CSRF': (await csrf.json()).requestToken, Origin: admin },
    data: { profileId: 'b', operationKey: crypto.randomUUID(), expectedRevision: 1, reason: 'Authorized local browser switch' },
  });
  expect(selected.status()).toBe(200);
}

async function draft(page: Page) {
  return page.evaluate(({ order }) => {
    const key = Object.keys(localStorage).find(k => k.startsWith('intelibill.billing.checkout.v1:payment:') && k.includes(order));
    if (!key) return null;
    const value = JSON.parse(localStorage.getItem(key)!);
    return { operationKey: value.command?.operationKey, attempt: value.attempt };
  }, { order });
}
async function noReplacement(page: Page, unresolved = true) {
  await expect(page.getByTestId('payment-start')).toHaveCount(0);
  await expect(page.getByTestId('payment-simulator-capture')).toHaveCount(0);
  if (unresolved) await expect(page.getByRole('button', { name: 'Review a new quote' })).toHaveCount(0);
}

async function confirmation(page: Page, adminView: boolean, name: string) {
  const prefix = adminView ? 'admin-' : '';
  await page.getByTestId(prefix + 'confirmation-list').click();
  const response = page.waitForResponse(r => r.url().includes('/document') && r.request().method() === 'GET');
  const downloaded = page.waitForEvent('download');
  await page.getByTestId(prefix + 'confirmation-download').click();
  const attachment = await downloaded;
  const received = await response;
  expect(received.status()).toBe(200);
  expect(received.headers()['cache-control']).toContain('no-store');
  expect(received.headers()['content-disposition']).toContain('attachment');
  expect(received.headers()['x-content-type-options'].split(',').map(v => v.trim()).every(v => v === 'nosniff')).toBe(true);
  expect(attachment.suggestedFilename()).toMatch(/^payment-confirmation-[\da-f-]+\.txt$/);
  await attachment.saveAs(join(evidence, name + '.txt'));
  const bytes = readFileSync(join(evidence, name + '.txt'));
  const text = bytes.toString('utf8');
  expect(text).toContain('Provider: development-simulator / simulator-a / Development');
  expect(text).toContain('Provider configuration: a/1');
  expect(text).toContain('not a tax invoice');
  expect(text).toContain('does not prove applied access');
  return bytes;
}

async function applyingConfirmations(page: Page, payer: Page) {
  await expect(payer.getByTestId('payment-confirmed')).toBeVisible();
  const original = await confirmation(payer, false, 'merchant-applying');
  await capture(payer, 'merchant-applying');
  await page.getByRole('button', { name: 'Refresh order', exact: true }).click();
  expect(await confirmation(page, true, 'admin-applying')).toEqual(original);
  await capture(page, 'admin-applying');
  return original;
}

async function inspectRecovery(page: Page, payer: Page, context: import('@playwright/test').BrowserContext, originalKey: string) {
  await page.goto(details);
  const routing = page.getByTestId('payment-routing');
  await expect(routing).toContainText('simulator-a');
  await expect(routing).toContainText('simulator-b');
  await expect(routing).toContainText('a/1');
  await expect(page.getByTestId('routing-available')).toBeVisible();
  await capture(page, 'admin-original');
  await mark('inspected');
  await wait('missing');
  await page.getByRole('button', { name: 'Refresh order', exact: true }).click();
  await expect(page.getByTestId('routing-blocker')).toContainText('MissingProfile');
  await capture(page, 'admin-missing');
  await payer.reload();
  await expect(payer.getByTestId('payment-routing-review')).toContainText('not yet verified');
  await noReplacement(payer);
  await expect(payer.getByTestId('payment-confirmed')).toHaveCount(0);
  const second = await context.newPage();
  await second.goto(checkout);
  await expect(second.getByTestId('payment-routing-review')).toBeVisible();
  await noReplacement(second);
  const frozen = await draft(payer);
  expect(frozen?.operationKey).toBe(originalKey);
  await second.goto(merchant + '/billing/accounts/' + other + '/checkout');
  await expect(second.getByTestId('payment-routing-review')).toHaveCount(0);
  await second.goto(checkout);
  await expect(second.getByTestId('payment-routing-review')).toBeVisible();
  expect((await draft(second))?.operationKey).toBe(originalKey);
  await payer.getByTestId('payment-check').click();
  await expect(payer.getByTestId('payment-check')).toBeEnabled();
  await expect(payer.getByTestId('payment-routing-review')).toBeVisible();
  await noReplacement(payer);
  await capture(payer, 'merchant-unverified-review');
}

async function confirmBeforeAndAfterDelivery(page: Page, payer: Page) {
  await mark('reviewed');
  await wait('captured');
  await payer.reload();
  await expect(payer.getByTestId('payment-applying')).toBeVisible();
  await noReplacement(payer, false);
  const applyingState = await payer.getByTestId('payment-applying').textContent();
  const original = await applyingConfirmations(page, payer);
  await mark('applying');
  await wait('delivered');
  await payer.reload();
  await expect(payer.getByTestId('payment-applied')).toBeVisible();
  await noReplacement(payer, false);
  expect(await confirmation(payer, false, 'merchant-applied')).toEqual(original);
  const appliedState = await payer.getByTestId('payment-applied').textContent();
  await capture(payer, 'merchant-applied');
  await page.getByRole('button', { name: 'Refresh order', exact: true }).click();
  await expect(page.getByTestId('payment-routing')).toContainText('simulator-a');
  await expect(page.getByTestId('payment-routing')).toContainText('simulator-b');
  await expect(page.getByTestId('routing-available')).toBeVisible();
  expect(await confirmation(page, true, 'admin-applied')).toEqual(original);
  await capture(page, 'admin-restored');
  return { applyingState, appliedState, documentSha256: createHash('sha256').update(original).digest('hex') };
}

test('Switch preserves original identity, unknown intent and one delayed capture', async ({ browser, page }) => {
  const merchantContext = await browser.newContext({ locale: 'en-IN', timezoneId: 'Asia/Kolkata' });
  const payer = await merchantContext.newPage();
  const calls: Record<string, number> = {};
  merchantContext.on('request', request => {
    const url = new URL(request.url());
    if (url.pathname.includes('/payment')) {
      const key = request.method() + ' ' + url.pathname;
      calls[key] = (calls[key] ?? 0) + 1;
    }
  });
  await signInMerchant(payer);
  await payer.goto(checkout);
  let creates = 0;
  let originalKey: string | undefined;
  let createCommitted = false;
  await payer.route('**/payment/attempts', async route => {
    creates++;
    originalKey = route.request().postDataJSON().operationKey;
    expect((await route.fetch()).status()).toBe(201);
    createCommitted = true;
    await route.abort();
  });
  await payer.getByTestId('payment-start').click();
  await expect(payer.getByTestId('payment-pending')).toBeVisible();
  await noReplacement(payer);
  await expect.poll(() => createCommitted).toBe(true);
  await mark('started');
  const ownerContext = await browser.newContext();
  await selectB(await ownerContext.newPage());
  await ownerContext.close();
  await mark('selected-b');
  await wait('switched');
  await signInAdmin(page);
  const csrf = await page.request.get(admin + '/api/admin/auth/sign-in/context');
  const denied = await page.request.post(admin + '/api/admin/payment-providers/selection', {
    headers: { 'X-Admin-CSRF': (await csrf.json()).requestToken, Origin: admin },
    data: { profileId: 'a', operationKey: crypto.randomUUID(), expectedRevision: 2, reason: 'Billing cannot switch providers' },
  });
  expect(denied.status()).toBe(403);
  await inspectRecovery(page, payer, merchantContext, originalKey!);
  expect(creates).toBe(1);
  const observations = await confirmBeforeAndAfterDelivery(page, payer);
  writeFileSync(join(evidence, 'safe-network-counts.json'), JSON.stringify({ calls, creates, originalKey, draftRetained: true }));
  writeFileSync(join(evidence, 'confirmation-observations.json'), JSON.stringify({
    provenance: 'development simulator, real services and PostgreSQL', ...observations, identicalDownloads: 4,
  }, null, 2));
  await merchantContext.close();
});
