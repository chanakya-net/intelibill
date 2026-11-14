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
    const prepareSignIn = await signInOwner(first);
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

    // Use separate pages with limited coordination support while preserving the same browser cookie jar.
    await assertCoordinationUnavailable(context, refreshes);

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

    await assertLostResponse(first, tabs[1], refreshes);
    const ended = await first.request.get(origin + '/api/admin/auth/session');
    expect(ended.status()).toBe(401);
    expect(ended.headers()['cache-control']).toBe('no-store');
    await assertCredentialFreeStorage(first);

    // Fresh sign-ins overlap a held old success and then a held refusal.
    await (await prepareSignIn(first))();
    await assertSignInWaitsForRefresh(first, tabs[1], tabs[2], prepareSignIn, refreshes, 200);
    await assertSignInWaitsForRefresh(tabs[1], first, tabs[2], prepareSignIn, refreshes, 401);
  } finally {
    await context.close();
  }
});

async function assertCoordinationUnavailable(context: BrowserContext, refreshes: ReturnType<typeof watchRefreshes>) {
  for (const mode of ['absent', 'rejected'] as const) {
    const tabs = [await context.newPage(), await context.newPage()];
    for (const tab of tabs) {
      await tab.addInitScript((mode) => {
        const locks = mode === 'absent' ? undefined : { request: () => Promise.reject(new Error('Fixture: Web Lock denied')) };
        Object.defineProperty(navigator, 'locks', { value: locks });
      }, mode);
      await tab.goto(origin + '/');
    }
    await expectDashboard(tabs);
    await context.clearCookies({ name: accessCookie });
    refreshes.reset();
    await Promise.all(tabs.map((tab) => tab.reload()));
    for (const tab of tabs) await expectUncertain(tab);
    await tabs[0].getByRole('button', { name: 'Try again' }).click();
    await expectUncertain(tabs[0]);
    expect(refreshes.attempts).toBe(0);
    for (const tab of tabs) await tab.close();
    console.log(`Two tabs with Web Locks ${mode} hid grants and offered recovery with zero refresh POSTs.`);
    const normal = await context.newPage();
    try {
      await normal.goto(origin + '/');
      await expectDashboard([normal]);
    } finally {
      await normal.close();
    }
  }
}

async function assertLostResponse(first: Page, other: Page, refreshes: ReturnType<typeof watchRefreshes>) {
  const context = first.context();
  await context.clearCookies({ name: accessCookie });
  refreshes.reset();
  const lost = await holdRefresh(first, 200);
  const firstLoad = first.reload();
  await lost.ready;
  const secondLoad = other.reload();
  try {
    await waitForCookieWriter(other);
  } finally {
    lost.release('lose');
  }
  await Promise.all([firstLoad, secondLoad]);
  for (const tab of [first, other]) await expectUncertain(tab);
  expect(refreshes.attempts).toBe(1);

  await first.getByRole('button', { name: 'Try again' }).click();
  for (const tab of [first, other]) {
    await expect(tab.getByRole('alert')).toHaveText('Your provider session ended.');
    await expect(tab.getByRole('heading', { name: 'Granted capabilities' })).toHaveCount(0);
  }
  expect(refreshes.statuses).toEqual([401]);
  console.log('Two tabs waited on a committed refresh with a lost response. Both hid grants after one POST. Explicit retry got 401.');
}

async function assertSignInWaitsForRefresh(
  old: Page,
  fresh: Page,
  stale: Page,
  prepareSignIn: PrepareSignIn,
  refreshes: ReturnType<typeof watchRefreshes>,
  refreshStatus: number,
) {
  const submit = await prepareSignIn(fresh);
  const context = old.context();
  await context.clearCookies({ name: refreshStatus === 200 ? accessCookie : sessionCookie });
  // Reload the known protected route in both tabs, including after a refused previous check.
  await stale.goto(origin + '/auth/sign-in');
  await old.goto(origin + '/auth/sign-in');
  refreshes.reset();
  const staleRequest = await holdRefusedSessionCheck(stale);
  const staleLoad = stale.goto(origin + '/');
  await staleRequest.ready;
  const held = await holdRefresh(old, refreshStatus);
  const reload = old.goto(origin + '/');
  await held.ready;
  const replacement = submit();
  try {
    await waitForCookieWriter(fresh);
  } finally {
    held.release('deliver');
  }
  await Promise.all([reload, replacement]);
  await assertRefreshCookie(context);
  const cookies = (await context.cookies()).filter(({ name }) => sessionCookie.test(name));
  expect(cookies).toHaveLength(2);
  const started = await fresh.request.get(origin + '/api/admin/auth/session');
  expect(started.status()).toBe(200);
  await expectDashboard([fresh]);
  staleRequest.release();
  await staleLoad;
  await expect(stale).toHaveURL(origin + '/auth/sign-in');
  await stale.goto(origin + '/');
  await expectDashboard([stale]);
  expect(refreshes.attempts).toBe(1);
  // A reload of the other tab can verify the fresh cookies without retrying an old protected operation.
  await old.goto(origin + '/');
  await expectDashboard([old]);
  console.log(`Fresh sign-in waited behind the held old refresh ${refreshStatus} and kept its cookie pair. A stale protected 401 sent no refresh or retry.`);
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => { resolve = done; });
  return { promise, resolve };
}

