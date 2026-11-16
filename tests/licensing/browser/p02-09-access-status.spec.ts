import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page, type Response } from '@playwright/test';
import { createOwner, origin, signIn, type Enrolled } from './p01-12-fixture';

const merchant = process.env['P0209_MERCHANT_WEB']!;
const evidence = process.env['P0209_EVIDENCE']!;
const account = process.env['P0209_ACCOUNT']!;
const password = process.env['P0209_PASSWORD']!;
const time = JSON.parse(readFileSync(join(process.cwd(), '../../tests/contracts/licensing/time-v1.json'), 'utf8'));
const ist = (utc: string) => time.display.find((row: { utc: string }) => row.utc === utc).ist as string;
const paidThrough = ist('2027-02-28T04:30:00Z');
const graceEnds = ist('2027-03-07T04:30:00Z');

/** Status values that must match exactly between every reader of the same evaluation. */
const basis = (term: Record<string, unknown>) => ({
  state: term['state'], basis: term['basis'], startsAt: term['startsAt'], paidThrough: term['paidThrough'],
  graceEndsAt: term['graceEndsAt'], accessEndsAt: term['accessEndsAt'], nextDeadline: term['nextDeadline'], purchasedCapacity: term['purchasedCapacity'],
  memberAllowance: term['memberAllowance'], timeZone: term['timeZone'],
});

async function signInMerchant(page: Page, email: string) {
  await page.goto(merchant + '/login');
  await page.locator('#identifier').fill(email);
  await page.locator('#password').evaluate((input: HTMLInputElement, secret) => {
    input.value = secret!;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }, password);
  const signedIn = page.waitForResponse(response => new URL(response.url()).pathname === '/api/auth/login'
    && response.request().method() === 'POST');
  await page.locator('button[type=submit]').click();
  expect((await signedIn).status()).toBe(200);
  await expect(page).not.toHaveURL(/\/login/);
}

function statusRead(page: Page, path: string | RegExp) {
  return page.waitForResponse(response => {
    const pathname = new URL(response.url()).pathname;
    return response.request().method() === 'GET' && (typeof path === 'string' ? pathname === path : path.test(pathname));
  });
}

async function openShopStatus(page: Page): Promise<Response> {
  const read = statusRead(page, /^\/api\/licensing\/shops\/[0-9a-f-]{36}\/status$/);
  await page.goto(merchant + '/billing/status');
  const response = await read;
  expect(response.headers()['cache-control']).toContain('no-store');
  return response;
}

async function staffPage(browser: Browser) {
  // A mobile staff device whose own clock zone is not IST: every deadline must still read in IST.
  const context = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true,
    timezoneId: 'America/Los_Angeles' });
  const page = await context.newPage();
  await signInMerchant(page, process.env['P0209_STAFF_EMAIL']!);
  return { context, page };
}

async function grant(page: Page, owner: Enrolled, capability: string) {
  await page.goto(origin + '/');
  await page.getByRole('link', { name: 'Manage administrator access' }).click();
  const users = await (await page.request.get(origin + '/api/admin/users')).json();
  const self = users.items.find((row: { email: string }) => row.email.toLowerCase() === owner.email.toLowerCase());
  await page.getByLabel('Administrator', { exact: true }).selectOption(self.id);
  await page.getByRole('checkbox', { name: capability, exact: true }).check();
  await page.getByLabel('Reason', { exact: true }).selectOption('responsibilities-changed');
  await page.getByLabel('I confirm these access changes.').check();
  await page.getByRole('button', { name: 'Save administrator access' }).click();
  await expect.poll(async () => (await page.request.get(origin + '/api/admin/auth/session')).status()).toBe(401);
  return signIn(page, owner);
}

async function providerLookup(page: Page) {
  await page.goto(origin + '/accounts');
  await page.getByLabel('Billing account ID').fill(account);
  const read = statusRead(page, `/api/admin/accounts/${account}/status`);
  await page.getByRole('button', { name: 'Read access status' }).click();
  const response = await read;
  expect(response.status()).toBe(200);
  expect(response.headers()['cache-control']).toBe('no-store');
  await expect(page.getByTestId('provider-term')).toHaveCount(3);
  return (await response.json()).items as { status: Record<string, unknown> }[];
}

function record(phase: string, observations: object) {
  writeFileSync(join(evidence, `observations-${phase}.json`), JSON.stringify({
    environment: 'Local fixture: shipping merchant and admin web on loopback, real Kestrel APIs and PostgreSQL',
    evaluationClock: 'Fixed licensing status clock 2027-03-02T00:00:00Z in the test host only',
    ...observations,
  }, null, 2));
}

