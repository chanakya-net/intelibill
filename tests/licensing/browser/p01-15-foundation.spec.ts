import { readFileSync, writeFileSync } from 'node:fs';
import { expect, test, type Browser, type BrowserContext, type Page } from '@playwright/test';
import { fillSecret } from './p01-12-fixture';
import {
  checkAdminStorage, contextProof, enroll, loopback, origin, password, postRefresh, privateCookies,
  readSession, remember, required, scratch, session, SetupUnavailable, signIn, type Account,
} from './p01-15-provider';

const ownerEmail = 'p0115-owner@example.test';
const inviteeEmail = 'p0115-invitee@example.test';
const audit = '/api/admin/audit';
const users = '/api/admin/users';
const phase = process.env['P0115_PHASE'] ?? 'enroll';
const stateFile = scratch + '/provider-state.json';
type SessionCookies = Awaited<ReturnType<BrowserContext['cookies']>>;

interface RestartState {
  operator: Account;
  revoked: SessionCookies;
}

if (phase === 'enroll') {
  test('owner completes bootstrap and MFA on the combined stack', async ({ browser }) => {
    const owner = await browser.newPage({ viewport: { width: 1280, height: 800 } });
    const operator = await browser.newPage({ viewport: { width: 390, height: 844 } });
    let stage = 'bootstrap';
    try {
      const credentials = await bootstrap(owner);
      stage = 'protected console';
      await signIn(owner, credentials);
      await expect(owner.getByRole('link', { name: 'Manage administrator access' })).toBeVisible();
      await expect(owner.getByRole('list', { name: 'Granted capabilities' }).getByText('Platform Owner', { exact: true })).toBeVisible();
      await checkAdminStorage(owner);
      stage = 'invitation and MFA';
      const invited = await invite(owner, operator);
      await signIn(operator, invited);
      await assertNoGrant(operator);
      stage = 'capability delegation';
      await grantSupport(owner, operator, credentials);
      await signIn(operator, invited);
      await expectSupport(operator);
      stage = 'refresh and sign-out';
      const revoked = await refreshAndSignOut(operator);
      stage = 'merchant and provider isolation';
      await assertMerchantIsolation(browser, owner);
      stage = 'audit history';
      await checkAudit(owner);
      writeFileSync(stateFile, JSON.stringify({ operator: invited, revoked }), { mode: 0o600 });
      observe('enroll', [
        'Provider owner reached protected Console after bootstrap, authenticator verification and recovery-code acknowledgement.',
        'A one-use Mailpit invitation enrolled an operator at 390px. No-grant access was denied.',
        'Owner delegated Support in the editor. Old access and refresh returned 401. New sign-in showed Support alone.',
        'HttpOnly SameSite=Strict cookies, no credential storage or worker, no-store, CSRF/origin denial and device sign-out observed.',
        'Merchant and provider credentials were rejected across authorities. Merchant login and its shop remained usable.',
        'Owner audit page displayed the capability change and denied read events.',
      ]);
    } catch (error) {
      if (error instanceof SetupUnavailable) throw error;
      throw new Error('Provider journey failed at ' + stage);
    } finally {
      await owner.close();
      await operator.close();
    }
  });
} else if (phase === 'restart') {
  test('restart preserves identity and revocation without widening access', async ({ browser, page }) => {
    try {
      const { operator, revoked } = JSON.parse(readFileSync(stateFile, 'utf8')) as RestartState;
      await assertRevoked(browser, revoked);
      await signIn(page, operator);
      await expectSupport(page);
      await checkAdminStorage(page);
      observe('restart', [
        'After Compose recreation, the old signed-out access and refresh returned 401 with fresh anti-CSRF state.',
        'Existing operator signed in with the same password and authenticator. Support alone persisted.',
      ]);
    } catch {
      throw new Error('Provider journey failed at restart');
    }
  });
} else {
  throw new Error('P0115_PHASE must be enroll or restart');
}

async function bootstrap(page: Page) {
  await page.goto(origin + '/auth/bootstrap');
  const proof = remember(readFileSync(required('P0115_PROOF_FILE'), 'utf8').trim());
  const chosen = password();
  await fillSecret(page, 'Setup proof', proof);
  await fillSecret(page, 'Password', chosen);
  await page.getByRole('button', { name: 'Create owner account' }).click();
  await expect(page.getByRole('heading', { name: 'MFA setup required' })).toBeVisible();
  const route = '/api/admin/auth/bootstrap';
  const context = await (await page.request.get(origin + route + '/context')).json();
  expect(context.available).toBe(false);
  const replay = await page.request.post(origin + route, {
    data: { proofToken: proof, password: chosen },
    headers: { Origin: origin, 'X-Bootstrap-CSRF': remember(context.requestToken) },
  });
  expect(replay.status()).toBe(409);
  return enroll(page, ownerEmail, chosen);
}

