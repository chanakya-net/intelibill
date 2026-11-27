import { createHmac, randomBytes } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { expect, test, type Page } from '@playwright/test';

const origin = process.env['P0114_ADMIN_WEB']!;
const mail = process.env['P0114_MAILPIT']!;
const file = process.env['P0114_PROOF_FILE']!;
const audit = '/api/admin/audit';
const bootstrap = '/api/admin/auth/bootstrap';
const invitations = '/api/admin/invitations';
const acceptance = '/api/admin/auth/invitation';
const ownerEmail = 'p0114-owner@example.test';
const operatorEmail = 'p0114-merchant-payload-canary@example.test';

test.skip(!origin || !mail || !file, 'Run with the disposable P01-14 fixture');

test('owner inspects history and ungranted operator sees no evidence', async ({ browser }) => {
  const owner = await browser.newPage({ viewport: { width: 1280, height: 800 } });
  const invitee = await browser.newPage({ viewport: { width: 390, height: 844 } });
  try {
    const proofToken = readFileSync(file, 'utf8').trim();
    const ownerPassword = fixturePassword();
    await mutate(owner, bootstrap + '/context', bootstrap, { proofToken, password: ownerPassword }, 200);
    await enrollAndSignIn(owner, ownerEmail, ownerPassword);
    await invitee.goto(origin + '/audit');
    await expect(invitee).toHaveURL(origin + '/auth/sign-in');
    const anonymous = await invitee.request.get(origin + audit);
    expect(anonymous.status()).toBe(401);
    expect(anonymous.headers()['cache-control']).toBe('no-store');

    await mutate(owner, invitations + '/context', invitations, { email: operatorEmail }, 201);
    const invitationToken = await invitationFromMail();
    const password = fixturePassword();
    await mutate(invitee, acceptance + '/context', acceptance + '/accept',
      { invitationToken, email: operatorEmail, password }, 200);
    expect((await invitee.request.get(origin + audit)).status()).toBe(401);
    await enrollAndSignIn(invitee, operatorEmail, password);

    await inspectHistory(owner, [proofToken, ownerPassword, invitationToken, password, operatorEmail,
      'p0114-attestation-canary', 'exception-message-canary']);
    await checkStaleResponse(owner);
    await assertUngranted(invitee);
  } finally {
    await invitee.close();
    await owner.close();
  }
});

