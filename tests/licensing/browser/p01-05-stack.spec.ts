import { expect, test, type APIRequestContext, type Page } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';

/**
 * P01-05 browser acceptance against a real, isolated full stack (see playwright.config.ts).
 *
 * P0105_PHASE=enroll   enrol a fresh merchant through the web UI and the stack's Mailpit,
 *                      finish sign-up, create one shop through the real API.
 * P0105_PHASE=restart  after the harness has taken the stack down and up again, the same
 *                      person signs in through the UI and still owns exactly that shop.
 * Both phases then open the separate admin origin in the same browser context.
 *
 * Identifiers go to browser-state.json (evidence); the generated fixture credentials go to a
 * dot-file beside it and are never printed.
 */
const phase = process.env['P0105_PHASE'] ?? 'enroll';
const merchant = required('P0105_MERCHANT_WEB');
const admin = required('P0105_ADMIN_WEB');
const mailpit = required('P0105_MAILPIT');
const artifacts = required('P0105_ARTIFACTS');
const credentialsFile = `${artifacts}/.fixture-credentials.json`;
const stateFile = `${artifacts}/browser-state.json`;

function required(name: string): string {
  const value = process.env[name];
  if (!value) throw new Error(`${name} is required; run scripts/dev/test-licensing-stack.sh --case browser`);
  return value.replace(/\/$/, '');
}

interface Credentials {
  email: string;
  password: string;
}