async function invite(owner: Page, operator: Page) {
  await owner.getByRole('link', { name: 'Manage administrator invitations' }).click();
  await owner.getByLabel('Administrator email').fill(inviteeEmail);
  await owner.getByRole('button', { name: 'Send invitation' }).click();
  await expect(owner.getByRole('heading', { name: 'Invitation sent' })).toBeVisible();
  const token = await invitationToken();
  const chosen = password();
  await operator.addInitScript((fragment) => {
    if (location.pathname === '/auth/invitation' && !location.hash)
      history.replaceState(null, '', location.pathname + '#token=' + fragment);
  }, token);
  await operator.goto(origin + '/auth/invitation');
  await operator.getByLabel('Invited email').fill(inviteeEmail);
  await fillSecret(operator, 'Password', chosen);
  await operator.getByRole('button', { name: 'Accept invitation' }).click();
  await expect(operator.getByRole('heading', { name: 'MFA setup required' })).toBeVisible();
  expect(operator.url() === origin + '/auth/invitation').toBe(true);
  await assertInvitationConsumed(operator, token, chosen);
  return enroll(operator, inviteeEmail, chosen);
}

async function invitationToken() {
  const mailpit = loopback('P0115_MAILPIT');
  const search = new URL('/api/v1/search', mailpit);
  search.searchParams.set('query', 'to:"' + inviteeEmail + '"');
  let token: string | undefined;
  await expect.poll(async () => {
    const { messages } = await (await fetch(search)).json() as { messages: { ID: string }[] };
    if (messages.length !== 1) return false;
    const { Text } = await (await fetch(mailpit + '/api/v1/message/' + messages[0].ID)).json();
    token = /#token=(i1_[A-Za-z0-9_-]{86})/.exec(Text)?.[1];
    return Boolean(token);
  }, { timeout: 20_000 }).toBe(true);
  return remember(token!);
}

async function assertInvitationConsumed(page: Page, invitationToken: string, password: string) {
  const path = origin + '/api/admin/auth/invitation';
  const response = await page.request.get(path + '/context');
  expect(response.status()).toBe(200);
  const { requestToken } = await response.json();
  const replay = await page.request.post(path + '/accept', {
    data: { invitationToken, email: inviteeEmail, password },
    headers: { Origin: origin, 'X-Bootstrap-CSRF': remember(requestToken) },
  });
  expect(replay.status()).toBe(409);
}

