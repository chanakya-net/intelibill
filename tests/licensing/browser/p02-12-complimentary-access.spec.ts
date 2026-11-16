import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Locator, type Page } from '@playwright/test';
import { createOwner, origin, signIn, type Enrolled } from './p01-12-fixture';

const merchant = process.env['P0212_MERCHANT_WEB']!;
const evidence = process.env['P0212_EVIDENCE']!;
const account = process.env['P0212_ACCOUNT']!;
const [tradingShop, paidShop] = process.env['P0212_SHOPS']!.split(',');
const password = process.env['P0212_PASSWORD']!;
const grantPath = `/api/admin/accounts/${account}/grants/complimentary`;
// Fixture instants: 1 Mar 2027 00:00 IST to 1 Apr 2027 00:00 IST (end-exclusive), entered in IST.
const courtesyStarts = '1 Mar 2027, 00:00:00 IST';
const courtesyEnds = '1 Apr 2027, 00:00:00 IST';

function record(phase: string, observations: object) {
  writeFileSync(join(evidence, `observations-${phase}.json`), JSON.stringify({
    environment: 'Local fixture: shipping merchant and admin web on loopback, real Kestrel APIs and PostgreSQL',
    lifecycleClock: 'Fixed licensing lifecycle clock in the test host only; sessions, delegations and MFA use real time',
    ...observations,
  }, null, 2));
}

async function grantCapability(page: Page, owner: Enrolled, capability: string) {
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

async function openAccount(page: Page) {
  await page.goto(origin + '/accounts');
  await page.getByLabel('Billing account ID').fill(account);
  const read = page.waitForResponse(response => new URL(response.url()).pathname === `/api/admin/accounts/${account}/status`);
  await page.getByRole('button', { name: 'Read access status' }).click();
  expect((await read).status()).toBe(200);
}

async function fillGrant(editor: Locator) {
  await editor.getByLabel(/Named shop IDs/).fill(`${tradingShop}\n${paidShop}`);
  await editor.getByLabel('Starts (IST)').fill('2027-03-01T00:00');
  await editor.getByLabel('Ends, exclusive (IST)').fill('2027-04-01T00:00');
  await editor.getByLabel('Members per shop').fill('4');
  await editor.getByLabel(/^Reason/).fill('Launch partner courtesy (local fixture)');
}

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

async function staffStatus(browser: Browser) {
  // A mobile staff device whose own zone is not IST: every courtesy instant still reads in IST.
  const context = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true,
    timezoneId: 'America/Los_Angeles' });
  const page = await context.newPage();
  await signInMerchant(page, process.env['P0212_STAFF_EMAIL']!);
  const read = page.waitForResponse(response => /^\/api\/licensing\/shops\/[0-9a-f-]{36}\/status$/.test(new URL(response.url()).pathname));
  await page.goto(merchant + '/billing/status');
  const response = await read;
  expect(response.status()).toBe(200);
  expect(response.headers()['cache-control']).toContain('no-store');
  return { context, page, body: await response.json() };
}

