import { existsSync, readFileSync, writeFileSync } from 'node:fs';
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

async function waitForFile(path: string, timeoutMs = 15_000) {
  const started = Date.now();
  while (!existsSync(path)) {
    if (Date.now() - started > timeoutMs) {
      throw new Error(`Timed out waiting for ${path}`);
    }
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
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

test('MerchantNoticesFollowLiveShopAndBillingAuthority', async ({ browser }) => {
  const merchantEvidence: Record<string, unknown> = {};

  // 1. Ordinary Shop Staff (active in North Shop and South Shop, no billing role).
  const staffSession = await signInMerchant(browser, cfg.staffEmail);
  try {
    const page = staffSession.page;
    await page.setViewportSize({ width: 1280, height: 860 });

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
    const domText = await page.locator('body').innerText();
    expect(domText).not.toContain('120.00');
    expect(domText).not.toContain(cfg.accountA);
    expect(domText).not.toContain(cfg.accountB);

    const ariaText = await page.getByTestId('shop-license-notices').ariaSnapshot();
    expect(ariaText).toContain('North Shop');
    expect(ariaText).not.toContain('120');
    await page.screenshot({ path: join(evidence, 'merchant-staff-operational-notice.png'), fullPage: true });

    // Direct denied account commercial notice read for Staff.
    const staffDeniedAccount = await merchantAuthedFetch(page, `/api/billing/accounts/${cfg.accountA}/notices`);
    expect(staffDeniedAccount.status).toBe(403);
    expect(staffDeniedAccount.text).not.toContain('12000');

    // Switch shops North -> South -> North and test stale in-flight response rejection.
    let releaseStaleNorth: (() => void) | null = null;
    const staleNorthGate = new Promise<void>((resolve) => {
      releaseStaleNorth = resolve;
    });
    let delayedOnce = false;
    await page.route(`**/api/licensing/shops/${cfg.northShopId}/notices**`, async (route) => {
      if (!delayedOnce) {
        delayedOnce = true;
        const upstream = await route.fetch();
        const body = await upstream.text();
        await staleNorthGate;
        await route.fulfill({ response: upstream, body });
        return;
      }
      await route.continue();
    });

    // Reload triggers a delayed North request, then immediately switch to South before North resolves.
    await page.reload();
    await page.locator('.active-shop-trigger-desktop').click();
    await page.getByRole('menuitem', { name: /South Shop/ }).click();
    await expect(page.getByTestId('shop-license-notice')).toContainText('South Shop');
    releaseStaleNorth!();
    await page.waitForTimeout(300);
    await expect(page.getByTestId('shop-license-notice')).toContainText('South Shop');
    await expect(page.getByTestId('shop-license-notice')).not.toContainText('North Shop');
    await page.unroute(`**/api/licensing/shops/${cfg.northShopId}/notices**`);

    // Switch back to North Shop (A -> B -> A) and reload to prove fresh non-cached API read.
    await page.locator('.active-shop-trigger-desktop').click();
    await page.getByRole('menuitem', { name: /North Shop/ }).click();
    await expect(page.getByTestId('shop-license-notice')).toContainText('North Shop');
    await page.reload();
    await expect(page.getByTestId('shop-license-notice')).toContainText('North Shop');

    // Log out clears operational notice immediately.
    await page.locator('.sidebar-profile .p-panelmenu-header-link').first().click();
    await page.locator('.sidebar-profile').getByText('Logout', { exact: true }).click();
    await expect(page).toHaveURL(/\/login/);
    await expect(page.getByTestId('shop-license-notice')).toHaveCount(0);

    merchantEvidence.staff = {
      status: noticeReply.status(),
      cacheControl: noticeReply.headers()['cache-control'],
      noticeCount: staffJson.notices.length,
      accountReadStatus: staffDeniedAccount.status,
      forbiddenFieldsAbsent: true,
    };
  } finally {
    await staffSession.context.close();
  }

  // 2. Non-billing Shop Owner (South Shop owner, no Account A billing role).
  const shopOwnerSession = await signInMerchant(browser, cfg.southOwnerEmail);
  try {
    const page = shopOwnerSession.page;
    await page.goto(`${merchantWeb}/`);
    const banner = page.getByTestId('shop-license-notice');
    await expect(banner).toBeVisible();
    await expect(banner).toContainText('South Shop');
    await expect(banner).not.toContainText('North Shop');
    await expect(banner).not.toContainText('120');

    const deniedAccount = (await merchantAuthedFetch(page, `/api/billing/accounts/${cfg.accountA}/notices`)).status;
    expect(deniedAccount).toBe(403);
    merchantEvidence.nonBillingShopOwner = { shopBannerVisible: true, accountReadStatus: deniedAccount };
  } finally {
    await shopOwnerSession.context.close();
  }

  // 3. Billing-only Manager on Account A (zero shop membership).
  const managerSession = await signInMerchant(browser, cfg.billingManagerEmail);
  try {
    const page = managerSession.page;
    await page.setViewportSize({ width: 1280, height: 860 });
    await page.goto(`${merchantWeb}/billing/accounts/${cfg.accountA}/status`);
    const accountNotice = page.getByTestId('account-license-notice');
    await expect(accountNotice).toBeVisible();
    await expect(page.getByTestId('account-notice-price')).toContainText('120');
    await expect(page.getByTestId('account-notice-quote-note')).toBeVisible();
    await expect(accountNotice).toContainText('North Shop');
    await expect(accountNotice).toContainText('South Shop');
    await page.screenshot({ path: join(evidence, 'merchant-billing-manager-commercial-notice.png'), fullPage: true });

    const deniedShop = (await merchantAuthedFetch(page, `/api/licensing/shops/${cfg.northShopId}/notices`)).status;
    expect(deniedShop).toBe(403);

    const deniedAccountB = (await merchantAuthedFetch(page, `/api/billing/accounts/${cfg.accountB}/notices`)).status;
    expect(deniedAccountB).toBe(403);

    merchantEvidence.billingOnlyManager = {
      commercialVisible: true,
      shopReadStatus: deniedShop,
      accountBReadStatus: deniedAccountB,
    };
  } finally {
    await managerSession.context.close();
  }

  // 4. Verified opted-in contact user has zero account/shop read rights (uses real login JWT from setup).
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

test('ProviderPreviewUsesLiveCapabilityAndShowsScheduledState', async ({ browser }) => {
  const adminEvidence: Record<string, unknown> = {};

  // 1. Billing operator: commercial preview, UTC+IST, keyboard focus, localization, narrow 390px, mid-session revocation.
  const billingCtx = await browser.newContext();
  try {
    const page = await billingCtx.newPage();
    await page.setViewportSize({ width: 1440, height: 960 });
    await signInAdmin(page, cfg.billingAdminEmail, cfg.billingAdminSeed, cfg.adminTime);

    await page.goto(`${adminWeb}/accounts`);
    const previewReplyPromise = page.waitForResponse(
      (r) => r.url().includes(`/api/admin/accounts/${cfg.accountA}/notifications`) && r.request().method() === 'GET',
    );
    await page.getByLabel('Billing account ID').fill(cfg.accountA);
    await page.getByRole('button', { name: 'Read access status' }).click();
    const previewReply = await previewReplyPromise;
    expect(previewReply.status()).toBe(200);
    expect(previewReply.headers()['cache-control'] ?? '').toContain('no-store');
    const previewJson = await previewReply.json();
    expect(previewJson.commercialIncluded).toBe(true);

    const panel = page.getByTestId('provider-notifications-panel');
    await expect(panel).toBeVisible();
    await expect(panel.getByTestId('provider-notification-row').first()).toBeVisible();
    await expect(panel.getByTestId('provider-notification-due-utc').first()).toContainText('22 Feb 2028, 04:30:00 UTC');
    await expect(panel.getByTestId('provider-notification-due-ist').first()).toContainText('22 Feb 2028, 10:00:00 IST');
    await expect(panel.getByTestId('provider-notification-price').first()).toContainText('120');

    // Verify both Due and Scheduled (future) badges are present and job IDs match persisted DB jobs.
    const renderedIds = await panel
      .getByTestId('provider-notification-row')
      .evaluateAll((nodes) => nodes.map((n) => n.getAttribute('data-job-id')));
    for (const id of renderedIds) {
      expect(cfg.expectedJobIds).toContain(id);
    }
    const badges = await panel.getByTestId('provider-notification-due-badge').allTextContents();
    expect(badges.some((text) => text.trim() === 'Due')).toBe(true);
    expect(badges.some((text) => text.trim() === 'Scheduled (future)')).toBe(true);

    // Keyboard focus & localization check.
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

    // Refresh preview (confirmed non-writing in C# post-check).
    const refreshReply = page.waitForResponse(
      (r) => r.url().includes(`/api/admin/accounts/${cfg.accountA}/notifications`) && r.request().method() === 'GET',
    );
    await panel.getByTestId('provider-notifications-refresh').click();
    expect((await refreshReply).status()).toBe(200);

    // Narrow 390px viewport check (zero horizontal overflow).
    await page.setViewportSize({ width: 390, height: 844 });
    const scrollWidth = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(scrollWidth).toBeLessThanOrEqual(390);
    await page.screenshot({ path: join(evidence, 'admin-billing-390px.png'), fullPage: true });

    // Mid-session capability removal: signal C# host, wait for confirmation, refresh -> stale rows cleared + 403.
    writeFileSync(join(scratch, 'revoke-billing-capability'), 'revoke');
    await waitForFile(join(scratch, 'billing-revoked'));
    const revokedReply = page.waitForResponse(
      (r) => r.url().includes(`/api/admin/accounts/${cfg.accountA}/notifications`) && r.request().method() === 'GET',
    );
    await panel.getByTestId('provider-notifications-refresh').click();
    expect([401, 403]).toContain((await revokedReply).status());
    await expect(panel.getByTestId('provider-notification-row')).toHaveCount(0);

    adminEvidence.billingOperator = {
      commercialIncluded: true,
      renderedJobCount: renderedIds.length,
      narrowScrollWidth: scrollWidth,
      revokedClearedStaleRows: true,
    };
  } finally {
    await billingCtx.close();
  }

  // 2. Support operator: redacted operational preview only.
  const supportCtx = await browser.newContext();
  try {
    const page = await supportCtx.newPage();
    await page.setViewportSize({ width: 1440, height: 960 });
    await signInAdmin(page, cfg.supportAdminEmail, cfg.supportAdminSeed, cfg.adminTime);

    await page.goto(`${adminWeb}/accounts`);
    const supportReplyPromise = page.waitForResponse(
      (r) => r.url().includes(`/api/admin/accounts/${cfg.accountA}/notifications`) && r.request().method() === 'GET',
    );
    await page.getByLabel('Billing account ID').fill(cfg.accountA);
    await page.getByRole('button', { name: 'Read access status' }).click();
    const supportReply = await supportReplyPromise;
    expect(supportReply.status()).toBe(200);
    const supportBody = await supportReply.text();
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

  // 3. Security-only operator: direct API request denied with 403.
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
