import { expect, test, type Browser, type BrowserContext, type Page } from '@playwright/test';
import {
  adminPost, createOperator, createOwner, currentSession, devicesPath, holdResponse, mail,
  operatorEmail, origin, proofFile, refreshPath, sessionCookies, sessionPath, signOutPath,
} from './p01-11-fixture';

const sessionRoute = '/security/sessions';
const revokeReason = { reason: 'device-unrecognized' };
const coordinationKey = 'intelibill.admin.session-coordination.v1';
const revertOrigin = 'https://other.example.test';

test.skip(!origin || !mail || !proofFile, 'Run with the disposable P01-11 fixture');

test('own-device management and sign-out across tabs on source API and PostgreSQL', async ({ browser }) => {
  const owner = await browser.newPage({ viewport: { width: 1280, height: 800 } });
  const actorContext = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const actor = await actorContext.newPage();
  const target = await browser.newPage();
  const sibling = await browser.newPage();
  try {
    await createOwner(owner);
    await assertAnonymous(actor);
    const signInOperator = await createOperator(owner, actor);
    const active = await currentSession(actor);
    expect(active.capabilities).toEqual([]);
    await signInOperator(target);
    const targetId = (await currentSession(target)).sessionId;
    await signInOperator(sibling);
    const siblingId = (await currentSession(sibling)).sessionId;

    await assertScopeAndCsrf(owner, actor, targetId);
    await revokeDevice(actor, active.sessionId, targetId, siblingId);
    await assertEnded(target);
    await target.reload();
    await expect(target).toHaveURL(origin + '/auth/sign-in');
    expect((await adminPost(sibling, refreshPath)).status()).toBe(200);
    expect((await currentSession(sibling)).sessionId).toBe(siblingId);
    expect((await currentSession(actor)).sessionId).toBe(active.sessionId);
    console.log('Ordinary administrator revoked only the selected device. Its access and refresh returned 401; sibling session renewed.');

    await endWithHeldResponses(browser, actor);
    await signInOperator(actor);
    expect((await currentSession(actor)).sessionId).not.toBe(active.sessionId);
    await endWithoutAccessCookie(sibling);
    await assertAudit(owner, targetId);
  } finally {
    await Promise.all([owner, actor, target, sibling].map((page) => page.context().close()));
  }
});

async function assertAnonymous(page: Page) {
  await page.goto(origin + sessionRoute);
  await expect(page).toHaveURL(origin + '/auth/sign-in');
  const response = await page.request.get(origin + devicesPath);
  expect(response.status()).toBe(401);
  expect(response.headers()['cache-control']).toBe('no-store');
}

async function assertScopeAndCsrf(owner: Page, actor: Page, target: string) {
  const ownerId = (await currentSession(owner)).sessionId;
  const scoped = await owner.request.get(origin + devicesPath + '?adminId=' + target);
  expect(scoped.status()).toBe(400);
  const foreign = await adminPost(owner, revokePath(target), revokeReason);
  const missing = await adminPost(owner, revokePath('11111111-1111-1111-1111-111111111111'), revokeReason);
  expect(foreign.status()).toBe(404);
  expect(missing.status()).toBe(404);
  expect((await foreign.json()).code).toBe((await missing.json()).code);
  expect((await adminPost(actor, revokePath(ownerId), revokeReason)).status()).toBe(404);
  const csrf = await actor.request.post(origin + revokePath(target), { data: revokeReason, headers: { Origin: origin } });
  expect(csrf.status()).toBe(403);
  expect((await adminPost(actor, revokePath(target), revokeReason, revertOrigin)).status()).toBe(403);
  expect((await actor.request.post(origin + signOutPath, { headers: { Origin: origin } })).status()).toBe(403);
  expect((await adminPost(actor, signOutPath, undefined, revertOrigin)).status()).toBe(403);
}

