import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { expect, test, type Browser, type BrowserContext, type Page, type Response } from '@playwright/test';

function setting(name: string): string {
  const value = process.env['P0203_' + name];
  if (!value) throw new Error('Missing local fixture setting ' + name);
  return value;
}

function loopback(name: string): string {
  const url = new URL(setting(name));
  if (url.protocol !== 'http:' || url.hostname !== '127.0.0.1') throw new Error('Loopback fixture required.');
  return url.origin;
}

async function signIn(page: Page, actor: string) {
  await page.goto(loopback('WEB') + '/login');
  await page.locator('#identifier').fill(setting(actor));
  await page.locator('#password').fill(setting('PASSWORD'));
  const response = await respond(page, '/api/auth/login', () => page.locator('button[type=submit]').click());
  expect(response.status()).toBe(200);
  return (await response.json()).accessToken as string;
}

async function respond(page: Page, path: string, action: () => Promise<unknown>, method = 'POST'): Promise<Response> {
  const reply = page.waitForResponse(r => new URL(r.url()).pathname === path && r.request().method() === method);
  await action();
  return reply;
}

async function api(page: Page, token: string, path: string, body?: unknown) {
  return page.evaluate(async ({ token, path, body }) => {
    const response = await fetch(path, {
      headers: { Authorization: 'Bearer ' + token, ...(body ? { 'Content-Type': 'application/json' } : {}) },
      method: body ? 'POST' : 'GET',
      body: body ? JSON.stringify(body) : undefined,
    });
    return { status: response.status, text: await response.text(), cache: response.headers.get('cache-control') };
  }, { token, path, body });
}

function isDelegation(header: string | undefined) {
  try {
    const value = header?.replace(/^Bearer /, '').split('.')[0] ?? '';
    return JSON.parse(Buffer.from(value, 'base64url').toString()).typ === 'intelibill-merchant-billing-delegation+jwt';
  } catch {
    return false;
  }
}

type Observe = (context: BrowserContext) => void;

function watchBoundary(licensing: string) {
  let leaked = false;
  const observe = (context: BrowserContext) => {
    context.on('request', request => {
      const headers = request.headers();
      const url = new URL(request.url());
      leaked ||= url.origin === licensing || url.pathname.startsWith('/internal/licensing/') ||
        'x-billing-request-id' in headers || isDelegation(headers['authorization']);
      if (url.pathname.endsWith('/accept')) leaked ||= 'token' in (request.postDataJSON() ?? {});
    });
    context.on('response', response => {
      leaked ||= 'x-billing-request-id' in response.headers();
    });
  };
  return { observe, leaked: () => leaked };
}

async function ownerIssues(browser: Browser, observe: Observe) {
  const owner = await browser.newContext({ viewport: { width: 1366, height: 900 } });
  observe(owner);
  const ownerPage = await owner.newPage();
  await signIn(ownerPage, 'OWNER');
  await expect(ownerPage.getByRole('heading', { name: /Billing browser shop/ })).toBeVisible();
  const profile = ownerPage.locator('.sidebar-profile');
  const billingLink = profile.getByText('Billing accounts', { exact: true });
  await expect(async () => {
    if (!await billingLink.isVisible()) await profile.locator('.shell-panelmenu-header-link').first().click();
    await billingLink.click({ timeout: 700 });
  }).toPass({ timeout: 10_000 });
  await expect(ownerPage).toHaveURL(/\/billing$/);
  await ownerPage.getByTestId('billing-account-name').fill('Local Manager Demo');
  const created = await respond(ownerPage, '/api/billing/accounts', () => ownerPage.getByTestId('billing-account-create').click());
  expect(created.status()).toBe(201);
  const accountId = (await created.json()).accountId as string;
  const account = `/api/billing/accounts/${accountId}`;
  await ownerPage.getByTestId('billing-members-link').click();
  await expect(ownerPage.getByTestId('billing-member-row')).toHaveCount(1);
  await ownerPage.getByTestId('billing-invite-recipient').fill(setting('RECIPIENT'));
  const issued = await respond(ownerPage, account + '/invitations', () => ownerPage.getByTestId('billing-invite-submit').click());
  expect(issued.status()).toBe(202);
  expect(issued.headers()['cache-control']).toContain('no-store');
  await expect(ownerPage.getByTestId('billing-invite-notice')).toBeVisible();
  await expect(ownerPage.getByTestId('billing-invite-recipient')).toHaveValue('');

  return { ownerPage, account, issued: issued.status() };
}