test('@grant provider previews, confirms and recovers a dated grant after a lost response', async ({ browser }) => {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, timezoneId: 'UTC' });
  const admin = await context.newPage();
  try {
    let owner = await signIn(admin, await createOwner(admin));
    // Platform Owner alone: no editor and the API refuses before anything is disclosed.
    await admin.goto(origin + '/accounts');
    await expect(admin.getByTestId('grant-editor')).toHaveCount(0);
    const context0 = await (await admin.request.get(origin + '/api/admin/auth/sign-in/context')).json();
    const denied = await admin.request.post(origin + grantPath + '/preview', {
      headers: { 'X-Admin-CSRF': context0.requestToken, Origin: origin },
      data: { shopIds: [tradingShop], startsAt: '2027-03-01T00:00:00+05:30', endsAt: '2027-04-01T00:00:00+05:30', memberAllowance: 4, reason: 'x' },
    });
    expect(denied.status()).toBe(403);
    expect((await denied.json()).code).toBe('Grant.AccessDenied');

    owner = await grantCapability(admin, owner, 'Licensing Manager');
    await openAccount(admin);
    const editor = admin.getByTestId('grant-editor');
    await fillGrant(editor);
    await editor.getByRole('button', { name: 'Preview grant' }).click();
    const preview = editor.getByTestId('grant-preview');
    await expect(preview.getByTestId('grant-preview-starts')).toHaveText(courtesyStarts);
    await expect(preview.getByTestId('grant-preview-ends')).toContainText(courtesyEnds);
    await expect(preview.getByTestId('grant-preview-capacity')).toHaveText('Grouped, 2 named shop(s)');
    await expect(preview.getByTestId('grant-preview-payment')).toContainText('None recorded');
    await expect(preview.getByTestId('grant-preview-grace')).toHaveText('No grace period');
    await expect(preview.getByTestId('grant-preview-shop').filter({ hasText: 'Paid subscription' }))
      .toContainText('Existing access stays in effect first.');
    await admin.screenshot({ path: join(evidence, 'provider-preview.png'), fullPage: true });

    // A second tab for the same administrator and account previews the same facts before tab A sends.
    const tabB = await context.newPage();
    let tabBGrantPosts = 0;
    tabB.on('request', request => {
      if (request.method() === 'POST' && new URL(request.url()).pathname === grantPath) tabBGrantPosts++;
    });
    await openAccount(tabB);
    const editorB = tabB.getByTestId('grant-editor');
    await fillGrant(editorB);
    await editorB.getByRole('button', { name: 'Preview grant' }).click();
    await expect(editorB.getByTestId('grant-preview-capacity')).toHaveText('Grouped, 2 named shop(s)');

    // The confirmation reaches the server, but its response is lost: the outcome is unknown.
    let committed = 0;
    await admin.route('**' + grantPath, async route => {
      if (route.request().method() !== 'POST' || committed) return route.continue();
      committed = (await route.fetch()).status();
      await route.abort('connectionreset');
    });
    await preview.getByRole('checkbox').check();
    await preview.getByRole('button', { name: 'Confirm grant' }).click();
    await expect(editor.getByTestId('grant-error')).toContainText('unconfirmed');
    await expect(editor.getByTestId('grant-pending')).toBeVisible();
    const key = (await editor.getByTestId('grant-pending-key').textContent())!.trim();
    await expect.poll(() => committed).toBe(201);
    await admin.unroute('**' + grantPath);

    // Tab B now confirms its own preview: it must not send, overwrite or clear tab A's unknown operation.
    await editorB.getByTestId('grant-preview').getByRole('checkbox').check();
    await editorB.getByRole('button', { name: 'Confirm grant' }).click();
    await expect(editorB.getByTestId('grant-error')).toContainText('another tab');
    await expect(editorB.getByTestId('grant-pending-key')).toHaveText(key);
    const stored = await tabB.evaluate(([prefix]) => Object.keys(localStorage).filter(item => item.startsWith(prefix)),
      [`intelibill.admin.complimentary-grant.v1.`]);
    expect(stored).toHaveLength(1);
    expect(stored[0]).toContain(key);
    await tabB.screenshot({ path: join(evidence, 'second-tab-refused.png'), fullPage: true });
    await tabB.close();
    expect(tabBGrantPosts).toBe(0);

    // Reload: the original operation is restored; a temporary lookup 404 stays unknown and never mints a new key.
    await admin.reload();
    await openAccount(admin);
    await expect(editor.getByTestId('grant-pending-key')).toHaveText(key);
    let missing = 0;
    await admin.route(`**${grantPath}/operations/${key}`, async route => {
      if (missing++ === 0) return route.fulfill({ status: 404, contentType: 'application/json', body: '{"code":"Grant.OperationNotFound"}' });
      return route.continue();
    });
    await editor.getByRole('button', { name: 'Check grant outcome' }).click();
    await expect(editor.getByTestId('grant-error')).toContainText('unconfirmed');
    await expect(editor.getByTestId('grant-pending-key')).toHaveText(key);
    const recovered = admin.waitForResponse(response => new URL(response.url()).pathname === `${grantPath}/operations/${key}` && response.status() === 200);
    await editor.getByRole('button', { name: 'Check grant outcome' }).click();
    const result = await (await recovered).json();
    await expect(editor.getByTestId('grant-result-key')).toHaveText(key);
    await expect(editor.getByTestId('grant-result-ends')).toHaveText(courtesyEnds);
    const courtesy = admin.getByTestId('provider-term').filter({ hasText: 'Courtesy access' });
    await expect(courtesy.getByTestId('provider-courtesy-ends')).toHaveText(courtesyEnds);
    await expect(courtesy).toContainText('Included capacity');
    await expect(courtesy).not.toContainText('Paid through');
    await expect(courtesy.getByTestId('provider-price')).toHaveCount(0);
    await admin.screenshot({ path: join(evidence, 'provider-confirmed.png'), fullPage: true });
    await admin.setViewportSize({ width: 390, height: 844 });
    await admin.screenshot({ path: join(evidence, 'mobile-grant.png'), fullPage: true });

    record('grant', {
      providerPlatformOwnerOnly: { editorShown: false, previewStatus: 403, code: 'Grant.AccessDenied' },
      preview: { starts: courtesyStarts, ends: courtesyEnds, capacity: 'Grouped, 2 named shop(s)', payment: 'None recorded',
        grace: 'No grace period', paidShopKeepsBaselineFirst: true, browserZone: 'UTC' },
      lostResponse: { serverStatus: committed, browserOutcome: 'unknown', operationKey: key },
      secondTab: { previewedBeforeTabASent: true, grantRequestsSent: tabBGrantPosts, shownOperationKey: key,
        storedOperationKeys: stored.length, tabAKeyRetained: stored[0].endsWith(key) },
      reloadRecovery: { temporaryLookup404KeptKey: true, recoveredOperationKey: result.operationKey, sameKey: result.operationKey === key,
        grantedVersion: result.grantedVersion, endsAt: result.endsAt, paymentRecorded: result.paymentRecorded, graceEndsAt: result.graceEndsAt },
      providerStatus: { basis: 'Courtesy access', ends: courtesyEnds, label: 'Included capacity', priceShown: false },
      viewports: ['1280x900', '390x844'],
      operationKey: key,
    });
  } finally {
    await context.close();
  }
});

