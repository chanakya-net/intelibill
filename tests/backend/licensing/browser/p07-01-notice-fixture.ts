import { existsSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, type APIResponse, type Browser, type Page } from '@playwright/test';
import { fillSecret, otp } from './p01-12-fixture';

function required(name: string) {
  const value = process.env['P0701_' + name];
  if (!value) throw new Error('Missing P0701 variable ' + name);
  return value;
}

export const adminWeb = required('ADMIN_WEB');
export const merchantWeb = required('MERCHANT_WEB');
const scratch = required('TEMPORARY');
export const evidence = required('EVIDENCE_DIR');
export const cfg = JSON.parse(readFileSync(join(scratch, 'fixture.json'), 'utf8'));

export async function signInMerchant(browser: Browser, email: string) {
  const context = await browser.newContext();
  const page = await context.newPage();
  await page.goto(`${merchantWeb}/login`);
  await page.locator('#identifier').fill(email);
  await page.locator('#password').evaluate((input: HTMLInputElement, value: string) => {
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }, cfg.merchantPassword);
  const login = page.waitForResponse(r => r.url().includes('/api/auth/login') && r.request().method() === 'POST');
  await page.locator('button[type=submit]').click();
  expect((await login).status()).toBe(200);
  await expect(page).not.toHaveURL(/\/login/);
  return { context, page };
}

export async function signInAdmin(page: Page, email: string, seed: string, timeSeconds: number) {
  await page.goto(`${adminWeb}/auth/sign-in`);
  await page.getByLabel('Email', { exact: true }).fill(email);
  await fillSecret(page, 'Password', cfg.adminPassword);
  await fillSecret(page, 'Authenticator code', otp(seed, Math.floor(timeSeconds / 30)));
  const reply = page.waitForResponse(
    (r) => r.url().endsWith('/api/admin/auth/sign-in') && r.request().method() === 'POST',
  );
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  expect((await reply).status()).toBe(200);
  await expect(page).toHaveURL(`${adminWeb}/`);
}

export async function signalHost(reqName: string, ackName: string, body = 'go', timeoutMs = 20_000) {
  const ackPath = join(scratch, ackName);
  if (existsSync(ackPath)) rmSync(ackPath);
  writeFileSync(join(scratch, reqName), body);
  const started = Date.now();
  while (!existsSync(ackPath)) {
    if (Date.now() - started > timeoutMs) throw new Error(`Timed out waiting for ${ackName}`);
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
  const result = readFileSync(ackPath, 'utf8').trim();
  rmSync(ackPath);
  return result;
}

export async function settleFrames(page: Page) {
  await page.evaluate(() => new Promise<void>((r) => requestAnimationFrame(() => requestAnimationFrame(() => r()))));
}

export async function merchantAuthedFetch(page: Page, path: string): Promise<{ status: number; text: string }> {
  return page.evaluate(async (targetPath) => {
    const raw =
      localStorage.getItem('inventory.auth.session.local') ??
      sessionStorage.getItem('inventory.auth.session.temporary');
    const token = raw ? (JSON.parse(raw) as { accessToken?: string }).accessToken : undefined;
    const headers: Record<string, string> = {};
    if (token) headers['Authorization'] = `Bearer ${token}`;
    const res = await fetch(targetPath, { headers, credentials: 'include' });
    return { status: res.status, text: await res.text() };
  }, path);
}

type HeldPayload = { status: number; scope: string; threshold?: string; commercial?: boolean };

export async function createDelayedRouteGate(page: Page, pattern: string, inspect: (response: APIResponse) => Promise<HeldPayload>) {
  let accept!: (proof: HeldPayload) => void;
  let reject!: (error: unknown) => void;
  let release!: () => void;
  let markSettled!: (outcome: string) => void;
  const intercepted = new Promise<HeldPayload>((resolve, fail) => { accept = resolve; reject = fail; });
  const hold = new Promise<void>((resolve) => { release = resolve; });
  const settled = new Promise<string>((resolve) => { markSettled = resolve; });
  let count = 0;

  await page.route(pattern, async (route) => {
    if (count > 0) {
      await route.continue();
      return;
    }
    count++;
    try {
      const response = await route.fetch();
      const body = await response.body();
      accept(await inspect(response));
      await hold;
      await route.fulfill({ response, body });
      markSettled('fulfilled');
    } catch (error) {
      reject(error);
      markSettled('aborted');
    }
  });

  return { intercepted, settled, release, hits: () => count, cleanup: () => page.unroute(pattern) };
}

export async function inspectShop(response: APIResponse): Promise<HeldPayload> {
  expect(response.status()).toBe(200);
  const body = await response.json();
  expect(body.shopId).toBe(cfg.northShopId);
  expect(body.notices).toHaveLength(1);
  expect(body.notices[0].shopName).toBe('North Shop');
  expect(body.notices[0].threshold).toBe('PaidMinus7Days');
  return { status: response.status(), scope: 'North', threshold: body.notices[0].threshold };
}

export async function inspectBilling(response: APIResponse): Promise<HeldPayload> {
  expect(response.status()).toBe(200);
  const body = await response.json();
  expect(body.accountId).toBe(cfg.accountA);
  expect(body.notices[0].evidencedTermPricePaise).toBe(12000);
  return { status: response.status(), scope: 'billing account A', commercial: true };
}

export async function inspectProvider(response: APIResponse): Promise<HeldPayload> {
  expect(response.status()).toBe(200);
  const body = await response.json();
  expect(body.accountId).toBe(cfg.accountA);
  expect(body.commercialIncluded).toBe(true);
  expect(body.items.some((item: { commercial?: { evidencedTermPricePaise?: number } }) =>
    item.commercial?.evidencedTermPricePaise === 12000)).toBe(true);
  return { status: response.status(), scope: 'provider account A', commercial: true };
}
