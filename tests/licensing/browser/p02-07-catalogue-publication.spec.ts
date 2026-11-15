import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { createOwner, origin, signIn, type Enrolled } from './p01-12-fixture';

const merchant = process.env['P0207_MERCHANT_WEB']!;
const evidence = process.env['P0207_EVIDENCE']!;
const name = 'Fixture Growth';

async function enableCatalogue(page: Page, owner: Enrolled) {
  await page.goto(origin + '/catalogue');
  await expect(page.getByRole('alert')).toContainText('Only a Licensing Manager');
  expect((await page.request.get(origin + '/api/admin/plans')).status()).toBe(403);
  await page.goto(origin + '/');
  await page.getByRole('link', { name: 'Manage administrator access' }).click();
  const users = await (await page.request.get(origin + '/api/admin/users')).json();
  const self = users.items.find((row: { email: string }) => row.email.toLowerCase() === owner.email.toLowerCase());
  await page.getByLabel('Administrator', { exact: true }).selectOption(self.id);
  await page.getByRole('checkbox', { name: 'Licensing Manager', exact: true }).check();
  await page.getByLabel('Reason', { exact: true }).selectOption('responsibilities-changed');
  await page.getByLabel('I confirm these access changes.').check();
  await page.getByRole('button', { name: 'Save administrator access' }).click();
  await expect.poll(async () => (await page.request.get(origin + '/api/admin/auth/session')).status()).toBe(401);
  await signIn(page, owner);
  await page.getByRole('link', { name: 'Plan catalogue drafts' }).click();
  await expect(page.getByRole('heading', { name: 'Published versions' })).toBeVisible();
}

async function signInMerchant(page: Page) {
  await page.goto(merchant + '/login');
  await page.locator('#identifier').fill(process.env['P0207_MERCHANT_EMAIL']!);
  await page.locator('#password').evaluate((input: HTMLInputElement, password) => {
    input.value = password!;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }, process.env['P0207_MERCHANT_PASSWORD']);
  const signedIn = page.waitForResponse(response => new URL(response.url()).pathname === '/api/auth/login'
    && response.request().method() === 'POST');
  await page.locator('button[type=submit]').click();
  expect((await signedIn).status()).toBe(200);
  await expect(page).not.toHaveURL(/\/login/);
}

async function openPlans(page: Page) {
  const read = page.waitForResponse(response => new URL(response.url()).pathname === '/api/billing/plans');
  await page.goto(merchant + '/billing/plans');
  const response = await read;
  expect(response.status()).toBe(200);
  expect(response.headers()['cache-control']).toContain('no-store');
  return (await response.json()).items as unknown[];
}

async function prepare(page: Page, action: 'trial policy' | 'plan') {
  await page.getByRole('button', { name: action === 'plan' ? 'Review selected plan draft' : 'Review trial policy draft' }).click();
  await page.getByLabel('Publication reason').fill('Local immutable catalogue verification');
  const prepared = page.waitForResponse(response => new URL(response.url()).pathname.endsWith('/publish-preview')
    && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Prepare ' + action + ' preview' }).click();
  const response = await prepared;
  expect(response.status(), response.ok() ? 'Prepared exact preview' : (await response.json()).code).toBe(200);
  const snapshot = page.getByTestId('publication-snapshot');
  await expect(snapshot).toContainText('Development fixture');
  await expect(page.getByRole('button', { name: 'Confirm publication' })).toBeDisabled();
  return snapshot;
}

async function confirm(page: Page) {
  await page.getByLabel('I approve publication of these exact terms.').check();
  await page.getByRole('button', { name: 'Confirm publication' }).click();
  await expect(page.getByText('Version published', { exact: true })).toBeVisible();
}

async function draftTrial(page: Page) {
  await page.getByLabel('Active member cap').fill('5');
  await page.getByLabel('Concurrent trial shop cap').fill('2');
  await page.getByLabel('Trial terms version').fill('trial-local-1');
  await page.getByRole('button', { name: 'Save trial policy draft' }).click();
  await expect(page.getByText('All trial facts are recorded.')).toBeVisible();
}

async function draftPlan(page: Page) {
  await page.getByRole('button', { name: 'New plan draft' }).click();
  await page.getByLabel('Plan kind').selectOption('multi-shop');
  await page.getByLabel('Plan name').fill(name);
  await page.getByLabel('Monthly base price (₹)').fill('1499');
  await page.getByLabel('Annual base price (₹)').fill('14990');
  await page.getByLabel('Purchased shop slots').fill('3');
  await page.getByLabel('Members per shop').fill('5');
  await page.getByLabel('Extra shops').selectOption('yes');
  await page.getByLabel('Extra-shop monthly price (₹)').fill('399');
  await page.getByLabel('Extra-shop annual price (₹)').fill('3990');
  await page.getByLabel('Customer terms version').fill('customer-local-1');
  await page.getByLabel('Refund policy version').fill('refund-local-1');
  await page.getByRole('button', { name: 'Save draft', exact: true }).click();
  await expect(page.getByText('All required facts are recorded.')).toBeVisible();
}

async function history(page: Page) {
  const response = await page.request.get(origin + '/api/admin/catalogue/history');
  expect(response.status()).toBe(200);
  return (await response.json()).items as { status: string; versionId: string; snapshot: { plan?: { versionNumber: number } } }[];
}

async function dropConfirmationAfterCommit(page: Page) {
  const operation = new URL(page.url()).searchParams.get('publication');
  expect(Boolean(operation)).toBe(true);
  await page.route('**/api/admin/plans/*/versions/*/publish', async route => {
    const response = await route.fetch();
    expect(response.status()).toBe(200);
    expect(route.request().postDataJSON().operationKey === operation).toBe(true);
    await route.abort('failed');
  }, { times: 1 });
  await page.getByLabel('I approve publication of these exact terms.').check();
  await page.getByRole('button', { name: 'Confirm publication' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'publication outcome is unconfirmed' })).toBeVisible();
  await page.reload();
  await expect(page.getByText('Version published', { exact: true })).toBeVisible();
  expect(new URL(page.url()).searchParams.get('publication') === operation).toBe(true);
}