async function inspectHistory(page: Page, canaries: string[]) {
  await page.goto(origin);
  const responsePromise = page.waitForResponse((response) => new URL(response.url()).pathname === audit);
  await page.getByRole('link', { name: 'Audit history' }).click();
  const response = await responsePromise;
  expect(response.status()).toBe(200);
  expect(response.headers()['cache-control']).toBe('no-store');
  const payload = await response.text();
  for (const secret of canaries) {
    expect(payload.toLowerCase().includes(secret.toLowerCase()), 'audit response contains a fixture secret').toBe(false);
  }

  await expect(page.getByRole('heading', { name: 'Audit history' })).toBeVisible();
  await page.getByLabel('Action').selectOption('invitation-accepted');
  await page.getByLabel('From (UTC)').fill('2020-01-01T00:00');
  await page.getByLabel('Before (UTC)').fill('2090-01-01T00:00');
  await page.getByRole('button', { name: 'Apply filters' }).click();
  await expect(page.getByRole('list', { name: 'Audit events' }).getByRole('listitem')).toHaveCount(1);
  await page.getByText('Event details', { exact: true }).click();
  await expect(page.getByText(/Invitation · Accepted · version [0-9]+/)).toBeVisible();
  await expect(page.getByText(/Invitation · Open · version [0-9]+/)).toBeVisible();
  await expect(page.locator('time').first()).toHaveText(/UTC$/);
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
  const text = await page.locator('main').innerText();
  for (const secret of canaries) {
    expect(text.toLowerCase().includes(secret.toLowerCase()), 'audit page contains a fixture secret').toBe(false);
  }

  await page.getByLabel('Action').selectOption('');
  await page.getByRole('button', { name: 'Apply filters' }).click();
  const list = page.getByRole('list', { name: 'Audit events' });
  await expect(list.getByRole('listitem')).toHaveCount(25);
  await page.getByRole('button', { name: 'Next page' }).click();
  await expect(page.getByText('Page 2', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Previous page' }).click();
  await expect(page.getByText('Page 1', { exact: true })).toBeVisible();
  console.log('Owner navigated from the console, applied UTC/action filters, opened the accepted version evidence and paged forward/back. Payload was no-store and excluded fixture canaries.');
}

async function checkStaleResponse(page: Page) {
  let release = () => {};
  let started = () => {};
  const hold = new Promise<void>((resolve) => { release = resolve; });
  const intercepted = new Promise<void>((resolve) => { started = resolve; });
  const matches = (url: URL) => url.pathname === audit && url.searchParams.get('action') === 'invitation-accepted';
  await page.route(matches, async (route) => {
    const source = await route.fetch();
    expect(source.status()).toBe(200);
    started();
    await hold;
    await route.fulfill({ response: source }).catch(() => {});
  });
  try {
    await page.getByLabel('Action').selectOption('invitation-accepted');
    await page.getByRole('button', { name: 'Apply filters' }).click();
    await intercepted;
    await page.getByLabel('Action').selectOption('invitation-issued');
    await page.getByRole('button', { name: 'Apply filters' }).click();
    const events = page.getByRole('list', { name: 'Audit events' });
    await expect(events.getByText('Invitation issued', { exact: true })).toBeVisible();
    release();
    await page.unrouteAll({ behavior: 'wait' });
    await expect(events.getByText('Invitation issued', { exact: true })).toBeVisible();
    await expect(events.getByText('Invitation accepted', { exact: true })).toHaveCount(0);
  } finally {
    release();
  }
  console.log('A delayed authorized result could not replace a newer filter result.');
}

async function assertUngranted(page: Page) {
  await page.goto(origin);
  await expect(page.getByText('No provider capabilities are assigned to this account.')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Audit history' })).toHaveCount(0);
  await page.goto(origin + '/audit');
  await expect(page.getByRole('alert')).toContainText('You cannot inspect provider audit history.');
  await expect(page.getByRole('list', { name: 'Audit events' })).toHaveCount(0);
  const forbidden = await page.request.get(origin + audit + '?scope=merchant-canary&cursor=secret-canary');
  expect(forbidden.status()).toBe(403);
  expect(forbidden.headers()['cache-control']).toBe('no-store');
  expect(await forbidden.text()).not.toContain('canary');
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
  console.log('The enrolled operator had no audit link and saw the denied state on a 390px deep link. Its direct API request returned no-store 403.');
}

async function mutate(page: Page, context: string, path: string, data: object, status: number) {
  const access = await page.request.get(origin + context);
  expect(access.status()).toBe(200);
  const { requestToken } = await access.json();
  const result = await page.request.post(origin + path, {
    data, headers: { Origin: origin, 'X-Bootstrap-CSRF': requestToken },
  });
  expect(result.status()).toBe(status);
  return result;
}

async function enrollAndSignIn(page: Page, email: string, password: string) {
  const mfa = bootstrap + '/enrollment/mfa';
  const setup = await mutate(page, mfa, mfa + '/start', {}, 200);
  const { setupSecret } = await setup.json();
  const now = Date.now();
  await mutate(page, mfa, mfa + '/verify', { otp: otp(setupSecret, now) }, 200);
  await page.goto(origin + '/auth/sign-in');
  await page.getByLabel('Email', { exact: true }).fill(email);
  await fillSecret(page, 'Password', password);
  await fillSecret(page, 'Authenticator code', otp(setupSecret, now + 30_000));
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(origin + '/');
}

async function fillSecret(page: Page, label: string, value: string) {
  await page.getByLabel(label, { exact: true }).evaluate((input: HTMLInputElement, secret) => {
    input.value = secret;
  }, value);
}

async function invitationFromMail() {
  const { messages } = await (await fetch(mail + '/api/v1/messages')).json();
  expect(messages).toHaveLength(1);
  const { Text } = await (await fetch(mail + '/api/v1/message/' + messages[0].ID)).json();
  const token = /#token=(i1_[A-Za-z0-9_-]{86})/.exec(Text)?.[1];
  expect(Boolean(token), 'mail includes a fixture invitation').toBe(true);
  return token!;
}

function fixturePassword() { return 'Fixture-' + randomBytes(20).toString('hex'); }

function otp(seed: string, time: number) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  const bits = [...seed].map(character => alphabet.indexOf(character).toString(2).padStart(5, '0')).join('');
  const key = Buffer.from(bits.match(/.{8}/g)!.map(byte => parseInt(byte, 2)));
  const step = Buffer.alloc(8);
  step.writeBigUInt64BE(BigInt(Math.floor(time / 30_000)));
  const mac = createHmac('sha1', key).update(step).digest();
  return String((mac.readUInt32BE(mac.at(-1)! & 15) & 0x7fffffff) % 1_000_000).padStart(6, '0');
}