test('@main merchant and provider explain the same access basis, capacity and IST deadline', async ({ browser }) => {
  const staff = await staffPage(browser);
  const payerContext = await browser.newContext({ viewport: { width: 1280, height: 900 }, timezoneId: 'Europe/London' });
  const adminContext = await browser.newContext({ viewport: { width: 1280, height: 900 }, timezoneId: 'UTC' });
  try {
    const shopResponse = await openShopStatus(staff.page);
    expect(shopResponse.status()).toBe(200);
    const shop = await shopResponse.json();
    expect(shop.accountId).toBeNull();
    expect(shop.term.commercial).toBeNull();
    await expect(staff.page.getByTestId('status-state')).toHaveText('In grace period');
    await expect(staff.page.getByTestId('paid-through')).toHaveText(paidThrough);
    await expect(staff.page.getByTestId('grace-ends')).toHaveText(graceEnds);
    await expect(staff.page.getByTestId('capacity')).toContainText('1');
    await expect(staff.page.getByTestId('member-allowance')).toHaveText('5');
    await expect(staff.page.getByTestId('observe-only')).toContainText('does not block');
    await expect(staff.page.getByTestId('commercial-price')).toHaveCount(0);
    await staff.page.screenshot({ path: join(evidence, 'merchant-staff-mobile.png'), fullPage: true });

    const payer = await payerContext.newPage();
    await signInMerchant(payer, process.env['P0209_PAYER_EMAIL']!);
    const accountRead = statusRead(payer, `/api/billing/accounts/${account}/subscriptions`);
    await payer.goto(`${merchant}/billing/accounts/${account}/status`);
    const accountResponse = await accountRead;
    expect(accountResponse.status()).toBe(200);
    const payerItems = (await accountResponse.json()).items as { status: Record<string, unknown> }[];
    const terms = payer.getByTestId('account-term');
    await expect(terms).toHaveCount(3);
    await expect(terms.filter({ hasText: 'Paid subscription' }).getByTestId('commercial-price')).toHaveText('₹1,499.00');
    await expect(terms.filter({ hasText: 'Trial' })).toContainText('Expired');
    await expect(terms.filter({ hasText: 'Trial' }).getByTestId('grace-ends')).toHaveText('No grace period');
    await expect(terms.filter({ hasText: 'Facts incomplete' })).toContainText('Grouped subscription, 3 shop slots');
    await payer.screenshot({ path: join(evidence, 'merchant-billing-only-desktop.png'), fullPage: true });
    const paidForPayer = payerItems.find(item => item.status['basis'] === 'Paid')!.status;
    expect(basis(paidForPayer)).toEqual(basis(shop.term));

    const admin = await adminContext.newPage();
    let owner = await signIn(admin, await createOwner(admin));
    await admin.goto(origin + '/accounts');
    await expect(admin.getByRole('alert')).toContainText('Only Support, Billing or Licensing Manager');
    const ownerOnly = await admin.request.get(`${origin}/api/admin/accounts/${account}/status`);
    expect(ownerOnly.status()).toBe(403);

    owner = await grant(admin, owner, 'Support');
    const supportItems = await providerLookup(admin);
    const supportPaid = supportItems.find(item => item.status['basis'] === 'Paid')!.status;
    expect(basis(supportPaid)).toEqual(basis(shop.term));
    expect(supportItems.every(item => item.status['commercial'] === null)).toBe(true);
    await expect(admin.getByTestId('provider-price')).toHaveCount(0);
    const supportPanel = admin.getByTestId('provider-term').filter({ hasText: 'In grace period' });
    await expect(supportPanel).toContainText(paidThrough);
    await expect(supportPanel).toContainText(graceEnds);
    await expect(supportPanel).toContainText('Independent, 1');
    await expect(admin.getByTestId('provider-term').filter({ hasText: 'Trial' })).toContainText('No grace period');
    await admin.screenshot({ path: join(evidence, 'provider-support-desktop.png'), fullPage: true });

    owner = await grant(admin, owner, 'Billing');
    await providerLookup(admin);
    await expect(admin.getByTestId('provider-term').filter({ hasText: 'Paid subscription' }).getByTestId('provider-price'))
      .toHaveText('₹1,499.00');
    await admin.setViewportSize({ width: 390, height: 844 });
    await admin.screenshot({ path: join(evidence, 'provider-billing-mobile.png'), fullPage: true });

    record('main', {
      merchantStaff: { status: 200, state: 'Grace', paidThrough, graceEnds, browserZone: 'America/Los_Angeles',
        commercialVisible: false, observeOnlyNotice: true },
      merchantBillingOnly: { status: 200, terms: payerItems.length, price: '₹1,499.00', trialGrace: null,
        legacyEvidence: 'Incomplete', browserZone: 'Europe/London' },
      providerPlatformOwnerOnly: 403,
      providerSupport: { status: 200, commercialVisible: false, sameBasisAsMerchant: true },
      providerBilling: { status: 200, price: '₹1,499.00' },
      sharedBasis: basis(shop.term),
    });
  } finally {
    await Promise.all([staff.context.close(), payerContext.close(), adminContext.close()]);
  }
});

async function outage(browser: Browser, phase: string, expected: 'unavailable' | 'recovered') {
  const staff = await staffPage(browser);
  try {
    const response = await openShopStatus(staff.page);
    if (expected === 'unavailable') {
      expect(response.status()).toBe(503);
      expect((await response.json()).code).toBe('Billing.DelegationUnavailable');
      await expect(staff.page.getByTestId('verification-unavailable')).toContainText('not an expiry');
      await expect(staff.page.getByTestId('status-state')).toHaveCount(0);
      await expect(staff.page.getByText(paidThrough)).toHaveCount(0);
    } else {
      expect(response.status()).toBe(200);
      await expect(staff.page.getByTestId('status-state')).toHaveText('In grace period');
      await expect(staff.page.getByTestId('grace-ends')).toHaveText(graceEnds);
    }
    await staff.page.screenshot({ path: join(evidence, `merchant-${phase}-mobile.png`), fullPage: true });
    record(phase, { merchantStaffStatus: response.status(), shown: expected });
  } finally {
    await staff.context.close();
  }
}

test('@storage-outage a licensing storage failure is verification unavailable, not expiry', async ({ browser }) =>
  outage(browser, 'storage-outage', 'unavailable'));

test('@recovered the same evaluation returns once licensing storage recovers', async ({ browser }) =>
  outage(browser, 'recovered', 'recovered'));

test('@unreachable a stopped licensing host is verification unavailable, not expiry', async ({ browser }) =>
  outage(browser, 'unreachable', 'unavailable'));