async function revokeDevice(actor: Page, current: string, target: string, sibling: string) {
  const waiting = actor.waitForResponse((response) => new URL(response.url()).pathname === devicesPath, { timeout: 10_000 });
  await actor.getByRole('link', { name: 'Device sessions' }).click();
  const response = await waiting;
  expect(response.status()).toBe(200);
  expect(response.headers()['cache-control']).toBe('no-store');
  const body = await response.json();
  expect(body.items.map(({ id }: { id: string }) => id).sort()).toEqual([current, target, sibling].sort());
  for (const row of body.items) {
    expect(Object.keys(row).sort()).toEqual(['absoluteExpiresAt', 'createdAt', 'id', 'isCurrent', 'lastSeenAt']);
    expect(row.isCurrent).toBe(row.id === current);
  }
  expect(JSON.stringify(body).includes(operatorEmail)).toBe(false);
  await expect(actor.getByRole('heading', { name: 'This device' })).toHaveCount(1);
  await expect(actor.getByRole('heading', { name: 'Other device' })).toHaveCount(2);
  await actor.screenshot({ path: process.env['P0111_ARTIFACTS'] + '/device-sessions-desktop.png' });
  await actor.setViewportSize({ width: 390, height: 844 });

  const device = actor.locator('section[aria-labelledby="device-' + target + '"]');
  await device.getByRole('button', { name: 'Revoke device' }).click();
  await device.getByLabel('Reason for revoking this device').selectOption(revokeReason.reason);
  expect(await actor.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
  await actor.screenshot({ path: process.env['P0111_ARTIFACTS'] + '/device-revocation-narrow.png' });
  await device.getByRole('button', { name: 'Confirm revoke' }).click();
  await expect(actor.getByText('Device session revoked.')).toBeVisible();
  await expect(device).toHaveCount(0);
  await expect(actor.getByRole('heading', { name: 'Other device' })).toHaveCount(1);
}

async function endWithHeldResponses(browser: Browser, actor: Page) {
  const context = actor.context();
  const existingTab = await context.newPage();
  await existingTab.goto(origin + '/');
  await expect(existingTab.getByRole('link', { name: 'Device sessions' })).toBeVisible();
  const delayedList = await holdResponse(existingTab, devicesPath);
  await existingTab.getByRole('link', { name: 'Device sessions' }).click();
  expect(await delayedList.status).toBe(200);

  const refreshingTab = await context.newPage();
  const delayedRefresh = await holdResponse(refreshingTab, refreshPath, true);
  const delayedEnd = await holdResponse(actor, signOutPath, true);
  try {
    await context.clearCookies({ name: 'InteliBill.Admin.Access' });
    const loading = refreshingTab.goto(origin + '/');
    expect(await delayedRefresh.status).toBe(200);
    let endRequests = 0;
    actor.on('request', (request) => {
      if (new URL(request.url()).pathname === signOutPath) endRequests++;
    });

    await actor.getByRole('button', { name: 'Sign out this device' }).click();
    await assertLocalClearance(actor, existingTab);
    expect(endRequests).toBe(0);
    await assertCoordinationOnly(actor, true);
    delayedList.release('deliver');
    delayedRefresh.release('deliver');
    expect(await delayedEnd.status).toBe(204);
    await loading;
    await assertLocalClearance(actor, existingTab);
    await expect(refreshingTab).toHaveURL(origin + '/auth/sign-in');
    await expect(actor.getByText('Signing out…')).toBeVisible();
    await actor.screenshot({ path: process.env['P0111_ARTIFACTS'] + '/sign-out-response-held.png' });

    const heldCookies = (await context.cookies()).filter(({ name }) => sessionCookies.test(name));
    expect(heldCookies).toHaveLength(2);
    await assertSavedPairEnded(browser, heldCookies);
    delayedEnd.release('lose');
    await expect(actor.getByRole('alert')).toContainText('Server sign-out could not be confirmed.');
    await actor.reload();
    await expect(actor.getByRole('alert')).toContainText('Server sign-out could not be confirmed.');
    await existingTab.getByRole('button', { name: 'Retry sign-out' }).click();
    await expect(existingTab.getByText('Signed out.', { exact: true })).toBeVisible();
    await expect(actor.getByText('Signed out.', { exact: true })).toBeVisible();
    await assertCoordinationOnly(actor, false);
    console.log('Protected content cleared locally and across tabs before the held refresh and sign-out responses. Lost response survived reload; retry confirmed the end.');
  } finally {
    delayedList.release('deliver');
    delayedRefresh.release('deliver');
    delayedEnd.release('deliver');
    await Promise.all([existingTab.close(), refreshingTab.close()]);
  }
}

async function assertLocalClearance(actor: Page, tab: Page) {
  for (const page of [actor, tab]) {
    await expect(page).toHaveURL(origin + '/auth/sign-in');
    await expect(page.getByRole('heading', { name: 'This device' })).toHaveCount(0);
    await expect(page.getByRole('heading', { name: 'Granted capabilities' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Continue session' })).toHaveCount(0);
  }
}

async function assertSavedPairEnded(browser: Browser, cookies: Awaited<ReturnType<BrowserContext['cookies']>>) {
  const holder = await browser.newPage();
  try {
    await holder.context().addCookies(cookies);
    await assertEnded(holder);
  } finally {
    await holder.context().close();
  }
}

async function endWithoutAccessCookie(page: Page) {
  const pair = (await page.context().cookies()).filter(({ name }) => sessionCookies.test(name));
  expect(pair).toHaveLength(2);
  for (const cookie of pair) {
    expect(cookie.httpOnly).toBe(true);
    expect(cookie.sameSite).toBe('Strict');
    expect(cookie.path).toBe('/api/admin');
  }
  await page.context().clearCookies({ name: 'InteliBill.Admin.Access' });
  expect((await page.request.get(origin + sessionPath)).status()).toBe(401);
  const answer = page.waitForResponse((response) => new URL(response.url()).pathname === signOutPath, { timeout: 10_000 });
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  expect((await answer).status()).toBe(204);
  await expect(page.getByText('Signed out.', { exact: true })).toBeVisible();
  await page.context().addCookies(pair);
  await assertEnded(page);
  console.log('Sign-out with only the refresh cookie ended its family. Saved access and refresh were refused.');
}

async function assertAudit(owner: Page, target: string) {
  const audit = await owner.request.get(origin + '/api/admin/audit?scope=provider-identity&action=device-session-revoked');
  expect(audit.status()).toBe(200);
  const { items } = await audit.json();
  expect(items).toHaveLength(1);
  expect(items[0].after).toMatchObject({ targetKind: 'Session', targetId: target, state: 'Revoked' });
  expect(items[0].after.version).not.toBe(items[0].before.version);
}

async function assertEnded(page: Page) {
  const access = await page.request.get(origin + sessionPath);
  expect(access.status()).toBe(401);
  expect(access.headers()['cache-control']).toBe('no-store');
  expect((await adminPost(page, refreshPath)).status()).toBe(401);
}

async function assertCoordinationOnly(page: Page, pending: boolean) {
  const stored = await page.evaluate(async (key) => ({
    local: Object.fromEntries(Object.entries(localStorage)),
    coordination: JSON.parse(localStorage.getItem(key)!),
    session: sessionStorage.length, databases: (await indexedDB.databases()).length,
    caches: (await caches.keys()).length, cookies: document.cookie.length,
  }), coordinationKey);
  expect(stored.coordination.generation).toMatch(/^[a-f0-9-]{36}$/);
  expect(Object.keys(stored.coordination).sort()).toEqual(['generation', 'state']);
  expect(Object.keys(stored.local).sort()).toEqual(pending
    ? [coordinationKey, coordinationKey + '.sign-out.' + stored.coordination.generation] : [coordinationKey]);
  if (pending) expect(stored.local[coordinationKey + '.sign-out.' + stored.coordination.generation]).toBe('1');
  expect({ ...stored, local: undefined, coordination: undefined }).toMatchObject({
    session: 0, databases: 0, caches: 0, cookies: 0,
  });
}

function revokePath(id: string) { return devicesPath + '/' + id + '/revoke'; }
