import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { origin as admin, signIn, type Enrolled } from './p01-12-fixture';
import { account, commerce, evidence, merchant, merchantSession, prepareProvider, storedMerchantToken } from './entitlement-browser-fixture';

const providerCredentials = join(process.env['P0214_SESSION_DIR']!, 'provider-fixture.json');
const sessionState = (role: 'owner' | 'staff') => join(process.env['P0214_SESSION_DIR']!, role + '.json');
const fixture = JSON.parse(readFileSync(join(evidence, 'fixture.json'), 'utf8')) as {
  term: string; end: string; otherTerm: string; otherAccount: string; affectedShops: string[]; distinctShop: string;
};
const merchantPage = merchant + '/billing/accounts/' + account + '/status';
const cancellationPath = '/api/billing/accounts/' + account + '/subscriptions/' + fixture.term + '/cancellation';

function observations(phase: string, data: object) {
  writeFileSync(join(evidence, 'observations-' + phase + '.json'), JSON.stringify(data, null, 2));
}

test('@prepare real MFA and merchant sign-in', async ({ page, browser }) => {
  const credentials = await prepareProvider(page);
  writeFileSync(providerCredentials, JSON.stringify(credentials), { mode: 0o600 });
  for (const role of ['owner', 'staff'] as const) {
    const signed = await browser.newPage();
    try {
      await merchantSession(signed, role);
      await signed.context().storageState({ path: sessionState(role) });
    } finally {
      await signed.close();
    }
  }
});

function merchantPanel(page: Page, term = fixture.term) {
  return page.getByTestId('account-term').filter({ has: page.locator(`[data-term-id="${term}"]`) });
}

async function review(page: Page) {
  const panel = merchantPanel(page);
  const response = page.waitForResponse(r => new URL(r.url()).pathname === cancellationPath && r.request().method() === 'GET');
  await panel.getByTestId('cancellation-review').click();
  const received = await response;
  expect(received.status()).toBe(200);
  expect(received.headers()['cache-control']).toContain('no-store');
  const detail = await received.json();
  expect(Date.parse(detail.effectiveAt)).toBe(Date.parse(fixture.end));
  expect(detail.commercial.pricePaise).toBe(149900);
  const section = panel.getByTestId('cancellation-detail');
  await expect(section).toContainText('no refund or grace period');
  await expect(section.getByTestId('cancellation-basis')).toContainText('1,499');
  const shown = await section.locator('[data-shop-id]').evaluateAll(elements => elements.map(element => ({
    shopId: element.getAttribute('data-shop-id'),
    shopName: element.textContent?.trim(),
  })));
  expect(shown).toEqual(detail.affectedShops);
  expect(shown.map(shop => shop.shopId).sort()).toEqual(fixture.affectedShops.sort());
  return detail;
}

test('@schedule preview, explicit acceptance, storage failure and same-key recovery across reload and tabs', async ({ browser }) => {
  const context = await browser.newContext({ storageState: sessionState('owner'), viewport: { width: 1100, height: 900 } });
  try {
    const page = await context.newPage();
    await page.goto(merchantPage);
    const panel = merchantPanel(page);
    await expect(panel).toContainText('Active');
    await expect(merchantPanel(page, fixture.otherTerm)).toContainText('Active');
    await page.screenshot({ path: join(evidence, 'merchant-before.png'), fullPage: true });
    const preview = await review(page);
    await page.screenshot({ path: join(evidence, 'merchant-preview.png'), fullPage: true });
    await expect(panel.getByTestId('cancellation-confirm')).toBeDisabled();
    const staff = await browser.newPage({ storageState: sessionState('staff') });
    try {
      const recovery = await exerciseRecovery(page, await context.newPage(), staff);
      observations('schedule', { preview, ...recovery });
    } finally {
      await staff.close();
    }
  } finally {
    await context.close();
  }
});

