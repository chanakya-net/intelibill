import { createHmac, randomBytes } from 'node:crypto';
import { appendFileSync } from 'node:fs';
import { expect, type Page } from '@playwright/test';
import { fillSecret } from './p01-12-fixture';

export const origin = loopback('P0115_ADMIN_WEB');
export const scratch = required('P0115_SCRATCH');
export const session = '/api/admin/auth/session';
export const refresh = '/api/admin/auth/refresh';
const access = '/api/admin/access';
const mfa = '/api/admin/auth/bootstrap/enrollment/mfa';

export interface Account {
  email: string;
  password: string;
  seed: string;
  step: number;
}

export function remember(value: string) {
  if (value.length > 0) appendFileSync(scratch + '/secrets', value + '\n', { mode: 0o600 });
  return value;
}

export function password() {
  return remember('Fixture-' + randomBytes(20).toString('hex'));
}

export class SetupUnavailable extends Error {}

export async function enroll(page: Page, email: string, password: string) {
  expect((await page.request.get(origin + access)).status()).toBe(403);
  const pending = (await page.context().cookies()).find(cookie => cookie.name === 'Intelibill.Admin.BootstrapEnrollment');
  expect(Boolean(pending?.httpOnly && pending.sameSite === 'Strict')).toBe(true);
  remember(pending!.value);
  const started = page.waitForResponse(result => new URL(result.url()).pathname === mfa + '/start');
  await page.getByRole('link', { name: 'Set up authenticator' }).click();
  const response = await started;
  if (response.status() !== 200) {
    const { code } = await response.json();
    const safeCode = code === 'Mfa.KeyUnavailable' ? code : 'unexpected error';
    throw new SetupUnavailable('MFA start expected HTTP 200, observed ' + response.status() + ' (' + safeCode + ')');
  }
  const key = page.getByTestId('setup-key');
  await expect(key).toBeVisible();
  const seed = remember((await key.textContent())!.trim());
  const step = currentStep();
  await fillSecret(page, 'Authenticator code', remember(otp(seed, step)));
  await page.getByRole('button', { name: 'Verify authenticator' }).click();
  await expect(page.getByRole('heading', { name: 'Authenticator enrolled' })).toBeVisible();
  const codes = page.getByRole('list', { name: 'Recovery codes' }).getByRole('listitem');
  await expect(codes).toHaveCount(10);
  for (const code of await codes.allTextContents()) remember(code.trim());
  expect((await page.request.get(origin + session)).status()).toBe(401);
  await page.getByRole('button', { name: 'I saved my codes' }).click();
  await expect(page.getByRole('list', { name: 'Recovery codes' })).toHaveCount(0);
  return { email, password, seed, step };
}

export async function signIn(page: Page, account: Account) {
  await page.goto(origin + '/auth/sign-in');
  await page.getByLabel('Email', { exact: true }).fill(account.email);
  const step = Math.max(account.step + 1, currentStep());
  await expect.poll(() => currentStep() + 1, { timeout: 35_000 }).toBeGreaterThanOrEqual(step);
  await fillSecret(page, 'Password', account.password);
  await fillSecret(page, 'Authenticator code', remember(otp(account.seed, step)));
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(origin + '/');
  await expect(page.getByRole('heading', { name: 'Granted capabilities' })).toBeVisible();
  account.step = step;
}

function currentStep() { return Math.floor(Date.now() / 30_000); }

function otp(seed: string, step: number) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  const bits = [...seed].map(character => alphabet.indexOf(character).toString(2).padStart(5, '0')).join('');
  const key = Buffer.from(bits.match(/.{8}/g)!.map(byte => parseInt(byte, 2)));
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(step));
  const mac = createHmac('sha1', key).update(counter).digest();
  return String((mac.readUInt32BE(mac.at(-1)! & 15) & 0x7fffffff) % 1_000_000).padStart(6, '0');
}

export function required(name: string) {
  const value = process.env[name];
  if (!value) throw new Error(name + ' is required');
  return value;
}

export function loopback(name: string) {
  const url = new URL(required(name));
  if (url.protocol !== 'http:' || url.hostname !== 'localhost' || !url.port || url.href !== url.origin + '/')
    throw new Error(name + ' must be a local Compose HTTP origin');
  return url.origin;
}

export async function readSession(page: Page) {
  const response = await page.request.get(origin + session);
  expect(response.status()).toBe(200);
  expect(response.headers()['cache-control']).toBe('no-store');
  return await response.json() as { sessionId: string; capabilities: string[] };
}

export async function contextProof(page: Page) {
  const response = await page.request.get(origin + '/api/admin/auth/sign-in/context');
  expect(response.status()).toBe(200);
  return remember((await response.json()).requestToken);
}

export async function postRefresh(page: Page, token?: string, requestOrigin = origin) {
  const response = await page.request.post(origin + refresh, {
    headers: token ? { Origin: requestOrigin, 'X-Admin-CSRF': token } : { Origin: requestOrigin },
  });
  expect(response.headers()['cache-control']).toBe('no-store');
  return response.status();
}

export async function privateCookies(page: Page) {
  const cookies = (await page.context().cookies()).filter(cookie => cookie.name.startsWith('InteliBill.Admin.'));
  for (const cookie of cookies) remember(cookie.value);
  const protectedCookies = cookies.filter(cookie => /\.(Access|Refresh)$/.test(cookie.name));
  expect(protectedCookies.length).toBe(2);
  expect(protectedCookies.every(cookie => cookie.httpOnly && cookie.sameSite === 'Strict')).toBe(true);
  return protectedCookies;
}

export async function checkAdminStorage(page: Page) {
  const clean = await page.evaluate(async () => {
    const key = 'intelibill.admin.session-coordination.v1';
    const keys = Object.keys(localStorage);
    const coordination = JSON.parse(localStorage.getItem(key) ?? '{}');
    return keys.every(value => value === key)
      && Object.keys(coordination).every(value => ['generation', 'state', 'confirmed'].includes(value))
      && sessionStorage.length === 0 && document.cookie.length === 0
      && (await navigator.serviceWorker.getRegistrations()).length === 0
      && (await indexedDB.databases()).length === 0;
  });
  expect(clean).toBe(true);
}
