import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type BrowserContext, type Page } from '@playwright/test';

function setting(name: string): string {
  const value = process.env['P0203B_' + name];
  if (!value) throw new Error('Missing local fixture setting ' + name);
  return value;
}

function loopback(name: string): string {
  const url = new URL(setting(name));
  if (url.protocol !== 'http:' || url.hostname !== '127.0.0.1') throw new Error('Loopback fixture required.');
  return url.origin;
}

function reply(page: Page, path: string, method = 'GET') {
  return page.waitForResponse(r => new URL(r.url()).pathname === path && r.request().method() === method);
}

async function signIn(page: Page, actor: string) {
  await page.goto(loopback('WEB') + '/login');
  await page.locator('#identifier').fill(setting(actor));
  await page.locator('#password').fill(setting('PASSWORD'));
  const response = reply(page, '/api/auth/login', 'POST');
  await page.locator('button[type=submit]').click();
  expect((await response).status()).toBe(200);
}

function watchBoundary(contexts: BrowserContext[]) {
  let leaked = false;
  for (const context of contexts) {
    context.on('request', request => {
      const url = new URL(request.url());
      leaked ||= url.origin === loopback('LICENSING') || url.pathname.startsWith('/internal/licensing/') ||
        'x-billing-request-id' in request.headers();
    });
    context.on('response', response => { leaked ||= 'x-billing-request-id' in response.headers(); });
  }
  return () => leaked;
}

async function removeThroughRoster(owner: Page, members: string, removal: string) {
  await owner.goto(loopback('WEB') + members);
  await expect(owner.getByTestId('billing-member-row')).toHaveCount(2);
  let requests = 0;
  owner.on('request', request => { if (new URL(request.url()).pathname === removal) requests++; });

  await owner.getByTestId('billing-remove-manager').click();
  await expect(owner.getByRole('dialog')).toBeVisible();
  await owner.getByTestId('billing-remove-cancel').click();
  await expect(owner.getByRole('dialog')).toBeHidden();
  const cancelRequests = requests;
  expect(cancelRequests).toBe(0);

  let committed = 0;
  let original: unknown;
  await owner.route('**' + removal, async route => {
    original = route.request().postDataJSON();
    const server = await route.fetch();
    committed = server.status();
    expect(server.headers()['cache-control']).toContain('no-store');
    await route.abort('failed');
  }, { times: 1 });
  await owner.getByTestId('billing-remove-manager').click();
  const dialog = owner.getByRole('dialog');
  await expect(dialog).toContainText('Removal fixture account');
  await expect(dialog).toContainText('Their shop access stays the same.');
  await dialog.screenshot({ path: join(setting('EVIDENCE'), 'owner-confirmation.png') });
  await dialog.locator('textarea').fill('Delegation ended');
  await owner.getByTestId('billing-remove-confirm').click();
  await expect(owner.getByTestId('billing-remove-retry')).toBeEnabled();
  expect(committed).toBe(200);

  await owner.reload();
  const replay = reply(owner, removal, 'POST');
  await owner.getByTestId('billing-remove-retry').click();
  const response = await replay;
  expect(response.status()).toBe(200);
  expect(response.request().postDataJSON()).toEqual(original);
  await expect(owner.getByTestId('billing-member-row')).toHaveCount(1);
  await expect(owner.getByTestId('billing-member-row')).toContainText('Owner');
  return { cancelRequests, committed, replay: response.status(), ownerCount: 1 };
}

async function preparePendingCoverage(page: Page, path: string) {
  await page.locator('#affiliation-shop').fill(setting('SHOP'));
  await page.locator('#affiliation-term').selectOption('none');
  await page.route('**' + path, async route => {
    if (route.request().method() === 'POST') await route.abort('failed');
    else await route.fallback();
  }, { times: 1 });
  await page.locator('app-account-coverage button[type="submit"]').click();
  await expect(page.getByRole('button', { name: /Retry/ })).toBeEnabled();
}

