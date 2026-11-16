import { expect, type Page } from '@playwright/test';
import { origin, signIn, type Enrolled } from './p01-12-fixture';

const merchant = process.env['P0207_MERCHANT_WEB']!;
export const name = 'Fixture Growth';

export async function enableCatalogue(page: Page, owner: Enrolled) {
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

export async function signInMerchant(page: Page) {
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

export async function openPlans(page: Page) {
  const read = page.waitForResponse(response => new URL(response.url()).pathname === '/api/billing/plans');
  await page.goto(merchant + '/billing/plans');
  const response = await read;
  expect(response.status()).toBe(200);
  expect(response.headers()['cache-control']).toContain('no-store');
  return (await response.json()).items as unknown[];
}

export async function prepare(page: Page, action: 'trial policy' | 'plan') {
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

export async function confirm(page: Page) {
  await page.getByLabel('I approve publication of these exact terms.').check();
  await page.getByRole('button', { name: 'Confirm publication' }).click();
  await expect(page.getByText('Version published', { exact: true })).toBeVisible();
}

export async function draftTrial(page: Page) {
  await page.getByLabel('Active member cap').fill('5');
  await page.getByLabel('Concurrent trial shop cap').fill('2');
  await page.getByLabel('Trial terms version').fill('trial-local-1');
  await page.getByRole('button', { name: 'Save trial policy draft' }).click();
  await expect(page.getByText('All trial facts are recorded.')).toBeVisible();
}

export async function draftPlan(page: Page) {
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

export async function history(page: Page) {
  const response = await page.request.get(origin + '/api/admin/catalogue/history');
  expect(response.status()).toBe(200);
  return (await response.json()).items as { status: string; versionId: string; snapshotHash: string; snapshot: { plan?: { versionNumber: number } }; retirement?: { reason: string; retiredAt: string } }[];
}
