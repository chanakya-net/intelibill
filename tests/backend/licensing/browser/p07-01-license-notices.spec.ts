import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page } from '@playwright/test';
import {
  adminWeb, cfg, createDelayedRouteGate, evidence, inspectBilling, inspectShop,
  merchantAuthedFetch, merchantWeb, settleFrames, signalHost, signInAdmin, signInMerchant,
} from './p07-01-notice-fixture';

import { verifyBillingOperatorWithStaleCommercialRace } from './p07-01-provider-preview';

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
  const gate = await createDelayedRouteGate(page, `**/api/licensing/shops/${cfg.northShopId}/notices**`, inspectShop);
  const shopsLoaded = page.waitForResponse((r) => r.url().endsWith('/api/shops/me') && r.status() === 200);
  await page.reload();
  const [held] = await Promise.all([gate.intercepted, shopsLoaded]);
  expect(gate.hits()).toBe(1);
  await switchShop(page, 'South Shop');
  await signalHost('advance-clock.req', 'advance-clock.ack', '2028-02-29T04:30:00Z');
  const fresh = page.waitForResponse((r) => r.url().endsWith(`/shops/${cfg.northShopId}/notices`) && r.status() === 200);
  await switchShop(page, 'North Shop');
  const current = await (await fresh).json();
  expect(current.shopId).toBe(cfg.northShopId);
  expect(current.notices[0].threshold).toBe('GraceStart');
  const banner = page.getByTestId('shop-license-notice');
  await expect(banner).toContainText('in its grace period');
  gate.release();
  const outcome = await gate.settled;
  await settleFrames(page);
  await expect(banner).toContainText('North Shop');
  await expect(banner).toContainText('in its grace period');
  await expect(banner).not.toContainText('ending soon');
  await gate.cleanup();
  await page.locator('.sidebar-profile .p-panelmenu-header-link').first().click();
  await page.locator('.sidebar-profile').getByText('Logout', { exact: true }).click();
  await expect(page).toHaveURL(/\/login/);
  await expect(page.getByTestId('shop-license-notice')).toHaveCount(0);
  await signalHost('advance-clock.req', 'advance-clock.ack', '2028-02-22T04:30:00Z');
  return { held, outcome, currentThreshold: current.notices[0].threshold, finalScope: 'North', finalGuidance: 'GraceActive' };
}

async function switchShop(page: Page, name: string) {
  const shopId = name === 'North Shop' ? cfg.northShopId : cfg.southShopId;
  const read = page.waitForResponse(r => r.url().endsWith(`/shops/${shopId}/notices`));
  const reply = page.waitForResponse((r) => r.url().endsWith('/api/shops/switch') && r.request().method() === 'POST');
  await page.locator('.active-shop-trigger-desktop').click();
  await page.getByRole('menuitem', { name: new RegExp(name) }).click();
  expect((await reply).status()).toBe(200);
  const response = await read;
  expect(response.status()).toBe(200);
  const current = await response.json();
  expect(current.shopId).toBe(shopId);
  expect(current.notices.map((item: { shopName: string }) => item.shopName)).toEqual([name]);
  await expect(page.getByTestId('shop-license-notice')).toContainText(name);
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
  const commercialGate = await createDelayedRouteGate(page, `**/api/billing/accounts/${cfg.accountA}/notices**`, inspectBilling);
  await page.getByTestId('status-retry').click();
  const held = await commercialGate.intercepted;
  expect(commercialGate.hits()).toBe(1);

  await page.locator('a[href="/billing"]').click();
  await expect(page).toHaveURL(`${merchantWeb}/billing`);
  const denied = await merchantAuthedFetch(page, `/api/billing/accounts/${cfg.accountB}/notices`);
  expect(denied.status).toBe(403);
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
  return { held, outcome: commercialOutcome, accountBStatus: denied.status, finalNotices: 0 };
}

async function verifyStaffSession(browser: Browser) {
  const staffSession = await signInMerchant(browser, cfg.staffEmail);
  try {
    await staffSession.page.setViewportSize({ width: 1280, height: 860 });
    const initial = await verifyStaffOperationalBoundaries(staffSession.page);
    const staffDeniedAccount = await merchantAuthedFetch(staffSession.page, `/api/billing/accounts/${cfg.accountA}/notices`);
    expect(staffDeniedAccount.status).toBe(403);
    expect(staffDeniedAccount.text).not.toContain('12000');

    await verifyStaffClockProgression(staffSession.page);
    const staleShopOutcome = await verifyStaffStaleShopSwitchAndLogout(staffSession.page);
    return {
      ...initial,
      accountReadStatus: staffDeniedAccount.status,
      staleShopReplyOutcome: staleShopOutcome,
      coherentThroughGraceExpiry: true,
    };
  } finally {
    await staffSession.context.close();
  }
}

