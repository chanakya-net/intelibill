import { createHmac, randomBytes } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { expect, test, type BrowserContext, type Page } from '@playwright/test';

const origin = process.env['P0110_ADMIN_WEB']!;
const proofFile = process.env['P0110_PROOF_FILE']!;
const ownerEmail = 'p0110-owner@example.test';
const refreshPath = '/api/admin/auth/refresh';
const accessCookie = 'InteliBill.Admin.Access';
const sessionCookie = /^InteliBill\.Admin\.(Access|Refresh)$/;

test.skip(!origin || !proofFile, 'Run with the disposable P01-10 fixture');

test('coordinated refresh across tabs, unknown outcomes and reuse-driven sign-out', async ({ browser }) => {
  const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const refreshes = watchRefreshes(context);
  try {
    const first = await context.newPage();
    await signInOwner(first);
    await assertRefreshCookie(context);

    // 1. Three tabs lose their access token together; renewals take turns, each presents the current credential.
    const tabs = [first, await context.newPage(), await context.newPage()];
    for (const tab of tabs.slice(1)) await tab.goto(origin + '/');
    await expectDashboard(tabs);
    await context.clearCookies({ name: accessCookie });
    refreshes.reset();
    await Promise.all(tabs.map((tab) => tab.reload()));
    await expectDashboard(tabs);
    expect(refreshes.statuses.length).toBeGreaterThanOrEqual(1);
    expect(refreshes.statuses.every((status) => status === 200)).toBe(true);
    expect(refreshes.maxConcurrent).toBe(1);
    console.log(`Concurrent tabs renewed with ${refreshes.statuses.length} serialized refreshes, all 200, max in flight 1.`);

    // 2. A renewal that never reached the API stays unknown: grants hidden, no sign-in redirect, no automatic retry.
    await context.clearCookies({ name: accessCookie });
    refreshes.reset();
    await first.route('**' + refreshPath, (route) => route.abort('connectionreset'), { times: 1 });
    await first.reload();
    await expectUncertain(first);
    expect(refreshes.attempts).toBe(1);
    await first.getByRole('button', { name: 'Try again' }).click();
    await expectDashboard([first]);
    expect(refreshes.statuses).toEqual([200]);

    // 3. The API rotated but the answer was lost: the retry presents a consumed credential and ends the session.
    await context.clearCookies({ name: accessCookie });
    refreshes.reset();
    await first.route('**' + refreshPath, async (route) => {
      const held = (await context.cookies()).filter((cookie) => sessionCookie.test(cookie.name));
      const answer = await route.fetch();
      expect(answer.status()).toBe(200);
      // route.fetch shares the context's cookie jar; put back exactly what the browser held before the lost answer.
      await context.clearCookies({ name: sessionCookie });
      await context.addCookies(held);
      await route.abort('connectionreset');
    }, { times: 1 });
    await first.reload();
    await expectUncertain(first);
    await first.getByRole('button', { name: 'Try again' }).click();
    await expect(first.getByRole('alert')).toHaveText('Your provider session ended.');
    await expect(first.getByRole('heading', { name: 'Granted capabilities' })).toHaveCount(0);
    expect(refreshes.statuses).toEqual([401]);

    // 4. Every other tab of the revoked session is refused on its next protected request.
    for (const tab of tabs.slice(1)) {
      await tab.reload();
      await expect(tab).toHaveURL(origin + '/auth/sign-in');
    }
    const ended = await first.request.get(origin + '/api/admin/auth/session');
    expect(ended.status()).toBe(401);
    expect(ended.headers()['cache-control']).toBe('no-store');
    await assertNoBrowserStorage(first);
    console.log('Unknown renewal kept the tab unconfirmed without retrying; the lost-answer retry revoked the session and every tab left the dashboard.');
  } finally {
    await context.close();
  }
});