async function assertNoGrant(page: Page) {
  expect((await readSession(page)).capabilities).toEqual([]);
  await expect(page.getByText('No provider capabilities are assigned to this account.')).toBeVisible();
  const denied = await page.request.get(origin + audit);
  expect(denied.status()).toBe(403);
  expect(denied.headers()['cache-control']).toBe('no-store');
  await page.goto(origin + '/audit');
  await expect(page.getByRole('alert')).toContainText('You cannot inspect provider audit history.');
  await expect(page.getByRole('list', { name: 'Audit events' })).toHaveCount(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
}

async function grantSupport(owner: Page, operator: Page, account: Account) {
  // Refresh recent MFA through the ordinary sign-in page before the sensitive edit.
  await signIn(owner, account);
  await owner.getByRole('link', { name: 'Manage administrator access' }).click();
  const response = await owner.request.get(origin + users);
  expect(response.status()).toBe(200);
  const { items } = await response.json() as { items: { id: string; email: string }[] };
  const target = items.find(row => row.email.toLowerCase() === inviteeEmail);
  expect(Boolean(target)).toBe(true);
  await owner.getByLabel('Administrator', { exact: true }).selectOption(target!.id);
  await owner.getByRole('checkbox', { name: 'Support', exact: true }).check();
  await owner.getByLabel('Reason', { exact: true }).selectOption('responsibilities-changed');
  await owner.getByLabel('I confirm these access changes.').check();
  await owner.getByRole('button', { name: 'Save administrator access' }).click();
  await expect(owner.getByRole('status')).toHaveText('Administrator access saved.');
  expect((await operator.request.get(origin + session)).status()).toBe(401);
  expect(await postRefresh(operator, await contextProof(operator))).toBe(401);
}

async function expectSupport(page: Page) {
  expect((await readSession(page)).capabilities).toEqual(['support']);
  await expect(page.getByRole('list', { name: 'Granted capabilities' }).getByText('Support', { exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Manage administrator access' })).toHaveCount(0);
}

async function refreshAndSignOut(page: Page) {
  const prior = await privateCookies(page);
  const proof = await contextProof(page);
  expect(await postRefresh(page)).toBe(403);
  expect(await postRefresh(page, proof, 'http://localhost:1')).toBe(403);
  expect(await postRefresh(page, proof)).toBe(200);
  const current = await privateCookies(page);
  const refreshCookie = (cookies: SessionCookies) => cookies.find(cookie => cookie.name.endsWith('.Refresh'))!.value;
  expect(refreshCookie(prior) !== refreshCookie(current)).toBe(true);
  await page.getByRole('link', { name: 'Device sessions' }).click();
  const ended = page.waitForResponse(response => new URL(response.url()).pathname === '/api/admin/auth/sign-out');
  await page.getByRole('button', { name: 'Sign out this device' }).click();
  expect((await ended).status()).toBe(204);
  await assertRevoked(page.context().browser()!, current);
  return current;
}

async function assertRevoked(browser: Browser, cookies: SessionCookies) {
  const stale = await browser.newPage();
  try {
    await stale.context().addCookies(cookies);
    expect((await stale.request.get(origin + session)).status()).toBe(401);
    expect(await postRefresh(stale, await contextProof(stale))).toBe(401);
  } finally {
    await stale.close();
  }
}

async function assertMerchantIsolation(browser: Browser, owner: Page) {
  const merchant = loopback('P0115_MERCHANT_WEB');
  const credentials = JSON.parse(readFileSync(required('P0115_MERCHANT_CREDENTIALS'), 'utf8'));
  const state = JSON.parse(readFileSync(required('P0115_MERCHANT_STATE'), 'utf8'));
  const page = await browser.newPage();
  try {
    await page.goto(merchant + '/login');
    await page.locator('#identifier').fill(credentials.email);
    await page.locator('#password').fill(remember(credentials.password));
    const login = page.waitForResponse(response => new URL(response.url()).pathname === '/api/auth/login');
    await page.locator('button[type=submit]').click();
    const response = await login;
    expect(response.status()).toBe(200);
    const { accessToken } = await response.json();
    await expect(page).not.toHaveURL(/\/(login|register|auth\/)/);
    await page.goto(origin + '/');
    await expect(page).toHaveURL(origin + '/auth/sign-in');
    await expect(page.getByRole('heading', { name: 'Granted capabilities' })).toHaveCount(0);
    const request = (base: string, route: string, token: string) =>
      page.request.get(base + route, { headers: { Authorization: 'Bearer ' + remember(token) } });
    expect((await request(origin, session, accessToken)).status()).toBe(401);
    expect((await page.request.get(origin + '/api/shops/me')).status()).toBe(404);
    const adminAccess = (await privateCookies(owner)).find(cookie => cookie.name.endsWith('.Access'))!;
    expect((await request(merchant, '/api/shops/me', adminAccess.value)).status()).toBe(401);
    const shops = await request(merchant, '/api/shops/me', accessToken);
    expect(shops.status()).toBe(200);
    expect((await shops.json()).map((shop: { id?: string; shopId?: string }) => shop.shopId ?? shop.id)).toEqual([state.shopId]);
  } finally {
    await page.close();
  }
}

async function checkAudit(owner: Page) {
  await owner.getByRole('link', { name: 'Provider Console' }).click();
  await owner.getByRole('link', { name: 'Audit history' }).click();
  await expect(owner.getByText('Administrator capabilities changed', { exact: true }).last()).toBeVisible();
  const response = await owner.request.get(origin + audit + '?action=audit-read-denied');
  expect(response.status()).toBe(200);
  expect(response.headers()['cache-control']).toBe('no-store');
  expect((await response.json()).items.length).toBeGreaterThan(0);
}

function observe(name: string, lines: string[]) {
  writeFileSync(scratch + '/observed-' + name, lines.join('\n') + '\n', { mode: 0o600 });
}