test('publishes a local plan after preview and preserves its immutable version after a successor', async ({ browser }) => {
  const adminContext = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const merchantContext = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
  try {
    const admin = await adminContext.newPage();
    const shop = await merchantContext.newPage();
    await signInMerchant(shop);
    expect(await openPlans(shop)).toEqual([]);
    await expect(shop.getByText('No plans have been published yet.')).toBeVisible();

    await enableCatalogue(admin, await signIn(admin, await createOwner(admin)));
    await draftTrial(admin);
    await expect(await prepare(admin, 'trial policy')).toContainText('14 calendar days');
    await confirm(admin);
    await admin.getByRole('button', { name: 'Close publication' }).click();
    await draftPlan(admin);
    const preview = await prepare(admin, 'plan');
    for (const term of ['₹1,499.00', '₹14,990.00', '₹399.00', '₹3,990.00', 'trial-local-1',
      'customer-local-1', 'refund-local-1', 'fixture-accounting', 'Asia/Kolkata']) {
      await expect(preview).toContainText(term);
    }
    expect(await openPlans(shop)).toEqual([]);
    await admin.getByRole('region', { name: 'Publish catalogue version' }).screenshot({ path: join(evidence, 'admin-preview.png') });
    await confirm(admin);
    const [first] = await openPlans(shop);
    expect(first).toMatchObject({ versionNumber: 1, annualPricePaise: 1499000, membersPerShop: 5,
      developmentFixture: true, paymentConfirmationIsTaxInvoice: false });
    const card = shop.getByTestId('published-plan');
    await expect(card).toHaveCount(1);
    await expect(card).toContainText('Development fixture');
    await expect(card).toContainText('₹14,990.00');
    await shop.screenshot({ path: join(evidence, 'merchant-v1-mobile.png'), fullPage: true });
    const original = (await history(admin)).find(row => row.snapshot.plan?.versionNumber === 1)!;
    await admin.getByRole('button', { name: 'Close publication' }).click();

    await admin.getByText(name + ' · version 1 · published', { exact: true }).click();
    const version = admin.locator('details').filter({ hasText: name + ' · version 1 · published' });
    await version.getByRole('button', { name: 'Create successor draft' }).click();
    await expect(admin.getByLabel('Annual base price (₹)')).toHaveValue('14990.00');
    await admin.getByLabel('Annual base price (₹)').fill('17990');
    await admin.getByLabel('Members per shop').fill('7');
    const saved = admin.waitForResponse(response => new URL(response.url()).pathname.endsWith('/draft')
      && response.request().method() === 'PUT');
    await admin.getByRole('button', { name: 'Save draft', exact: true }).click();
    expect((await saved).status()).toBe(200);
    await expect(admin.getByText('Draft saved. It is not published and no customer can see it.')).toBeVisible();
    await expect(await prepare(admin, 'plan')).toContainText('₹17,990.00');
    await dropConfirmationAfterCommit(admin);

    const offers = await openPlans(shop);
    expect(offers).toHaveLength(1);
    expect(offers[0]).toMatchObject({ versionNumber: 2, annualPricePaise: 1799000, membersPerShop: 7,
      developmentFixture: true, paymentConfirmationIsTaxInvoice: false });
    await expect(card).toContainText('₹17,990.00');
    await shop.screenshot({ path: join(evidence, 'merchant-v2-mobile.png'), fullPage: true });
    const archived = (await history(admin)).find(row => row.versionId === original.versionId)!;
    expect(archived.status).toBe('retired');
    expect(archived.snapshot).toEqual(original.snapshot);
    await expect(admin.getByText(name + ' · version 1 · retired', { exact: true })).toBeVisible();
    await admin.setViewportSize({ width: 390, height: 844 });
    await admin.getByRole('region', { name: 'Publish catalogue version' }).screenshot({ path: join(evidence, 'admin-recovered-mobile.png') });
    await shop.setViewportSize({ width: 1366, height: 900 });
    await shop.screenshot({ path: join(evidence, 'merchant-v2-desktop.png'), fullPage: true });

    writeFileSync(join(evidence, 'observations.json'), JSON.stringify({
      environment: 'Development fixture on loopback, shipping admin and merchant web applications',
      deniedPlatformOwner: 403,
      offersBeforePlanConfirmation: 0,
      preview: 'INR 1499.00 monthly, 14990.00 annual; three shop slots and five members; pinned trial and policy references',
      version1: { annualPricePaise: 1499000, membersPerShop: 5, fixture: true },
      version2: { annualPricePaise: 1799000, membersPerShop: 7, fixture: true },
      recoveredLostConfirmation: true,
      archivedSnapshotUnchanged: true,
      paymentConfirmationIsTaxInvoice: false,
    }, null, 2));
  } finally {
    await Promise.all([adminContext.close(), merchantContext.close()]);
  }
});
