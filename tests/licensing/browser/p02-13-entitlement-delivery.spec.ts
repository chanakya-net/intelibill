import { randomUUID } from 'node:crypto';
import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page } from '@playwright/test';
import { adminPost, createOwner, origin as admin, signIn } from './p01-12-fixture';

const merchant = process.env['P0213_MERCHANT_WEB']!;
const evidence = process.env['P0213_EVIDENCE']!;
const account = process.env['P0213_ACCOUNT']!;
const trialEnd = '24 Feb 2027, 05:30:00.123456 IST';

async function merchantSession(page: Page, role: 'owner' | 'staff'): Promise<string> {
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

async function merchantStatus(page: Page, phase: string) {
  const pending = page.waitForResponse(response => /^\/api\/licensing\/shops\/[0-9a-f-]{36}\/status$/.test(
    new URL(response.url()).pathname));
  await page.goto(merchant + '/billing/status');
  const response = await pending;
  expect(response.headers()['cache-control']).toContain('no-store');
  await expect(page.getByTestId('observe-only')).toContainText('does not block');
  if (phase === 'outage') {
    expect(response.status()).toBe(503);
    await expect(page.getByTestId('verification-unavailable')).toContainText('not an expiry');
    await expect(page.getByTestId('status-state')).toHaveCount(0);
    return { http: 503, view: 'Unavailable' };
  }
  expect(response.status()).toBe(200);
  const body = await response.json();
  expect(body.accountId).toBeNull();
  expect(body.term.commercial).toBeNull();
  expect(body.entitlementSynchronization).toBe('NotAvailable');
  expect(body.term.basis).toBe('Trial');
  expect(body.state).toBe(phase === 'expired' ? 'Expired' : 'Active');
  await expect(page.getByTestId('paid-through')).toHaveText(trialEnd);
  await expect(page.getByTestId('commercial-price')).toHaveCount(0);
  return { http: 200, view: body.state, basis: body.term.basis, deadline: body.term.accessEndsAt, priceVisible: false };
}

async function providerStatus(browser: Browser) {
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 },
    storageState: process.env['P0213_PROVIDER_STATE']! });
  try {
    await page.goto(admin + '/accounts');
    await page.getByLabel('Billing account ID').fill(account);
    const read = page.waitForResponse(response => new URL(response.url()).pathname === `/api/admin/accounts/${account}/status`);
    await page.getByRole('button', { name: 'Read access status' }).click();
    expect((await read).status()).toBe(200);
    await expect(page.getByTestId('provider-term')).toContainText('Trial');
    await expect(page.getByTestId('provider-trial-ends')).toHaveText(trialEnd);
    await page.screenshot({ path: join(evidence, 'provider-desktop.png'), fullPage: true });
    await page.setViewportSize({ width: 390, height: 844 });
    await page.screenshot({ path: join(evidence, 'provider-mobile.png'), fullPage: true });
  } finally {
    await page.close();
  }
}

async function commerce(browser: Browser, staff: Page, staffToken: string, phase: string) {
  const owner = await browser.newPage();
  try {
    const ownerToken = await merchantSession(owner, 'owner');
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

test('@prepare provider signs in with MFA and publishes the local trial policy', async ({ page }) => {
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
});

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

for (const phase of ['delayed', 'recovered', 'outage', 'expired']) {
  test(`@${phase} shipping billing status and merchant commerce`, async ({ browser }) => {
    const staff = await browser.newPage({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true,
      timezoneId: 'America/Los_Angeles' });
    try {
      const token = await merchantSession(staff, 'staff');
      const shown = await merchantStatus(staff, phase);
      await staff.screenshot({ path: join(evidence, `merchant-${phase}-mobile.png`), fullPage: true });
      if (phase === 'delayed') await providerStatus(browser);
      const stock = await commerce(browser, staff, token, phase);
      const privateStatus: number[] = [];
      for (const web of [merchant, admin]) {
        for (const action of ['entitlements', 'merchant-events/acknowledgement', 'merchant-events/reconciliation']) {
          const probe = await staff.request.post(`${web}/internal/licensing/v1/${action}`, { data: {} });
          if (probe.status() === 200) {
            // Merchant SPA fallback. The API-side ingress counters also stay flat.
            expect(probe.headers()['content-type']).toContain('text/html');
          } else {
            expect([404, 405]).toContain(probe.status());
          }
          privateStatus.push(probe.status());
        }
      }
      writeFileSync(join(evidence, `observations-${phase}.json`), JSON.stringify({ shown, stock, privateStatus }, null, 2));
    } finally {
      await staff.close();
    }
  });
}
