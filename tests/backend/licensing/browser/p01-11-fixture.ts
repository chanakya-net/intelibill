import { createHmac, randomBytes } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { expect, type Page } from '@playwright/test';

export const origin = process.env['P0111_ADMIN_WEB']!;
export const mail = process.env['P0111_MAILPIT']!;
export const proofFile = process.env['P0111_PROOF_FILE']!;
export const ownerEmail = 'p0111-owner@example.test';
export const operatorEmail = 'p0111-operator-canary@example.test';
export const signInContext = '/api/admin/auth/sign-in/context';
export const sessionPath = '/api/admin/auth/session';
export const refreshPath = '/api/admin/auth/refresh';
export const signOutPath = '/api/admin/auth/sign-out';
export const devicesPath = '/api/admin/sessions';
export const sessionCookies = /^InteliBill\.Admin\.(Access|Refresh)$/;
const bootstrap = '/api/admin/auth/bootstrap';

export async function createOwner(page: Page) {
  const password = fixturePassword();
  const proofToken = readFileSync(proofFile, 'utf8').trim();
  await setupRequest(page, bootstrap + '/context', bootstrap, { proofToken, password }, 200);
  const signIn = await finishEnrollment(page, ownerEmail, password);
  await signIn(page);
}

export async function createOperator(owner: Page, page: Page) {
  const invitations = '/api/admin/invitations';
  await setupRequest(owner, invitations + '/context', invitations, { email: operatorEmail }, 201);
  const { messages } = await (await fetch(mail + '/api/v1/messages')).json();
  expect(messages).toHaveLength(1);
  const { Text } = await (await fetch(mail + '/api/v1/message/' + messages[0].ID)).json();
  const invitationToken = /#token=(i1_[A-Za-z0-9_-]{86})/.exec(Text)?.[1];
  expect(Boolean(invitationToken), 'Mailpit captured the fixture invitation').toBe(true);
  const password = fixturePassword();
  const acceptance = '/api/admin/auth/invitation';
  await setupRequest(page, acceptance + '/context', acceptance + '/accept',
    { invitationToken, email: operatorEmail, password }, 200);
  expect((await page.request.get(origin + devicesPath)).status()).toBe(401);
  const signIn = await finishEnrollment(page, operatorEmail, password);
  await signIn(page);
  return signIn;
}

async function finishEnrollment(page: Page, email: string, password: string) {
  const mfa = bootstrap + '/enrollment/mfa';
  const setup = await setupRequest(page, mfa, mfa + '/start', {}, 200);
  const { setupSecret } = await setup.json();
  let step = Math.floor(Date.now() / 30_000);
  await setupRequest(page, mfa, mfa + '/verify', { otp: otp(setupSecret, step) }, 200);

  return async (target: Page) => {
    await target.goto(origin + '/auth/sign-in');
    await target.getByLabel('Email', { exact: true }).fill(email);
    step = Math.max(step + 1, Math.floor(Date.now() / 30_000));
    await expect.poll(() => Math.floor(Date.now() / 30_000) + 1, { timeout: 35_000 }).toBeGreaterThanOrEqual(step);
    await fillSecret(target, 'Password', password);
    await fillSecret(target, 'Authenticator code', otp(setupSecret, step));
    await target.getByRole('button', { name: 'Sign in', exact: true }).click();
    await expect(target).toHaveURL(origin + '/');
    await expect(target.getByRole('link', { name: 'Device sessions' })).toBeVisible();
  };
}

async function setupRequest(page: Page, context: string, path: string, data: object, status: number) {
  const source = await page.request.get(origin + context);
  expect(source.status()).toBe(200);
  const { requestToken } = await source.json();
  const response = await page.request.post(origin + path, {
    data, headers: { Origin: origin, 'X-Bootstrap-CSRF': requestToken },
  });
  expect(response.status()).toBe(status);
  return response;
}

export async function adminPost(page: Page, path: string, data?: object, mutationOrigin = origin) {
  const source = await page.request.get(origin + signInContext);
  expect(source.status()).toBe(200);
  const { requestToken } = await source.json();
  const response = await page.request.post(origin + path, {
    data, headers: { Origin: mutationOrigin, 'X-Admin-CSRF': requestToken },
  });
  expect(response.headers()['cache-control']).toBe('no-store');
  return response;
}

export async function currentSession(page: Page) {
  const response = await page.request.get(origin + sessionPath);
  expect(response.status()).toBe(200);
  return response.json() as Promise<{ sessionId: string; capabilities: string[] }>;
}

export function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((settled) => (resolve = settled));
  return { promise, resolve };
}

/** Calls the source API and controls only delivery of its response to the browser. */
export async function holdResponse(page: Page, path: string, cookieWriter = false) {
  const status = deferred<number>();
  const release = deferred<'lose' | 'deliver'>();
  const delivered = deferred<void>();
  await page.route((url) => url.pathname === path, async (route) => {
    try {
      const context = page.context();
      const cookies = cookieWriter ? (await context.cookies()).filter((cookie) => sessionCookies.test(cookie.name)) : [];
      const answer = await route.fetch();
      if (cookieWriter) {
        // Playwright applies Set-Cookie during route.fetch. Restore the jar until delivery.
        await context.clearCookies({ name: sessionCookies });
        await context.addCookies(cookies);
      }
      status.resolve(answer.status());
      if (await release.promise === 'lose') await route.abort('connectionreset');
      else await route.fulfill({ response: answer });
    } catch {
      // Navigation can cancel the protected request while the source response is held.
      status.resolve(0);
    } finally {
      delivered.resolve();
    }
  }, { times: 1 });
  return { status: status.promise, release: release.resolve, delivered: delivered.promise };
}

function fixturePassword() {
  return 'Fixture-' + randomBytes(20).toString('hex');
}

async function fillSecret(page: Page, label: string, value: string) {
  await page.getByLabel(label, { exact: true }).evaluate((input: HTMLInputElement, secret) => {
    input.value = secret;
  }, value);
}

function otp(seed: string, step: number) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  const bits = [...seed].map((character) => alphabet.indexOf(character).toString(2).padStart(5, '0')).join('');
  const key = Buffer.from(bits.match(/.{8}/g)!.map((byte) => parseInt(byte, 2)));
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(step));
  const mac = createHmac('sha1', key).update(counter).digest();
  return String((mac.readUInt32BE(mac.at(-1)! & 15) & 0x7fffffff) % 1_000_000).padStart(6, '0');
}
