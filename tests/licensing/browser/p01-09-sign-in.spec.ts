import { createHmac, randomBytes } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { expect, test, type Page } from '@playwright/test';

const origin = process.env['P0109_ADMIN_WEB']!;
const mailpit = process.env['P0109_MAILPIT']!;
const proofFile = process.env['P0109_PROOF_FILE']!;
const ownerEmail = 'p0109-owner@example.test';
const inviteeEmail = 'p0109-invitee@example.test';
const signIn = '/api/admin/auth/sign-in';
const session = '/api/admin/auth/session';

test.skip(!origin || !mailpit || !proofFile, 'Run with the disposable P01-09 fixture');

test('real sign-in, reload, invitation scope and request boundaries', async ({ browser }) => {
  const owner = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const invitee = await browser.newContext({ viewport: { width: 390, height: 844 } });
  try {
    const page = await owner.newPage();
    await assertAnonymous(page);
    const password = randomPassword();
    await page.goto(origin + '/auth/bootstrap');
    await fillSecret(page, 'Setup proof', readFileSync(proofFile, 'utf8').trim());
    await fillSecret(page, 'Password', password);
    await page.getByRole('button', { name: 'Create owner account' }).click();
    const ownerEnrollment = await enroll(page);
    await authenticate(page, ownerEmail, password, ownerEnrollment);
    await expect(page.getByRole('link', { name: 'Manage administrator invitations' })).toBeVisible();
    await page.reload();
    await expect(page.getByRole('heading', { name: 'Granted capabilities' })).toBeVisible();
    await expect(page.getByRole('list', { name: 'Granted capabilities' }).getByRole('listitem')).toHaveCount(3);
    await assertSessionIsolation(page);
    await assertMutationBoundaries(page);
    const invitation = await invite(page);

    const inviteePage = await invitee.newPage();
    const inviteePassword = randomPassword();
    await acceptInvite(inviteePage, invitation, inviteePassword);
    const inviteeEnrollment = await enroll(inviteePage);
    await authenticate(inviteePage, inviteeEmail, inviteePassword, inviteeEnrollment);
    await expect(inviteePage.getByText('No provider capabilities are assigned to this account.')).toBeVisible();
    await expect(inviteePage.getByRole('link', { name: 'Manage administrator invitations' })).toHaveCount(0);
    await assertInvitationDenied(inviteePage);
    console.log('Anonymous dashboard redirect, owner sign-in and reload, invitee direct-route 403, private cookies, no browser storage, missing CSRF 403 and cross-origin 403 verified.');
  } finally {
    await invitee.close();
    await owner.close();
  }
});

async function assertAnonymous(page: Page) {
  await page.goto(origin + '/');
  await expect(page).toHaveURL(origin + '/auth/sign-in');
  await expect(page.getByRole('heading', { name: 'Sign in to the Provider Console' })).toBeVisible();
  const response = await page.request.get(origin + session);
  expect(response.status()).toBe(401);
  expect(response.headers()['cache-control']).toBe('no-store');
}

async function enroll(page: Page) {
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
  return { seed, time };
}

async function authenticate(page: Page, email: string, password: string, enrollment: { seed: string; time: number }) {
  await page.getByLabel('Email', { exact: true }).fill(email);
  await fillSecret(page, 'Password', password);
  // The authenticator accepts a one-step clock window. Use the step after enrollment to prove replay protection.
  await fillSecret(page, 'Authenticator code', otp(enrollment.seed, enrollment.time + 30_000));
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(origin + '/');
  await expect(page.getByRole('heading', { name: 'Granted capabilities' })).toBeVisible();
}

async function assertSessionIsolation(page: Page) {
  const cookie = (await page.context().cookies()).find(item => item.name === 'InteliBill.Admin.Access');
  expect(cookie?.httpOnly).toBe(true);
  expect(cookie?.path).toBe('/api/admin');
  expect(cookie?.sameSite).toBe('Strict');
  const storage = await page.evaluate(async () => ({
    local: localStorage.length, session: sessionStorage.length, databases: (await indexedDB.databases()).length,
    caches: (await caches.keys()).length, cookies: document.cookie.length,
  }));
  expect(storage).toEqual({ local: 0, session: 0, databases: 0, caches: 0, cookies: 0 });
}

async function assertMutationBoundaries(page: Page) {
  const missing = await page.evaluate(async (path) => (await fetch(path, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}',
  })).status, signIn);
  expect(missing).toBe(403);
  const context = await page.request.get(origin + signIn + '/context');
  const { requestToken } = await context.json();
  const crossOrigin = await page.request.post(origin + signIn, {
    headers: { Origin: 'http://localhost:1', 'X-Admin-CSRF': requestToken },
    data: {},
  });
  expect(crossOrigin.status()).toBe(403);
  expect(crossOrigin.headers()['access-control-allow-origin']).toBeUndefined();
}

async function invite(page: Page) {
  await page.getByRole('link', { name: 'Manage administrator invitations' }).click();
  await page.getByLabel('Administrator email').fill(inviteeEmail);
  await page.getByRole('button', { name: 'Send invitation' }).click();
  await expect(page.getByRole('heading', { name: 'Invitation sent' })).toBeVisible();
  const messages = await (await fetch(mailpit + '/api/v1/messages')).json() as { messages: { ID: string }[] };
  expect(messages.messages.length).toBe(1);
  const message = await (await fetch(mailpit + '/api/v1/message/' + messages.messages[0].ID)).json() as { Text: string };
  const token = /#token=(i1_[A-Za-z0-9_-]{86})/.exec(message.Text)?.[1];
  expect(Boolean(token), 'Mailpit received the fixture invitation').toBe(true);
  return token!;
}

async function acceptInvite(page: Page, invitation: string, password: string) {
  await page.addInitScript((token) => {
    if (location.pathname === '/auth/invitation' && !location.hash)
      history.replaceState(null, '', location.pathname + '#token=' + token);
  }, invitation);
  await page.goto(origin + '/auth/invitation');
  await page.getByLabel('Invited email').fill(inviteeEmail);
  await fillSecret(page, 'Password', password);
  await page.getByRole('button', { name: 'Accept invitation' }).click();
  await expect(page.getByRole('heading', { name: 'MFA setup required' })).toBeVisible();
}

async function assertInvitationDenied(page: Page) {
  const response = page.waitForResponse((result) => new URL(result.url()).pathname === '/api/admin/invitations/context');
  await page.goto(origin + '/admin-users/invite');
  expect((await response).status()).toBe(403);
  await expect(page.getByRole('alert')).toHaveText('You cannot manage administrator invitations.');
  await expect(page.getByLabel('Administrator email')).toHaveCount(0);
}

async function fillSecret(page: Page, label: string, value: string) {
  await page.getByLabel(label, { exact: true }).evaluate((input: HTMLInputElement, secret) => {
    input.value = secret;
  }, value);
}

function randomPassword() { return 'Fixture-' + randomBytes(20).toString('hex'); }

function otp(seed: string, timestamp: number) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  const bits = [...seed].map(character => alphabet.indexOf(character).toString(2).padStart(5, '0')).join('');
  const bytes = Buffer.from(bits.match(/.{8}/g)!.map(byte => parseInt(byte, 2)));
  const step = Buffer.alloc(8);
  step.writeBigUInt64BE(BigInt(Math.floor(timestamp / 30_000)));
  const mac = createHmac('sha1', bytes).update(step).digest();
  return String((mac.readUInt32BE(mac.at(-1)! & 15) & 0x7fffffff) % 1_000_000).padStart(6, '0');
}