test('@active merchant staff and billing-only owner see the same exact courtesy end', async ({ browser }) => {
  const staff = await staffStatus(browser);
  const payerContext = await browser.newContext({ viewport: { width: 1280, height: 900 }, timezoneId: 'Europe/London' });
  try {
    expect(staff.body.accountId).toBeNull();
    expect(staff.body.term.basis).toBe('Complimentary');
    expect(staff.body.term.commercial).toBeNull();
    expect(staff.body.term.graceEndsAt).toBeNull();
    expect(JSON.stringify(staff.body)).not.toContain('Launch partner');
    await expect(staff.page.getByTestId('status-state')).toHaveText('Active');
    await expect(staff.page.getByTestId('paid-through')).toHaveText(courtesyEnds);
    await expect(staff.page.getByTestId('starts-at')).toHaveText(courtesyStarts);
    await expect(staff.page.getByTestId('grace-ends')).toHaveText('No grace period');
    await expect(staff.page.getByTestId('member-allowance')).toHaveText('4');
    await expect(staff.page.getByTestId('status-term')).toContainText('Courtesy access ends');
    await expect(staff.page.getByTestId('status-term')).not.toContainText('Paid through');
    await expect(staff.page.getByTestId('courtesy-note')).toContainText('no payment is recorded');
    await expect(staff.page.getByTestId('commercial-price')).toHaveCount(0);
    await staff.page.screenshot({ path: join(evidence, 'merchant-active.png'), fullPage: true });

    const payer = await payerContext.newPage();
    await signInMerchant(payer, process.env['P0212_PAYER_EMAIL']!);
    const read = payer.waitForResponse(response => new URL(response.url()).pathname === `/api/billing/accounts/${account}/subscriptions`);
    await payer.goto(`${merchant}/billing/accounts/${account}/status`);
    expect((await read).status()).toBe(200);
    const courtesy = payer.getByTestId('account-term').filter({ hasText: 'Courtesy access' });
    await expect(courtesy.getByTestId('paid-through')).toHaveText(courtesyEnds);
    await expect(courtesy.getByTestId('commercial-price')).toHaveCount(0);
    await expect(courtesy).toContainText('Included capacity');
    await expect(payer.getByTestId('account-term').filter({ hasText: 'Paid subscription' }).getByTestId('commercial-price'))
      .toHaveText('₹1,499.00');
    record('active', {
      merchantStaff: { state: 'Active', basis: 'Complimentary', ends: courtesyEnds, starts: courtesyStarts, memberAllowance: 4,
        accountIdVisible: false, commercialVisible: false, grace: null, reasonVisible: false, browserZone: 'America/Los_Angeles' },
      merchantBillingOnly: { courtesyEnds, courtesyPriceShown: false, paidPriceShown: '₹1,499.00', browserZone: 'Europe/London' },
    });
  } finally {
    await Promise.all([staff.context.close(), payerContext.close()]);
  }
});

test('@expired at the exact end the grant is expired with no grace', async ({ browser }) => {
  const staff = await staffStatus(browser);
  try {
    expect(staff.body.state).toBe('Expired');
    expect(staff.body.term.basis).toBe('Complimentary');
    expect(staff.body.term.graceEndsAt).toBeNull();
    await expect(staff.page.getByTestId('status-state')).toHaveText('Expired');
    await expect(staff.page.getByTestId('paid-through')).toHaveText(courtesyEnds);
    await expect(staff.page.getByTestId('grace-ends')).toHaveText('No grace period');
    await expect(staff.page.getByTestId('observe-only')).toBeVisible();
    await staff.page.screenshot({ path: join(evidence, 'merchant-expired.png'), fullPage: true });
    record('expired', { merchantStaff: { state: 'Expired', ends: courtesyEnds, grace: null, observeOnlyNotice: true } });
  } finally {
    await staff.context.close();
  }
});
