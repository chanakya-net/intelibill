import { createHmac, randomBytes } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { expect, type Page } from '@playwright/test';

export const origin = process.env['P0112_ADMIN_WEB']!;
export const mail = process.env['P0112_MAILPIT']!;
export const proofFile = process.env['P0112_PROOF_FILE']!;
export const ownerEmail = 'p0112-owner@example.test';
export const operatorEmail = 'p0112-operator@example.test';
export const sessionPath = '/api/admin/auth/session';
export const refreshPath = '/api/admin/auth/refresh';
export const accessPath = '/api/admin/access';
const bootstrap = '/api/admin/auth/bootstrap';
const mfa = bootstrap + '/enrollment/mfa';

/** An enrolled administrator: password, authenticator seed and its last accepted TOTP step. */
export interface Enrolled {
  email: string;
  password: string;
  seed: string;
  step: number;
  codes: string[];
}

export async function createOwner(page: Page): Promise<Enrolled> {
  const password = fixturePassword();
  const proofToken = readFileSync(proofFile, 'utf8').trim();
  await setupRequest(page, bootstrap + '/context', bootstrap, { proofToken, password }, 200);
  return { email: ownerEmail, password, ...await enrollByApi(page) };
}

export async function createOperator(owner: Page, page: Page): Promise<Enrolled> {
  const invitations = '/api/admin/invitations';
  await setupRequest(owner, invitations + '/context', invitations, { email: operatorEmail }, 201);
  const { messages } = await (await fetch(mail + '/api/v1/messages')).json();
  const { Text } = await (await fetch(mail + '/api/v1/message/' + messages[0].ID)).json();
  const invitationToken = /#token=(i1_[A-Za-z0-9_-]{86})/.exec(Text)?.[1];
  expect(Boolean(invitationToken), 'Mailpit captured the fixture invitation').toBe(true);
  const password = fixturePassword();
  const acceptance = '/api/admin/auth/invitation';
  await setupRequest(page, acceptance + '/context', acceptance + '/accept',
    { invitationToken, email: operatorEmail, password }, 200);
  return { email: operatorEmail, password, ...await enrollByApi(page) };
}

async function enrollByApi(page: Page) {
  const setup = await setupRequest(page, mfa, mfa + '/start', {}, 200);
  const { setupSecret } = await setup.json();
  const step = Math.floor(Date.now() / 30_000);
  const verified = await setupRequest(page, mfa, mfa + '/verify', { otp: otp(setupSecret, step) }, 200);
  return { seed: setupSecret as string, step, codes: (await verified.json()).recoveryCodes as string[] };
}

/** Completes authenticator setup through the Provider Console page the recovery led to. */
export async function enrollInBrowser(page: Page, account: Enrolled): Promise<Enrolled> {
  await expect(page).toHaveURL(origin + '/auth/mfa-enrollment');
  const seed = (await page.getByTestId('setup-key').textContent())!.trim();
  expect(seed).not.toBe(account.seed);
  const step = Math.floor(Date.now() / 30_000);
  await fillSecret(page, 'Authenticator code', otp(seed, step));
  await page.getByRole('button', { name: 'Verify authenticator' }).click();
  const items = page.getByRole('list', { name: 'Recovery codes' }).getByRole('listitem');
  await expect(items).toHaveCount(10);
  const codes = await items.allTextContents();
  await page.getByRole('button', { name: 'I saved my codes' }).click();
  return { ...account, seed, step, codes: codes.map((code) => code.trim()) };
}

export async function signIn(page: Page, account: Enrolled): Promise<Enrolled> {
  await page.goto(origin + '/auth/sign-in');
  await page.getByLabel('Email', { exact: true }).fill(account.email);
  const step = Math.max(account.step + 1, Math.floor(Date.now() / 30_000));
  await expect.poll(() => Math.floor(Date.now() / 30_000) + 1, { timeout: 35_000 }).toBeGreaterThanOrEqual(step);
  await fillSecret(page, 'Password', account.password);
  await fillSecret(page, 'Authenticator code', otp(account.seed, step));
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(origin + '/');
  await expect(page.getByRole('link', { name: 'Device sessions' })).toBeVisible();
  return { ...account, step };
}

export async function adminPost(page: Page, path: string, data?: object) {
  const source = await page.request.get(origin + '/api/admin/auth/sign-in/context');
  expect(source.status()).toBe(200);
  const { requestToken } = await source.json();
  const response = await page.request.post(origin + path, {
    data, headers: { Origin: origin, 'X-Admin-CSRF': requestToken },
  });
  expect(response.headers()['cache-control']).toBe('no-store');
  return response;
}

export async function fillSecret(page: Page, label: string, value: string) {
  await page.getByLabel(label, { exact: true }).evaluate((input: HTMLInputElement, secret) => {
    input.value = secret;
  }, value);
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

function fixturePassword() {
  return 'Fixture-' + randomBytes(20).toString('hex');
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