async function verifyOwnerSession(browser: Browser) {
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
    return { shopBannerVisible: true, accountReadStatus: deniedAccount };
  } finally {
    await shopOwnerSession.context.close();
  }
}

async function verifyManagerSession(browser: Browser) {
  const managerSession = await signInMerchant(browser, cfg.billingManagerEmail);
  try {
    const staleCommercialOutcome = await verifyBillingManagerPartialSuspensionStaleReplyAndRenewal(managerSession.page);
    const deniedShop = (await merchantAuthedFetch(managerSession.page, `/api/licensing/shops/${cfg.northShopId}/notices`)).status;
    const deniedAccountB = (await merchantAuthedFetch(managerSession.page, `/api/billing/accounts/${cfg.accountB}/notices`)).status;
    expect(deniedShop).toBe(403);
    expect(deniedAccountB).toBe(403);
    return {
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
}

async function verifyContactSession(browser: Browser) {
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
    return { accountReadStatus: accStatus, shopReadStatus: shopStatus };
  } finally {
    await contactCtx.close();
  }
}

async function verifyRetainedStaffSuccessor(browser: Browser) {
  await signalHost('advance-clock.req', 'advance-clock.ack', '2028-03-24T04:30:00Z');
  const staff = await signInMerchant(browser, cfg.staffEmail);
  try {
    const reply = staff.page.waitForResponse(r => r.url().endsWith(`/shops/${cfg.northShopId}/notices`) && r.status() === 200);
    await staff.page.reload();
    expect((await (await reply).json()).notices[0].threshold).toBe('PaidMinus7Days');
    const banner = staff.page.getByTestId('shop-license-notice');
    await expect(banner).toContainText('North Shop');
    await expect(banner).toContainText('31 Mar 2028');
    return { threshold: 'PaidMinus7Days', visibleThrough: '2028-03-31' };
  } finally {
    await staff.context.close();
  }
}

async function verifyEndInOpenTab(browser: Browser, email: string, shopId: string, end: string, threshold: string, guidance: string) {
  await signalHost('advance-clock.req', 'advance-clock.ack', new Date(Date.parse(end) - 5000).toISOString());
  const trial = await signInMerchant(browser, email);
  try {
    const { page } = trial;
    await page.clock.install();
    const route = `/shops/${shopId}/notices`;
    const before = page.waitForResponse(r => r.url().endsWith(route) && r.status() === 200);
    await page.reload();
    expect((await (await before).json()).notices[0].threshold).toBe(threshold);
    await expect(page.getByTestId('shop-license-notice-guidance')).toContainText(guidance);
    const at = page.waitForResponse(r => r.url().endsWith(route) && r.status() === 200);
    await signalHost('advance-clock.req', 'advance-clock.ack', end);
    await page.clock.fastForward(6000);
    expect((await (await at).json()).notices).toHaveLength(0);
    await expect(page.getByTestId('shop-license-notice')).toHaveCount(0);
    return { timerRefreshed: true, finalNotices: 0 };
  } finally {
    await trial.context.close();
  }
}

test('MerchantNoticesFollowLiveShopAndBillingAuthority', async ({ browser }) => {
  const observations = {
    staff: await verifyStaffSession(browser),
    nonBillingShopOwner: await verifyOwnerSession(browser),
    billingOnlyManager: await verifyManagerSession(browser),
    contactOnly: await verifyContactSession(browser),
    successor: await verifyRetainedStaffSuccessor(browser),
    trialExpiry: await verifyEndInOpenTab(
      browser, cfg.trialOwnerEmail, cfg.trialShopId, '2028-02-15T04:30:00Z', 'TrialMinus1Day', 'trial access is ending soon'),
    paidExpiry: await verifyEndInOpenTab(
      browser, cfg.shortTermEmail, cfg.shortTermShopId, '2028-02-03T04:30:00Z', 'PaidMinus1Day', 'ending soon'),
  };
  writeFileSync(join(evidence, 'merchant-browser-assertions.json'), JSON.stringify(observations, null, 2));
});

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