async function exerciseRecovery(page: Page, otherTab: Page, staff: Page) {
  const panel = merchantPanel(page);
  const posts: unknown[] = [];
  let committedStatus = 0;
  await page.route('**' + cancellationPath, async route => {
    if (route.request().method() !== 'POST') return route.continue();
    posts.push(route.request().postDataJSON());
    if (posts.length === 1) return route.abort('connectionreset');
    const actual = await fetch(route.request().url(), {
      method: 'POST',
      headers: { Authorization: (await route.request().allHeaders())['authorization'], 'Content-Type': 'application/json' },
      body: route.request().postData(),
    });
    committedStatus = actual.status;
    await route.abort('connectionreset');
  });
  await page.evaluate(() => {
    const write = Storage.prototype.setItem;
    Storage.prototype.setItem = function (key, value) {
      if (key.startsWith('intelibill.billing.cancellation.v1:')) throw new DOMException('Fixture storage failure', 'QuotaExceededError');
      return write.call(this, key, value);
    };
  });
  await panel.getByTestId('cancellation-accept').check();
  await panel.getByTestId('cancellation-confirm').click();
  await expect(panel.getByTestId('cancellation-error')).toContainText('could not save');
  expect(posts).toHaveLength(0);

  await page.reload();
  await review(page);
  await panel.getByTestId('cancellation-accept').check();
  await panel.getByTestId('cancellation-confirm').click();
  await expect(panel.getByTestId('cancellation-error')).toContainText('outcome is unknown');
  await page.reload();
  const missing = page.waitForResponse(r => new URL(r.url()).pathname.startsWith(cancellationPath + '/operations/'));
  await panel.getByTestId('cancellation-check').click();
  expect((await missing).status()).toBe(404);
  await otherTab.goto(merchantPage);
  await expect(merchantPanel(otherTab).getByTestId('cancellation-check')).toBeVisible();
  await assertRecoveryScope(page, otherTab, staff);
  await review(otherTab);
  await expect(merchantPanel(otherTab).getByTestId('cancellation-confirm')).toHaveCount(0);

  await panel.getByTestId('cancellation-retry').click();
  await expect.poll(() => committedStatus).toBe(200);
  await expect(panel.getByTestId('cancellation-error')).toContainText('outcome is unknown');
  expect(posts).toHaveLength(2);
  expect(posts[0]).toEqual(posts[1]);
  await page.reload();
  await panel.getByTestId('cancellation-check').click();
  await expect(panel.getByTestId('cancellation-saved')).toContainText('scheduled for');
  await expect(panel.getByTestId('cancellation-check')).toHaveCount(0);
  await expect(merchantPanel(otherTab).getByTestId('cancellation-check')).toHaveCount(0);
  return { initialLookup: 404, retryCommitted: committedStatus, identicalCommand: true, sameOriginSecondTab: true,
    storagePreventedHttp: true, receiptRecoveredAfterReload: true, otherAccountShowsNoDraft: true,
    otherActorLookup: 403 };
}

async function assertRecoveryScope(owner: Page, otherTab: Page, staff: Page) {
  await otherTab.goto(merchant + '/billing/accounts/' + fixture.otherAccount + '/status');
  await expect(otherTab.getByText('This billing account has no subscription terms yet.')).toBeVisible();
  await expect(otherTab.getByTestId('cancellation-check')).toHaveCount(0);
  await otherTab.goto(merchantPage);
  await expect(merchantPanel(otherTab).getByTestId('cancellation-check')).toBeVisible();

  const operation = await owner.evaluate(() => {
    const key = Object.keys(localStorage).find(item => item.startsWith('intelibill.billing.cancellation.v1:'))!;
    return JSON.parse(localStorage.getItem(key)!).command.operationKey as string;
  });
  await staff.goto(merchant + '/billing/status');
  const staffToken = await storedMerchantToken(staff);
  const denied = await staff.request.get(merchant + cancellationPath + '/operations/' + operation,
    { headers: { Authorization: `Bearer ${staffToken}` } });
  expect(denied.status()).toBe(403);
}

async function readProvider(page: Page, phase: string, expected: { effectiveAt: string; affectedShops: unknown },
  commercial = true) {
  await page.goto(admin + '/accounts');
  await page.getByLabel('Billing account ID').fill(account);
  await page.getByRole('button', { name: 'Read access status' }).click();
  const panel = page.getByRole('region', { name: 'Term ' + fixture.term, exact: true });
  const matching = page.waitForResponse(r => new URL(r.url()).pathname === cancellationPath.replace('/api/billing/', '/api/admin/'));
  await panel.getByTestId('provider-cancellation-review').click();
  const received = await matching;
  expect(received.status()).toBe(200);
  expect(received.headers()['cache-control']).toContain('no-store');
  const detail = await received.json();
  expect(detail.effectiveAt).toBe(expected.effectiveAt);
  expect(detail.affectedShops).toEqual(expected.affectedShops);
  const expectedSync = phase === 'pending' ? 'Saved, waiting for confirmation' : 'Applied';
  await expect(panel.getByTestId('provider-sync-state')).toHaveText([expectedSync, expectedSync]);
  await expect(panel.getByTestId('provider-cancellation-detail')).toContainText('No refund or grace period');
  if (commercial) await expect(panel.getByTestId('provider-price')).toContainText('1,499');
  else {
    expect(detail.commercial).toBeNull();
    await expect(panel.getByTestId('provider-price')).toHaveCount(0);
  }
  await expect(panel.getByRole('heading')).toHaveText(['exact-end', 'support'].includes(phase) ? 'Expired' : 'Active');
  await page.screenshot({ path: join(evidence, `provider-${phase}.png`), fullPage: true });
  return detail;
}