async function loadRecipient(browser: Browser, observe: Observe) {
  const web = loopback('WEB');
  const recipient = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
  observe(recipient);
  const page = await recipient.newPage();
  const token = await signIn(page, 'RECIPIENT');
  const before = await api(page, token, '/api/shops/me');
  expect(before.status).toBe(200);
  await page.goto(web + '/billing');
  await page.getByTestId('billing-inbox-link').click();
  await expect(page.getByTestId('billing-invitation-row')).toHaveCount(1);
  await expect(page.getByTestId('billing-invitation-row')).toContainText('Local Manager Demo');
  const inbox = await api(page, token, '/api/billing/invitations');
  expect(inbox.cache).toContain('no-store');
  const invitation = JSON.parse(inbox.text).invitations[0] as { invitationId: string; revision: number };
  expect('token' in invitation).toBe(false);
  const acceptPath = `/api/billing/invitations/${invitation.invitationId}/accept`;
  const consent = { expectedInvitationRevision: invitation.revision };

  return { page, token, shopsBefore: before.text, acceptPath, consent };
}

async function denyStranger(browser: Browser, observe: Observe, acceptPath: string, consent: unknown) {
  const stranger = await browser.newContext();
  observe(stranger);
  const other = await stranger.newPage();
  const otherToken = await signIn(other, 'STRANGER');
  await other.goto(loopback('WEB') + '/billing/invitations');
  await expect(other.getByTestId('billing-inbox-empty')).toBeVisible();
  const wrongActor = await api(other, otherToken, acceptPath, consent);
  expect(wrongActor.status).toBe(404);

  return wrongActor.status;
}

async function acceptAndReload(page: Page, acceptPath: string) {
  // Lose the actual success response. Reload must show the committed membership and consumed invitation.
  let accept = 0;
  await page.route('**' + acceptPath, async route => {
    const server = await route.fetch();
    accept = server.status();
    expect(server.headers()['cache-control']).toContain('no-store');
    expect('token' in await server.json()).toBe(false);
    await route.abort('failed');
  }, { times: 1 });
  await page.getByTestId('billing-invitation-accept').click();
  await expect(page.getByTestId('billing-inbox-empty')).toBeVisible();
  expect(accept).toBe(200);
  await page.reload();
  await expect(page.getByTestId('billing-inbox-empty')).toBeVisible();
  await page.locator('app-billing-inbox').getByRole('link', { name: 'Billing accounts', exact: true }).click();
  await expect(page.getByTestId('billing-account-row')).toContainText('Manager');
  await page.getByTestId('billing-members-link').click();
  await expect(page.getByTestId('billing-member-row')).toHaveCount(2);
  await expect(page.getByTestId('billing-member-row').filter({ hasText: 'You' })).toContainText('Manager');
  await expect(page.getByTestId('billing-invite-submit')).toHaveCount(0);

  return accept;
}

async function checkManagerAuthority(page: Page, token: string, account: string, acceptPath: string,
  consent: unknown, shopsBefore: string) {
  const replay = await api(page, token, acceptPath, consent);
  expect(replay.status).toBe(404);
  const denied = await api(page, token, account + '/invitations', {
    operationKey: randomUUID(), expectedOwnerRevision: 1, recipientKind: 'email', recipient: setting('STRANGER'),
  });
  expect(denied.status).toBe(403);
  expect((await api(page, token, account + '/transfer', { revision: 1 })).status).toBe(404);
  const after = await api(page, token, '/api/shops/me');
  expect(after.status === 200 && shopsBefore === after.text).toBe(true);
  const foreignShop = setting('OWNER_SHOP');
  const foreign = await api(page, token, '/api/shops/' + foreignShop);
  const switchShop = await api(page, token, '/api/shops/switch', { shopId: foreignShop });
  expect(foreign.status).not.toBe(200);
  expect(switchShop.status).not.toBe(200);
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(overflow).toBeLessThanOrEqual(1);
  return { replay: replay.status, managerRedelegation: denied.status, shopListUnchanged: true,
    foreignShop: foreign.status, foreignShopSwitch: switchShop.status, phoneViewportOverflowPx: overflow };
}

test('verified recipient consents once and can only manage commercial billing', async ({ browser }) => {
  const watch = watchBoundary(loopback('LICENSING'));
  const { ownerPage, account, issued } = await ownerIssues(browser, watch.observe);
  const { page, token, shopsBefore, acceptPath, consent } = await loadRecipient(browser, watch.observe);
  const wrongActor = await denyStranger(browser, watch.observe, acceptPath, consent);
  const accept = await acceptAndReload(page, acceptPath);
  const authority = await checkManagerAuthority(page, token, account, acceptPath, consent, shopsBefore);
  await ownerPage.reload();
  await expect(ownerPage.getByTestId('billing-member-row').filter({ hasText: 'Manager' })).toHaveCount(1);
  expect(watch.leaked()).toBe(false);

  writeFileSync(join(setting('EVIDENCE'), 'observations.json'), JSON.stringify({
    issue: issued, wrongActor, accept, ...authority, ownerRosterManagerCount: 1, recipientRosterRole: 'Manager',
    lostAcceptanceResponseRecovered: true, browserSawProofOrDelegation: watch.leaked(),
  }, null, 2), { mode: 0o600 });
});