async function denyCoverage(page: Page, path: string, remove: () => Promise<unknown>) {
  let release = () => {};
  const held = new Promise<void>(resolve => { release = resolve; });
  let delivered = () => {};
  const finished = new Promise<void>(resolve => { delivered = resolve; });
  let fetched = () => {};
  const ready = new Promise<void>(resolve => { fetched = resolve; });
  await page.route('**' + path, async route => {
    const before = await route.fetch();
    expect(before.status()).toBe(200);
    fetched();
    await held;
    try {
      await route.fulfill({ response: before });
    } finally {
      delivered();
    }
  }, { times: 1 });
  const refresh = page.locator('app-account-coverage button').last();
  await refresh.click();
  await ready;
  try {
    const ownerEvidence = await remove();
    const denied = reply(page, path);
    await refresh.click();
    const response = await denied;
    expect(response.status()).toBe(403);
    expect(response.headers()['cache-control']).toContain('no-store');
    await expect(page.locator('app-account-coverage [role="alert"]')).toBeVisible();
    return { affiliationDenied: response.status(), ownerEvidence };
  } finally {
    release();
    await finished;
  }
}

test('live manager loses protected account views while their shop and other account remain available', async ({ browser }) => {
  const owner = await browser.newContext({ viewport: { width: 1366, height: 900 }, serviceWorkers: 'block' });
  const manager = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true,
    hasTouch: true, serviceWorkers: 'block' });
  const leaked = watchBoundary([owner, manager]);
  const ownerPage = await owner.newPage();
  const coveragePage = await manager.newPage();
  const account = setting('ACCOUNT');
  const roster = '/billing/accounts/' + account + '/members';
  const coverage = '/billing/accounts/' + account + '/coverage';
  const route = '/api/billing/accounts/' + account;
  const affiliations = route + '/affiliations';

  await signIn(ownerPage, 'OWNER');
  await signIn(coveragePage, 'MANAGER');
  await coveragePage.goto(loopback('WEB') + coverage);
  await preparePendingCoverage(coveragePage, affiliations);
  const rosterPage = await manager.newPage();
  await rosterPage.goto(loopback('WEB') + roster);
  await expect(rosterPage.getByTestId('billing-member-row')).toHaveCount(2);
  await expect(rosterPage.getByTestId('billing-remove-manager')).toHaveCount(0);

  const outcome = await denyCoverage(coveragePage, affiliations,
    () => removeThroughRoster(ownerPage, roster, route + '/members/' + setting('MEMBER') + '/remove'));
  await expect(coveragePage.getByRole('button', { name: /Retry/ })).toHaveCount(0);
  await expect(coveragePage.locator('#affiliation-shop')).toHaveCount(0);
  const coverageAlert = coveragePage.locator('app-account-coverage [role="alert"]');
  await expect(coverageAlert).toHaveText('You do not have access to this billing account.');
  await coverageAlert.screenshot({ path: join(setting('EVIDENCE'), 'manager-denied.png') });
  const pendingPreserved = await coveragePage.evaluate(accountId =>
    Object.keys(localStorage).some(key => key.startsWith('intelibill.billing.affiliation.v1:') && key.includes(accountId)), account);
  expect(pendingPreserved).toBe(true);
  const denied = reply(rosterPage, route + '/members');
  await rosterPage.evaluate(() => window.dispatchEvent(new Event('focus')));
  expect((await denied).status()).toBe(403);
  await expect(rosterPage.getByTestId('billing-member-row')).toHaveCount(0);

  await rosterPage.locator('app-billing-members').getByRole('link', { name: 'Billing accounts' }).click();
  await expect(rosterPage.getByTestId('billing-account-row')).toHaveCount(1);
  await expect(rosterPage.getByTestId('billing-members-link')).toHaveAttribute('href',
    '/billing/accounts/' + setting('OTHER') + '/members');
  await rosterPage.getByTestId('billing-members-link').click();
  await expect(rosterPage.getByTestId('billing-member-row')).toContainText('Owner');
  await rosterPage.goto(loopback('WEB') + '/');
  await expect(rosterPage.getByRole('heading', { name: /Removal fixture shop/ })).toBeVisible();
  expect(leaked()).toBe(false);

  writeFileSync(join(setting('EVIDENCE'), 'observations.json'), JSON.stringify({
    ...outcome.ownerEvidence as object, affiliationDenied: outcome.affiliationDenied, rosterDenied: 403,
    pendingControlsCleared: true, pendingPreserved, oldResponseReleased: true, otherAccountAccessible: true,
    shopAccessible: true, browserSawInternalTraffic: leaked(),
  }, null, 2), { mode: 0o600 });
});
