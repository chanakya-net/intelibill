import { existsSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page } from '@playwright/test';
import { fillSecret, otp } from './p01-12-fixture';

function required(name: string) {
  const value = process.env['P0701_' + name];
  if (!value) throw new Error('Missing P0701 variable ' + name);
  return value;
}

const adminWeb = required('ADMIN_WEB');
const merchantWeb = required('MERCHANT_WEB');
const scratch = required('TEMPORARY');
const evidence = required('EVIDENCE_DIR');
const cfg = JSON.parse(readFileSync(join(scratch, 'fixture.json'), 'utf8'));

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
  return { context, page };
}

async function signInAdmin(page: Page, email: string, seed: string, timeSeconds: number) {
  await page.goto(`${adminWeb}/auth/sign-in`);
  await page.getByLabel('Email', { exact: true }).fill(email);
  await fillSecret(page, 'Password', cfg.adminPassword);
  await fillSecret(page, 'Authenticator code', otp(seed, Math.floor(timeSeconds / 30)));
  const reply = page.waitForResponse(
    (r) => r.url().endsWith('/api/admin/auth/sign-in') && r.request().method() === 'POST',
  );
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  expect((await reply).status()).toBe(200);
  await expect(page).toHaveURL(`${adminWeb}/`);
}

async function signalHost(reqName: string, ackName: string, body = 'go', timeoutMs = 20_000) {
  const ackPath = join(scratch, ackName);
  if (existsSync(ackPath)) rmSync(ackPath);
  writeFileSync(join(scratch, reqName), body);
  const started = Date.now();
  while (!existsSync(ackPath)) {
    if (Date.now() - started > timeoutMs) throw new Error(`Timed out waiting for ${ackName}`);
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
  const result = readFileSync(ackPath, 'utf8').trim();
  rmSync(ackPath);
  return result;
}

async function settleFrames(page: Page) {
  await page.evaluate(() => new Promise<void>((r) => requestAnimationFrame(() => requestAnimationFrame(() => r()))));
}

async function merchantAuthedFetch(page: Page, path: string): Promise<{ status: number; text: string }> {
  return page.evaluate(async (targetPath) => {
    const raw =
      localStorage.getItem('inventory.auth.session.local') ??
      sessionStorage.getItem('inventory.auth.session.temporary');
    const token = raw ? (JSON.parse(raw) as { accessToken?: string }).accessToken : undefined;
    const headers: Record<string, string> = {};
    if (token) headers['Authorization'] = `Bearer ${token}`;
    const res = await fetch(targetPath, { headers, credentials: 'include' });
    return { status: res.status, text: await res.text() };
  }, path);
}

async function createDelayedRouteGate(page: Page, pattern: string) {
  let markIntercepted!: () => void;
  let release!: () => void;
  let markSettled!: (outcome: string) => void;
  const intercepted = new Promise<void>((r) => { markIntercepted = r; });
  const hold = new Promise<void>((r) => { release = r; });
  const settled = new Promise<string>((r) => { markSettled = r; });
  let count = 0;

  await page.route(pattern, async (route) => {
    if (count === 0) {
      count++;
      markIntercepted();
      try {
        const upstream = await route.fetch();
        const body = await upstream.text();
        await hold;
        await route.fulfill({ response: upstream, body });
        markSettled('fulfilled');
      } catch {
        markSettled('aborted');
      }
      return;
    }
    await route.continue();
  });

  return {
    intercepted,
    settled,
    release,
    hits: () => count,
    cleanup: () => page.unroute(pattern),
  };
}

async function verifyStaffOperationalBoundaries(page: Page) {
  const noticeReplyPromise = page.waitForResponse(
    (r) => r.url().includes('/api/licensing/shops/') && r.url().endsWith('/notices') && r.request().method() === 'GET',
  );
  await page.goto(`${merchantWeb}/`);
  const noticeReply = await noticeReplyPromise;
  expect(noticeReply.status()).toBe(200);
  expect(noticeReply.headers()['cache-control'] ?? '').toContain('no-store');
  const staffJsonText = await noticeReply.text();
  const staffJson = JSON.parse(staffJsonText);
  expect(staffJson.notices.length).toBe(1);
  expect(staffJsonText).not.toContain('12000');
  expect(staffJsonText).not.toContain('INR');
  expect(staffJsonText).not.toContain('evidencedTermPricePaise');
  expect(staffJsonText).not.toContain(cfg.accountA);
  expect(staffJsonText).not.toContain(cfg.accountB);

  const banner = page.getByTestId('shop-license-notice');
  await expect(banner).toBeVisible();
  await expect(banner).toContainText('North Shop');
  await expect(banner).toContainText('29 Feb 2028, 10:00:00 IST');
  const guidanceText = (await page.getByTestId('shop-license-notice-guidance').innerText()).toLowerCase();
  expect(guidanceText).toContain('contact the account billing owner or manager');
  expect(guidanceText).not.toContain('renew');
  const ariaText = await page.getByTestId('shop-license-notices').ariaSnapshot();
  expect(ariaText).toContain('North Shop');
  expect(ariaText).not.toContain('120');
  await page.screenshot({ path: join(evidence, 'merchant-staff-operational-notice.png'), fullPage: true });
  return { status: noticeReply.status(), cacheControl: noticeReply.headers()['cache-control'], count: staffJson.notices.length };
}

async function verifyStaffClockProgression(page: Page) {
  const steps = [
    { at: '2028-02-28T04:30:00Z', threshold: 'PaidMinus1Day', state: 'Active' },
    { at: '2028-02-29T04:30:00Z', threshold: 'GraceStart', state: 'Grace' },
    { at: '2028-03-06T04:30:00Z', threshold: 'GraceMinus1Day', state: 'Grace' },
    { at: '2028-03-07T04:30:00Z', threshold: 'GraceExpiry', state: 'Expired' },
  ];
  for (const step of steps) {
    await signalHost('advance-clock.req', 'advance-clock.ack', step.at);
    const replyPromise = page.waitForResponse(
      (r) => r.url().includes(`/api/licensing/shops/${cfg.northShopId}/notices`) && r.request().method() === 'GET',
    );
    await page.reload();
    const json = await (await replyPromise).json();
    expect(json.notices.length).toBe(1);
    expect(json.notices[0].threshold).toBe(step.threshold);
    expect(json.notices[0].accessState).toBe(step.state);
    await expect(page.getByTestId('shop-license-notice')).toHaveCount(1);
    const guidance = (await page.getByTestId('shop-license-notice-guidance').innerText()).toLowerCase();
    expect(guidance).toContain('contact the account billing owner or manager');
    expect(guidance).not.toContain('renew');
  }

  await signalHost('advance-clock.req', 'advance-clock.ack', '2028-02-22T04:30:00Z');
}

async function verifyStaffStaleShopSwitchAndLogout(page: Page) {
  const gate = await createDelayedRouteGate(page, `**/api/licensing/shops/${cfg.northShopId}/notices**`);
  const shopsLoaded = page.waitForResponse((r) => r.url().endsWith('/api/shops/me') && r.status() === 200);
  await page.reload();
  await Promise.all([gate.intercepted, shopsLoaded]);
  expect(gate.hits()).toBe(1);

  const switchToSouth = page.waitForResponse(
    (r) => r.url().endsWith('/api/shops/switch') && r.request().method() === 'POST',
  );
  await page.locator('.active-shop-trigger-desktop').click();
  await page.getByRole('menuitem', { name: /South Shop/ }).click();
  expect((await switchToSouth).status()).toBe(200);
  await expect(page.getByTestId('shop-license-notice')).toContainText('South Shop');

  gate.release();
  const outcome = await gate.settled;
  await settleFrames(page);
  expect(['fulfilled', 'aborted']).toContain(outcome);
  await expect(page.getByTestId('shop-license-notice')).toContainText('South Shop');
  await expect(page.getByTestId('shop-license-notice')).not.toContainText('North Shop');
  await gate.cleanup();

  const switchToNorth = page.waitForResponse(
    (r) => r.url().endsWith('/api/shops/switch') && r.request().method() === 'POST',
  );
  await page.locator('.active-shop-trigger-desktop').click();
  await page.getByRole('menuitem', { name: /North Shop/ }).click();
  expect((await switchToNorth).status()).toBe(200);
  await expect(page.getByTestId('shop-license-notice')).toContainText('North Shop');
  await page.locator('.sidebar-profile .p-panelmenu-header-link').first().click();
  await page.locator('.sidebar-profile').getByText('Logout', { exact: true }).click();
  await expect(page).toHaveURL(/\/login/);
  await expect(page.getByTestId('shop-license-notice')).toHaveCount(0);
  return outcome;
}

async function verifyBillingManagerPartialSuspensionStaleReplyAndRenewal(page: Page) {
  await page.setViewportSize({ width: 1280, height: 860 });
  await page.goto(`${merchantWeb}/billing/accounts/${cfg.accountA}/status`);
  const accountNotice = page.getByTestId('account-license-notice');
  await expect(accountNotice).toBeVisible();
  await expect(page.getByTestId('account-notice-price')).toContainText('120');
  await expect(page.getByTestId('account-notice-quote-note')).toBeVisible();
  await page.screenshot({ path: join(evidence, 'merchant-billing-manager-commercial-notice.png'), fullPage: true });

  // Partial suspension (North suspended, South active): raw commercial JSON & DOM must not promise uninterrupted trading.
  await signalHost('suspend-north.req', 'suspend-north.ack');
  const suspReply = page.waitForResponse((r) => r.url().includes(`/api/billing/accounts/${cfg.accountA}/notices`));
  await page.reload();
  const suspJson = await (await suspReply).json();
  expect(suspJson.notices.length).toBe(1);
  expect(suspJson.notices[0].paymentDoesNotLiftSuspension).toBe(true);
  expect(suspJson.notices[0].guidanceCode).toBe('PaidRenewalPartialSuspension');
  await expect(accountNotice).toContainText('At least one covered shop is suspended');
  await expect(accountNotice).not.toContainText('keep trading uninterrupted');

  await signalHost('release-north.req', 'release-north.ack');
  const relReply = page.waitForResponse((r) => r.url().includes(`/api/billing/accounts/${cfg.accountA}/notices`));
  await page.reload();
  expect((await (await relReply).json()).notices[0].paymentDoesNotLiftSuspension).toBe(false);

  // Deterministic delayed commercial Account A response released after SPA navigation away and unauthorized Account B view.
  const commercialGate = await createDelayedRouteGate(page, `**/api/billing/accounts/${cfg.accountA}/notices**`);
  await page.getByTestId('status-retry').click();
  await commercialGate.intercepted;
  expect(commercialGate.hits()).toBe(1);

  await page.locator('a[href="/billing"]').click();
  await expect(page).toHaveURL(`${merchantWeb}/billing`);
  commercialGate.release();
  const commercialOutcome = await commercialGate.settled;
  await settleFrames(page);
  expect(['fulfilled', 'aborted']).toContain(commercialOutcome);
  await expect(page.getByTestId('account-license-notice')).toHaveCount(0);
  await commercialGate.cleanup();

  await page.goto(`${merchantWeb}/billing/accounts/${cfg.accountB}/status`);
  await expect(page.locator('.status-page [role="alert"]')).toBeVisible();
  await expect(page.getByTestId('account-license-notice')).toHaveCount(0);
  expect(await page.locator('body').innerText()).not.toContain('North Shop');
  expect(await page.locator('body').innerText()).not.toContain('₹120');

  // Renewal: old predecessor deadline must no longer be presented as current on Account A.
  await signalHost('renew-account-a.req', 'renew-account-a.ack');
  const postRenew = page.waitForResponse((r) => r.url().includes(`/api/billing/accounts/${cfg.accountA}/notices`));
  await page.goto(`${merchantWeb}/billing/accounts/${cfg.accountA}/status`);
  expect((await (await postRenew).json()).notices.length).toBe(0);
  await expect(page.getByTestId('account-license-notice')).toHaveCount(0);
  return commercialOutcome;
}

test('MerchantNoticesFollowLiveShopAndBillingAuthority', async ({ browser }) => {
  const merchantEvidence: Record<string, unknown> = {};

  const staffSession = await signInMerchant(browser, cfg.staffEmail);
  try {
    await staffSession.page.setViewportSize({ width: 1280, height: 860 });
    const initial = await verifyStaffOperationalBoundaries(staffSession.page);
    const staffDeniedAccount = await merchantAuthedFetch(staffSession.page, `/api/billing/accounts/${cfg.accountA}/notices`);
    expect(staffDeniedAccount.status).toBe(403);
    expect(staffDeniedAccount.text).not.toContain('12000');

    await verifyStaffClockProgression(staffSession.page);
    const staleShopOutcome = await verifyStaffStaleShopSwitchAndLogout(staffSession.page);
    merchantEvidence.staff = {
      ...initial,
      accountReadStatus: staffDeniedAccount.status,
      staleShopReplyOutcome: staleShopOutcome,
      coherentThroughGraceExpiry: true,
    };
  } finally {
    await staffSession.context.close();
  }

  const shopOwnerSession = await signInMerchant(browser, cfg.southOwnerEmail);
  try {
    await shopOwnerSession.page.goto(`${merchantWeb}/`);
    const banner = shopOwnerSession.page.getByTestId('shop-license-notice');
    await expect(banner).toBeVisible();
    await expect(banner).toContainText('South Shop');
    await expect(banner).not.toContainText('North Shop');
    await expect(banner).not.toContainText('120');
    const ownerGuidance = (await shopOwnerSession.page.getByTestId('shop-license-notice-guidance').innerText()).toLowerCase();
    expect(ownerGuidance).not.toContain('renew');
    const deniedAccount = (await merchantAuthedFetch(shopOwnerSession.page, `/api/billing/accounts/${cfg.accountA}/notices`)).status;
    expect(deniedAccount).toBe(403);
    merchantEvidence.nonBillingShopOwner = { shopBannerVisible: true, accountReadStatus: deniedAccount };
  } finally {
    await shopOwnerSession.context.close();
  }

  const managerSession = await signInMerchant(browser, cfg.billingManagerEmail);
  try {
    const staleCommercialOutcome = await verifyBillingManagerPartialSuspensionStaleReplyAndRenewal(managerSession.page);
    const deniedShop = (await merchantAuthedFetch(managerSession.page, `/api/licensing/shops/${cfg.northShopId}/notices`)).status;
    const deniedAccountB = (await merchantAuthedFetch(managerSession.page, `/api/billing/accounts/${cfg.accountB}/notices`)).status;
    expect(deniedShop).toBe(403);
    expect(deniedAccountB).toBe(403);
    merchantEvidence.billingOnlyManager = {
      commercialVisible: true,
      partialSuspensionVerified: true,
      staleCommercialReplyOutcome: staleCommercialOutcome,
      supersededPredecessorClearedAfterRenewal: true,
      shopReadStatus: deniedShop,
      accountBReadStatus: deniedAccountB,
    };
  } finally {
    await managerSession.context.close();
  }

  const contactCtx = await browser.newContext();
  try {
    const page = await contactCtx.newPage();
    await page.goto(`${merchantWeb}/login`);
    const [accStatus, shopStatus] = await page.evaluate(
      async ({ accountId, shopId, token }) => {
        const headers = { Authorization: `Bearer ${token}` };
        const a = await fetch(`/api/billing/accounts/${accountId}/notices`, { headers });
        const s = await fetch(`/api/licensing/shops/${shopId}/notices`, { headers });
        return [a.status, s.status];
      },
      { accountId: cfg.accountA, shopId: cfg.northShopId, token: cfg.contactAccessToken },
    );
    expect(accStatus).toBe(403);
    expect(shopStatus).toBe(403);
    merchantEvidence.contactOnly = { accountReadStatus: accStatus, shopReadStatus: shopStatus };
  } finally {
    await contactCtx.close();
  }

  writeFileSync(join(evidence, 'merchant-browser-assertions.json'), JSON.stringify(merchantEvidence, null, 2));
});

async function verifyBillingOperatorWithStaleCommercialRace(page: Page) {
  await page.goto(`${adminWeb}/accounts`);
  const previewReplyPromise = page.waitForResponse(
    (r) => r.url().includes(`/api/admin/accounts/${cfg.accountA}/notifications`) && r.request().method() === 'GET',
  );
  await page.getByLabel('Billing account ID').fill(cfg.accountA);
  await page.getByRole('button', { name: 'Read access status' }).click();
  const previewReply = await previewReplyPromise;
  expect(previewReply.status()).toBe(200);
  expect(previewReply.headers()['cache-control'] ?? '').toContain('no-store');
  expect((await previewReply.json()).commercialIncluded).toBe(true);

  const panel = page.getByTestId('provider-notifications-panel');
  await expect(panel.getByTestId('provider-notification-row').first()).toBeVisible();
  await expect(panel.getByTestId('provider-notification-due-utc').first()).toContainText('22 Feb 2028, 04:30:00 UTC');
  await expect(panel.getByTestId('provider-notification-due-ist').first()).toContainText('22 Feb 2028, 10:00:00 IST');
  await expect(panel.getByTestId('provider-notification-price').first()).toContainText('120');

  const renderedIds = await panel.getByTestId('provider-notification-row').evaluateAll((nodes) =>
    nodes.map((n) => n.getAttribute('data-job-id')),
  );
  for (const id of renderedIds) expect(cfg.expectedJobIds).toContain(id);
  const badges = await panel.getByTestId('provider-notification-due-badge').allTextContents();
  expect(badges.some((text) => text.trim() === 'Due')).toBe(true);
  expect(badges.some((text) => text.trim() === 'Scheduled (future)')).toBe(true);

  const localeSelect = panel.getByTestId('provider-notifications-locale');
  await localeSelect.focus();
  await expect(localeSelect).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(panel.getByTestId('provider-notifications-refresh')).toBeFocused();
  await localeSelect.selectOption('hi-IN');
  await expect(panel.locator('#provider-notifications-heading')).toContainText('निर्धारित लाइसेंस सूचनाएं');
  await localeSelect.selectOption('en-IN');
  await expect(panel.locator('#provider-notifications-heading')).toContainText('Scheduled licence notifications');
  await page.screenshot({ path: join(evidence, 'admin-billing-commercial-preview.png'), fullPage: true });

  await page.setViewportSize({ width: 390, height: 844 });
  const scrollWidth = await page.evaluate(() => document.documentElement.scrollWidth);
  expect(scrollWidth).toBeLessThanOrEqual(390);
  await page.screenshot({ path: join(evidence, 'admin-billing-390px.png'), fullPage: true });

  // Deterministic race: hold a commercial refresh, revoke capability, fire focus revalidation (403), then release old commercial reply.
  const gate = await createDelayedRouteGate(page, `**/api/admin/accounts/${cfg.accountA}/notifications**`);
  await panel.getByTestId('provider-notifications-refresh').click();
  await gate.intercepted;
  expect(gate.hits()).toBe(1);

  await signalHost('revoke-billing-capability', 'billing-revoked', 'revoke');
  const deniedReply = page.waitForResponse(
    (r) => r.url().includes(`/api/admin/accounts/${cfg.accountA}/notifications`) && [401, 403].includes(r.status()),
  );
  await page.evaluate(() => window.dispatchEvent(new Event('focus')));
  expect([401, 403]).toContain((await deniedReply).status());
  await expect(panel.getByTestId('provider-notification-row')).toHaveCount(0);

  gate.release();
  const staleAdminOutcome = await gate.settled;
  await settleFrames(page);
  expect(['fulfilled', 'aborted']).toContain(staleAdminOutcome);
  await expect(panel.getByTestId('provider-notification-row')).toHaveCount(0);
  await expect(panel.getByTestId('provider-notification-commercial')).toHaveCount(0);
  await gate.cleanup();

  return {
    commercialIncluded: true,
    renderedJobCount: renderedIds.length,
    narrowScrollWidth: scrollWidth,
    staleCommercialAfterRevocationOutcome: staleAdminOutcome,
    browserLanguagesSwitched: ['hi-IN', 'en-IN'],
  };
}

test('ProviderPreviewUsesLiveCapabilityAndShowsScheduledState', async ({ browser }) => {
  const adminEvidence: Record<string, unknown> = {};

  const billingCtx = await browser.newContext();
  try {
    const page = await billingCtx.newPage();
    await page.setViewportSize({ width: 1440, height: 960 });
    await signInAdmin(page, cfg.billingAdminEmail, cfg.billingAdminSeed, cfg.adminTime);
    adminEvidence.billingOperator = await verifyBillingOperatorWithStaleCommercialRace(page);
  } finally {
    await billingCtx.close();
  }

  const supportCtx = await browser.newContext();
  try {
    const page = await supportCtx.newPage();
    await page.setViewportSize({ width: 1440, height: 960 });
    await signInAdmin(page, cfg.supportAdminEmail, cfg.supportAdminSeed, cfg.adminTime);
    await page.goto(`${adminWeb}/accounts`);
    const supportReply = page.waitForResponse(
      (r) => r.url().includes(`/api/admin/accounts/${cfg.accountA}/notifications`) && r.request().method() === 'GET',
    );
    await page.getByLabel('Billing account ID').fill(cfg.accountA);
    await page.getByRole('button', { name: 'Read access status' }).click();
    const supportBody = await (await supportReply).text();
    expect(supportBody).not.toContain('12000');
    expect(supportBody).not.toContain('destinationHint');
    const panel = page.getByTestId('provider-notifications-panel');
    await expect(panel.getByTestId('provider-notifications-redacted')).toBeVisible();
    await expect(panel.getByTestId('provider-notification-commercial')).toHaveCount(0);
    await page.screenshot({ path: join(evidence, 'admin-support-redacted-preview.png'), fullPage: true });
    adminEvidence.supportOperator = { commercialIncluded: false, redactedBannerVisible: true };
  } finally {
    await supportCtx.close();
  }

  const secCtx = await browser.newContext();
  try {
    const page = await secCtx.newPage();
    await signInAdmin(page, cfg.securityAdminEmail, cfg.securityAdminSeed, cfg.adminTime);
    const directDenied = await page.evaluate(async (accountId) => {
      const res = await fetch(`/api/admin/accounts/${accountId}/notifications`, { credentials: 'include' });
      return res.status;
    }, cfg.accountA);
    expect(directDenied).toBe(403);
    adminEvidence.securityOnlyOperator = { directReadStatus: directDenied };
  } finally {
    await secCtx.close();
  }

  writeFileSync(join(evidence, 'admin-browser-assertions.json'), JSON.stringify(adminEvidence, null, 2));
});