async function holdRefresh(page: Page, status: number) {
  const ready = deferred<number>();
  const disposition = deferred<'lose' | 'deliver'>();
  await page.route('**' + refreshPath, async (route) => {
    const context = page.context();
    const before = (await context.cookies()).filter((cookie) => sessionCookie.test(cookie.name));
    const answer = await route.fetch();
    // route.fetch shares the cookie jar. Restore it so the response is held until the test delivers it.
    await context.clearCookies({ name: sessionCookie });
    await context.addCookies(before);
    ready.resolve(answer.status());
    if (await disposition.promise === 'lose') await route.abort('connectionreset');
    else await route.fulfill({ response: answer });
  }, { times: 1 });
  const observed = ready.promise.then((statusCode) => expect(statusCode).toBe(status));
  return { ready: observed, release: disposition.resolve };
}

async function holdRefusedSessionCheck(page: Page) {
  const ready = deferred<number>();
  const release = deferred<void>();
  await page.route('**/api/admin/auth/session', async (route) => {
    const answer = await route.fetch();
    ready.resolve(answer.status());
    await release.promise;
    await route.fulfill({ response: answer });
  }, { times: 1 });
  return { ready: ready.promise.then((status) => expect(status).toBe(401)), release: release.resolve };
}

async function waitForCookieWriter(page: Page) {
  await expect.poll(() => page.evaluate(async () => {
    const locks = await navigator.locks.query();
    return locks.pending?.some(({ name }) => name === 'intelibill-admin-session-renewal');
  }).catch(() => false)).toBe(true);
}

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
  let step = Math.floor(time / 30_000);
  const prepareSignIn: PrepareSignIn = async (target) => {
    await target.goto(origin + '/auth/sign-in');
    await target.getByLabel('Email', { exact: true }).fill(ownerEmail);
    step = Math.max(step + 1, Math.floor(Date.now() / 30_000));
    await expect.poll(() => Math.floor(Date.now() / 30_000) + 1, { timeout: 35_000 }).toBeGreaterThanOrEqual(step);
    return async () => {
      await fillSecret(target, 'Password', password);
      await fillSecret(target, 'Authenticator code', otp(seed, step * 30_000));
      await target.getByRole('button', { name: 'Sign in', exact: true }).click();
      await expectDashboard([target]);
    };
  };
  await (await prepareSignIn(page))();
  return prepareSignIn;
}

type PrepareSignIn = (page: Page) => Promise<() => Promise<void>>;

async function assertRefreshCookie(context: BrowserContext) {
  const cookie = (await context.cookies()).find((item) => item.name === 'InteliBill.Admin.Refresh');
  expect(cookie?.httpOnly).toBe(true);
  expect(cookie?.sameSite).toBe('Strict');
  expect(cookie?.path).toBe('/api/admin');
}

async function assertCredentialFreeStorage(page: Page) {
  const storage = await page.evaluate(async () => ({
    localKeys: Object.keys(localStorage),
    coordination: JSON.parse(localStorage.getItem('intelibill.admin.session-coordination.v1') ?? 'null'),
    session: sessionStorage.length, databases: (await indexedDB.databases()).length,
    caches: (await caches.keys()).length, cookies: document.cookie.length,
  }));
  expect(storage).toEqual({
    localKeys: ['intelibill.admin.session-coordination.v1'],
    coordination: { generation: expect.stringMatching(/^[a-f0-9-]{36}$/), state: 'ended' },
    session: 0, databases: 0, caches: 0, cookies: 0,
  });
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