function watchRefreshes(context: BrowserContext) {
  const state = { attempts: 0, statuses: [] as number[], active: 0, maxConcurrent: 0 };
  const isRefresh = (url: string) => new URL(url).pathname === refreshPath;
  context.on('request', (request) => {
    if (!isRefresh(request.url())) return;
    state.attempts++;
    state.active++;
    state.maxConcurrent = Math.max(state.maxConcurrent, state.active);
  });
  const settle = () => (state.active = Math.max(0, state.active - 1));
  context.on('requestfinished', async (request) => {
    if (!isRefresh(request.url())) return;
    settle();
    const response = await request.response();
    if (response) state.statuses.push(response.status());
  });
  context.on('requestfailed', (request) => {
    if (isRefresh(request.url())) settle();
  });
  return {
    get attempts() { return state.attempts; },
    get statuses() { return state.statuses; },
    get maxConcurrent() { return state.maxConcurrent; },
    reset() { Object.assign(state, { attempts: 0, statuses: [], maxConcurrent: state.active }); },
  };
}

async function expectDashboard(tabs: Page[]) {
  for (const tab of tabs) {
    await expect(tab).toHaveURL(origin + '/');
    await expect(tab.getByRole('heading', { name: 'Granted capabilities' })).toBeVisible();
  }
}

async function expectUncertain(page: Page) {
  await expect(page).toHaveURL(origin + '/');
  await expect(page.getByRole('alert')).toContainText('We could not confirm your provider session.');
  await expect(page.getByRole('heading', { name: 'Granted capabilities' })).toHaveCount(0);
}

async function signInOwner(page: Page) {
  const password = 'Fixture-' + randomBytes(20).toString('hex');
  await page.goto(origin + '/auth/bootstrap');
  await fillSecret(page, 'Setup proof', readFileSync(proofFile, 'utf8').trim());
  await fillSecret(page, 'Password', password);
  await page.getByRole('button', { name: 'Create owner account' }).click();
  await page.getByRole('link', { name: 'Set up authenticator' }).click();
  const key = page.getByTestId('setup-key');
  await expect(key).toBeVisible();
  const seed = (await key.textContent())!;
  const time = Date.now();
  await fillSecret(page, 'Authenticator code', otp(seed, time));
  await page.getByRole('button', { name: 'Verify authenticator' }).click();
  await expect(page.getByRole('heading', { name: 'Authenticator enrolled' })).toBeVisible();
  await page.getByRole('button', { name: 'I saved my codes' }).click();
  await page.getByRole('link', { name: 'Sign in to the Provider Console' }).click();
  await page.getByLabel('Email', { exact: true }).fill(ownerEmail);
  await fillSecret(page, 'Password', password);
  await fillSecret(page, 'Authenticator code', otp(seed, time + 30_000));
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expectDashboard([page]);
}

async function assertRefreshCookie(context: BrowserContext) {
  const cookie = (await context.cookies()).find((item) => item.name === 'InteliBill.Admin.Refresh');
  expect(cookie?.httpOnly).toBe(true);
  expect(cookie?.sameSite).toBe('Strict');
  expect(cookie?.path).toBe('/api/admin');
}

async function assertNoBrowserStorage(page: Page) {
  const storage = await page.evaluate(async () => ({
    local: localStorage.length, session: sessionStorage.length, databases: (await indexedDB.databases()).length,
    caches: (await caches.keys()).length, cookies: document.cookie.length,
  }));
  expect(storage).toEqual({ local: 0, session: 0, databases: 0, caches: 0, cookies: 0 });
}

async function fillSecret(page: Page, label: string, value: string) {
  await page.getByLabel(label, { exact: true }).evaluate((input: HTMLInputElement, secret) => {
    input.value = secret;
  }, value);
}

function otp(seed: string, timestamp: number) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  const bits = [...seed].map((character) => alphabet.indexOf(character).toString(2).padStart(5, '0')).join('');
  const bytes = Buffer.from(bits.match(/.{8}/g)!.map((byte) => parseInt(byte, 2)));
  const step = Buffer.alloc(8);
  step.writeBigUInt64BE(BigInt(Math.floor(timestamp / 30_000)));
  const mac = createHmac('sha1', bytes).update(step).digest();
  return String((mac.readUInt32BE(mac.at(-1)! & 15) & 0x7fffffff) % 1_000_000).padStart(6, '0');
}
