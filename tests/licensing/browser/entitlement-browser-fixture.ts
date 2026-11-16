import { randomUUID } from 'node:crypto';
import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, type Browser, type Page } from '@playwright/test';
import { adminPost, createOwner, origin as admin, signIn } from './p01-12-fixture';

export const merchant = process.env['P0213_MERCHANT_WEB']!;
export const evidence = process.env['P0213_EVIDENCE']!;
export const account = process.env['P0213_ACCOUNT']!;

export async function merchantSession(page: Page, role: 'owner' | 'staff'): Promise<string> {
  await page.goto(merchant + '/login');
  await page.locator('#identifier').fill(process.env[`P0213_${role.toUpperCase()}_EMAIL`]!);
  await page.locator('#password').evaluate((input: HTMLInputElement, secret) => {
    input.value = secret;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }, process.env['P0213_MERCHANT_PASSWORD']!);
  const signedIn = page.waitForResponse(response => new URL(response.url()).pathname === '/api/auth/login'
    && response.request().method() === 'POST');
  await page.locator('button[type=submit]').click();
  const response = await signedIn;
  expect(response.status()).toBe(200);
  await expect(page).not.toHaveURL(/\/login/);
  return (await response.json()).accessToken;
}

export async function commerce(browser: Browser, staff: Page, staffToken: string, phase: string, ownerState?: string) {
  const owner = await browser.newPage(ownerState ? { storageState: ownerState } : {});
  try {
    if (ownerState) await owner.goto(merchant + '/billing');
    const ownerToken = ownerState ? await storedMerchantToken(owner) : await merchantSession(owner, 'owner');
    const posted = await owner.request.post(merchant + '/api/inventory/inbound', {
      headers: { Authorization: `Bearer ${ownerToken}` },
      data: {
        itemName: `Entitlement fixture ${phase}`, barcode: randomUUID().replaceAll('-', ''), uom: 'pcs',
        batchNumber: 'p0213', quantity: 5, totalPurchaseCost: 50, mrp: 12, salesPrice: 12,
        taxRatePercent: 0, taxIncluded: false, purchaseTaxIncluded: false,
      },
    });
    expect(posted.ok()).toBe(true);
    const denied = await staff.request.put(merchant + '/api/expiry-policies', {
      headers: { Authorization: `Bearer ${staffToken}` },
      data: { expectedCatalogVersion: 1, reason: 'Staff fixture', policy: { mode: 'Block', expiryRequired: true,
        sellThroughExpiryDate: true, warningDays: 7, overrideAllowed: false } },
    });
    expect(denied.status()).toBe(403);
    return { stock: posted.status(), staffRestrictedWrite: denied.status() };
  } finally {
    await owner.close();
  }
}

export async function storedMerchantToken(page: Page) {
  const token = await page.evaluate(() => JSON.parse(localStorage.getItem('inventory.auth.session.local') ?? '{}').accessToken);
  expect(typeof token).toBe('string');
  return token as string;
}

export async function prepareProvider(page: Page) {
  const owner = await signIn(page, await createOwner(page));
  await page.getByRole('link', { name: 'Manage administrator access' }).click();
  const users = await (await page.request.get(admin + '/api/admin/users')).json();
  const user = users.items.find((row: { email: string }) => row.email.toLowerCase() === owner.email.toLowerCase());
  await page.getByLabel('Administrator', { exact: true }).selectOption(user.id);
  await page.getByRole('checkbox', { name: 'Licensing Manager', exact: true }).check();
  await page.getByLabel('Reason', { exact: true }).selectOption('responsibilities-changed');
  await page.getByLabel('I confirm these access changes.').check();
  await page.getByRole('button', { name: 'Save administrator access' }).click();
  await expect.poll(async () => (await page.request.get(admin + '/api/admin/auth/session')).status()).toBe(401);
  await signIn(page, owner);
  await publishTrialPolicy(page);
  await page.context().storageState({ path: process.env['P0213_PROVIDER_STATE']! });
  writeFileSync(join(evidence, 'observations-prepare.json'), JSON.stringify({ signedInWithMfa: true, publishedLocalTrialPolicy: true }));
}


async function publishTrialPolicy(page: Page) {
  const context = await page.request.get(admin + '/api/admin/auth/sign-in/context');
  expect(context.status()).toBe(200);
  const { requestToken } = await context.json();
  const created = await page.request.put(admin + '/api/admin/trial-policy/draft', {
    headers: { Origin: admin, 'X-Admin-CSRF': requestToken },
    data: { expectedVersion: 0, activeMemberCap: 5, maxConcurrentTrialShops: 1, trialTermsVersion: 'trial-local-1' },
  });
  expect(created.status()).toBe(201);
  const { draft } = await created.json();
  const path = `/api/admin/trial-policy/versions/${draft.versionId}`;
  const prepared = await adminPost(page, path + '/publish-preview', {
    operationKey: randomUUID(), expectedVersion: 1, reason: 'Local entitlement transport fixture',
  });
  expect(prepared.status()).toBe(200);
  const { operationKey, expectedVersion, snapshotHash } = await prepared.json();
  const published = await adminPost(page, path + '/publish', { operationKey, expectedVersion, snapshotHash, confirmed: true });
  expect(published.status()).toBe(200);
}