for (const phase of ['pending', 'applied', 'before-end', 'exact-end']) {
  test(`@${phase} both builds show the same end and shop scope`, async ({ browser }) => {
    const merchantView = await browser.newPage({ storageState: sessionState('owner'), viewport: { width: 390, height: 844 } });
    const provider = await browser.newPage({ storageState: process.env['P0213_PROVIDER_STATE']!, viewport: { width: 1280, height: 900 } });
    const staff = await browser.newPage({ storageState: sessionState('staff') });
    try {
      await merchantView.goto(merchantPage);
      const panel = merchantPanel(merchantView);
      await expect(panel.locator('.state')).toHaveText(phase === 'exact-end' ? 'Expired' : 'Active');
      await expect(merchantPanel(merchantView, fixture.otherTerm).locator('.state'))
        .toHaveText(phase === 'exact-end' ? /grace/i : 'Active');
      const detail = await review(merchantView);
      const sync = phase === 'pending' ? /waiting|pending/i : /applied/i;
      await expect(panel.getByTestId('sync-state')).toHaveText([sync, sync]);
      await merchantView.screenshot({ path: join(evidence, `merchant-${phase}.png`), fullPage: true });
      const providerDetail = await readProvider(provider, phase, detail);
      const merchantData = await provider.request.get(merchant + '/api/inventory/batches');
      expect(merchantData.status()).toBe(401);
      await staff.goto(merchant + '/billing/status');
      const staffToken = await storedMerchantToken(staff);
      const denied = await staff.request.get(merchant + cancellationPath, { headers: { Authorization: `Bearer ${staffToken}` } });
      expect(denied.status()).toBe(403);
      await expect(staff.getByTestId('commercial-price')).toHaveCount(0);
      const stock = await commerce(browser, staff, staffToken, phase, sessionState('owner'));
      observations(phase, { merchant: detail, provider: providerDetail, stock, staffBillingDenied: denied.status(),
        providerMerchantDataDenied: merchantData.status(), activeBeforeExactEnd: phase !== 'exact-end', distinctTerm: fixture.otherTerm });
    } finally {
      await Promise.all([merchantView.close(), provider.close(), staff.close()]);
    }
  });
}

test('@support redacts the paid amount while retaining the frozen scope', async ({ browser }) => {
  const page = await browser.newPage({ storageState: process.env['P0213_PROVIDER_STATE']! });
  try {
    const actor = JSON.parse(readFileSync(providerCredentials, 'utf8')) as Enrolled;
    await page.goto(admin + '/');
    await page.getByRole('link', { name: 'Manage administrator access' }).click();
    const users = await (await page.request.get(admin + '/api/admin/users')).json();
    const self = users.items.find((row: { email: string }) => row.email.toLowerCase() === actor.email.toLowerCase());
    await page.getByLabel('Administrator', { exact: true }).selectOption(self.id);
    await page.getByRole('checkbox', { name: 'Licensing Manager', exact: true }).uncheck();
    await page.getByRole('checkbox', { name: 'Support', exact: true }).check();
    await page.getByLabel('Reason', { exact: true }).selectOption('responsibilities-changed');
    await page.getByLabel('I confirm these access changes.').check();
    await page.getByRole('button', { name: 'Save administrator access' }).click();
    await expect.poll(async () => (await page.request.get(admin + '/api/admin/auth/session')).status()).toBe(401);
    await signIn(page, actor);

    const { provider } = JSON.parse(readFileSync(join(evidence, 'observations-exact-end.json'), 'utf8'));
    const redacted = await readProvider(page, 'support', provider, false);
    observations('support', { commercialPricePaise: provider.commercial.pricePaise, supportCommercial: redacted.commercial,
      sameFrozenEnd: true, sameAffectedShops: true, supersededSessionStatus: 401 });
  } finally {
    await page.close();
  }
});

test('@outage clears the merchant view', async ({ browser }) => {
  const page = await browser.newPage({ storageState: sessionState('owner') });
  try {
    await page.goto(merchantPage);
    await expect(page.getByTestId('verification-unavailable')).toBeVisible();
    await expect(page.getByTestId('cancellation-saved')).toHaveCount(0);
    observations('outage', { expiredDataShown: false, verificationUnavailable: true });
  } finally {
    await page.close();
  }
});