async function verificationLink(request: APIRequestContext, email: string): Promise<string> {
  for (let attempt = 0; attempt < 30; attempt++) {
    const search = await request.get(`${mailpit}/api/v1/search`, { params: { query: `to:"${email}"` } });
    const { messages } = (await search.json()) as { messages: { ID: string }[] };
    if (messages.length > 0) {
      const message = await (await request.get(`${mailpit}/api/v1/message/${messages[0].ID}`)).json();
      const link = /https?:\/\/[^\s"'<>]+\/auth\/verify-email#[^\s"'<>]+/.exec(`${message.Text}\n${message.HTML}`);
      if (link) return link[0].replace(/&amp;/g, '&');
    }
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
  throw new Error('No verification message reached the stack Mailpit');
}

async function apiSession(request: APIRequestContext, credentials: Credentials) {
  const login = await request.post(`${merchant}/api/auth/login/email`, { data: credentials });
  expect(login.status()).toBe(200);
  const auth = (await login.json()) as { accessToken: string; user: { id: string } };
  return { headers: { Authorization: `Bearer ${auth.accessToken}` }, userId: auth.user.id };
}

async function expectSignedIn(page: Page) {
  await expect(page).not.toHaveURL(/\/(login|register|auth\/)/, { timeout: 30_000 });
}

async function enroll(page: Page, request: APIRequestContext): Promise<Credentials> {
  const credentials = {
    email: `p0105.${randomBytes(4).toString('hex')}@intelibill.test`,
    password: `Fixture-${randomBytes(9).toString('base64url')}9a`,
  };
  await page.goto(`${merchant}/register`);
  await page.locator('#email').fill(credentials.email);
  await page.locator('button[type=submit]').click();
  await page.goto(await verificationLink(request, credentials.email));
  await page.locator('#first-name').fill('Fixture');
  await page.locator('#last-name').fill('Merchant');
  await page.locator('#chosen-password').fill(credentials.password);
  await page.locator('#confirm-password').fill(credentials.password);
  await page.locator('button[type=submit]').click();
  await expectSignedIn(page);
  await page.screenshot({ path: `${artifacts}/merchant-enrolled.png`, fullPage: true });
  return credentials;
}

async function signIn(page: Page, credentials: Credentials) {
  await page.goto(`${merchant}/login`);
  await page.locator('#identifier').fill(credentials.email);
  await page.locator('#password').fill(credentials.password);
  await page.locator('button[type=submit]').click();
  await expectSignedIn(page);
  await page.screenshot({ path: `${artifacts}/merchant-signed-in-after-restart.png`, fullPage: true });
}

async function myShops(request: APIRequestContext, headers: Record<string, string>) {
  const response = await request.get(`${merchant}/api/shops/me`, { headers });
  expect(response.status()).toBe(200);
  return (await response.json()) as { shopId?: string; id?: string; shopName?: string; name?: string }[];
}

const shopIdOf = (shop: { shopId?: string; id?: string }) => shop.shopId ?? shop.id ?? '';

test('merchant web: enrolment through Mailpit, sign-in and shop persistence on real PostgreSQL', async ({
  page,
  request,
}) => {
  if (phase === 'enroll') {
    const credentials = await enroll(page, request);
    const session = await apiSession(request, credentials);
    const created = await request.post(`${merchant}/api/shops`, {
      headers: session.headers,
      data: { name: 'P01-05 Fixture Shop', address: '1 Test Road', city: 'Pune', state: 'Maharashtra', pincode: '411001' },
    });
    expect(created.status()).toBe(200);
    // Creating a shop changes the caller's authority, so the answer carries the new scoped token.
    const scope = (await created.json()) as { accessToken: string; activeShopId: string };
    const shops = await myShops(request, { Authorization: `Bearer ${scope.accessToken}` });
    expect(shops).toHaveLength(1);
    expect(shopIdOf(shops[0])).toBe(scope.activeShopId);
    // One tenant-scoped (row-level secured) record, so the harness can show the runtime role
    // sees it only under that shop's scope.
    const customer = await request.post(`${merchant}/api/customers`, {
      headers: { Authorization: `Bearer ${scope.accessToken}` },
      data: { name: 'P01-05 Fixture Customer', phoneNumber: '9876543210', address: null, isActive: true },
    });
    expect([200, 201]).toContain(customer.status());
    await writeFile(credentialsFile, JSON.stringify(credentials), { mode: 0o600 });
    await writeFile(stateFile, JSON.stringify({ userId: session.userId, shopId: shopIdOf(shops[0]) }, null, 1));
    return;
  }
  const credentials = JSON.parse(await readFile(credentialsFile, 'utf8')) as Credentials;
  const state = JSON.parse(await readFile(stateFile, 'utf8')) as { userId: string; shopId: string };
  await signIn(page, credentials);
  const session = await apiSession(request, credentials);
  expect(session.userId).toBe(state.userId);
  const shops = await myShops(request, session.headers);
  expect(shops.map(shopIdOf)).toEqual([state.shopId]);
});

test('admin web: separate origin denies provider access to a signed-in merchant browser', async ({ page }) => {
  const credentials = JSON.parse(await readFile(credentialsFile, 'utf8')) as Credentials;
  await signIn(page, credentials);
  const merchantKeys = await page.evaluate(() => Object.keys(localStorage).length);
  expect(merchantKeys).toBeGreaterThan(0);

  const adminPaths: string[] = [];
  page.on('request', (request) => {
    if (request.url().startsWith(admin)) adminPaths.push(new URL(request.url()).pathname);
  });
  await page.goto(`${admin}/`);
  await expect(page.getByRole('heading', { name: 'Provider Console' })).toBeVisible();
  await expect(page.getByRole('alert')).toHaveText('No provider session');
  await expect(page.getByText('No provider permissions')).toBeVisible();
  await expect(page.locator('.ib-form-section')).toHaveCSS('display', 'grid');

  const hideHelp = page.getByRole('button', { name: 'Hide access help' });
  await hideHelp.click();
  const prompt = page.getByRole('alertdialog', { name: 'Hide access help?' });
  await expect(prompt).toBeVisible();
  await page.screenshot({ path: `${artifacts}/admin-denied-${phase}.png`, fullPage: true });
  await prompt.getByRole('button', { name: 'Cancel' }).click();
  await expect(hideHelp).toBeVisible();

  expect(adminPaths.filter((path) => /^\/(api|hubs|ngsw)([/.-]|$)/.test(path))).toEqual([]);
  expect(await page.evaluate(() => Object.keys(localStorage).length)).toBe(0);
  expect(await page.evaluate(async () => (await navigator.serviceWorker.getRegistrations()).length)).toBe(0);
  const borrowed = await page.evaluate(async () => {
    const response = await fetch('/api/shops/me', { credentials: 'include' });
    return { status: response.status, cache: response.headers.get('cache-control') };
  });
  expect(borrowed).toEqual({ status: 404, cache: 'no-store' });
});
