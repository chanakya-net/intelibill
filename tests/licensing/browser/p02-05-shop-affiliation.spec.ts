import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';

function setting(name: string): string {
  const value = process.env['P0205_' + name];
  if (!value) throw new Error('Missing P0205 fixture setting: ' + name);
  return value;
}

const web = new URL(setting('WEB'));
if (web.protocol !== 'http:' || web.hostname !== '127.0.0.1') throw new Error('Loopback fixture required.');
const shop = setting('SHOP');
const account = setting('ACCOUNT');
const evidence = setting('EVIDENCE');

async function signIn(page: Page, actor: 'PAYER' | 'OWNER') {
  await page.goto(web.origin + '/login');
  await page.locator('#identifier').fill(setting(actor));
  await page.locator('#password').fill(setting('PASSWORD'));
  const reply = await respond(page, '/api/auth/login', () => page.locator('button[type=submit]').click());
  expect(reply.status()).toBe(200);
  await expect(page).not.toHaveURL(/\/login/);
  return (await reply.json()).accessToken as string;
}

async function respond(page: Page, path: string, action: () => Promise<unknown>) {
  const response = page.waitForResponse(r => new URL(r.url()).pathname === path && r.request().method() === 'POST');
  await action();
  return response;
}

async function api(page: Page, token: string, path: string, body?: unknown) {
  return page.evaluate(async ({ token, path, body }) => {
    const response = await fetch(path, {
      headers: { Authorization: 'Bearer ' + token, ...(body ? { 'Content-Type': 'application/json' } : {}) },
      method: body ? 'POST' : 'GET',
      body: body ? JSON.stringify(body) : undefined,
      cache: 'no-store',
    });
    return { status: response.status, text: await response.text() };
  }, { token, path, body });
}

const accountApi = '/api/billing/accounts/' + account + '/affiliations';
const consentLink = (id: string) => '/billing/consent/' + account + '/' + shop + '/' + id;
const consentApi = (id: string) => accountApi + '/' + shop + '/' + id + '/consent';

async function request(page: Page, label: 'Grouped' | 'Affiliation without coverage') {
  await page.locator('#affiliation-shop').fill(shop);
  const term = page.locator('#affiliation-term option').filter({ hasText: label });
  await page.locator('#affiliation-term').selectOption(await term.getAttribute('value') ?? '');
  const response = await respond(page, accountApi, () => page.getByRole('button', { name: 'Request affiliation' }).click());
  expect(response.status()).toBe(201);
  expect(response.headers()['cache-control']).toContain('no-store');
  return (await response.json()).requestId as string;
}

async function grant(page: Page, id: string) {
  await page.goto(web.origin + consentLink(id));
  await expect(page.locator('dd').getByText('Fixture Retail Shop', { exact: true })).toBeVisible();
  await expect(page.locator('dd').getByText('Billing Fixture', { exact: true })).toBeVisible();
  await expect(page.getByText('Fixture Payer Account', { exact: true })).toBeVisible();
  const response = await respond(page, consentApi(id) + '/grant', () => page.getByRole('button', { name: 'Grant consent' }).click());
  expect(response.status()).toBe(200);
  await expect(page.getByText('Consent granted', { exact: true })).toBeVisible();
}

const retryAcceptance = 'Retry · Accept affiliation';

async function verifyConflictingTabClick(first: Page, second: Page, id: string) {
  await second.goto(web.origin + consentLink(id));
  await expect(second.getByText('Consent granted', { exact: true })).toBeVisible();
  const path = consentApi(id);
  let frozenKey: string | undefined;
  await first.route('**' + path + '/accept', async route => {
    frozenKey = route.request().postDataJSON().operationKey as string;
    await route.abort('failed');
  }, { times: 1 });
  await first.getByRole('button', { name: 'Accept affiliation', exact: true }).click();
  await expect(first.getByRole('button', { name: retryAcceptance, exact: true })).toBeEnabled();
  const sent: string[] = [];
  second.on('request', request => {
    const pathname = new URL(request.url()).pathname;
    if (request.method() === 'POST' && pathname.startsWith(path + '/')) sent.push(pathname);
  });
  await second.getByRole('button', { name: 'Revoke consent', exact: true }).click();
  await expect(second.getByRole('alert').filter({ hasText: 'Saved operations changed in another tab.' })).toBeVisible();
  await expect(second.getByText('Consent granted', { exact: true })).toBeVisible();
  await expect(second.getByRole('button', { name: retryAcceptance, exact: true })).toBeEnabled();
  expect(sent).toEqual([]);
  expect(frozenKey).toBeTruthy();
  return frozenKey;
}

async function loseAcknowledgementAndRecover(page: Page, id: string, frozenKey: string | undefined) {
  const path = consentApi(id) + '/accept';
  await page.route('**' + path, async route => {
    expect(route.request().postDataJSON().operationKey).toBe(frozenKey);
    const response = await route.fetch();
    expect(response.status()).toBe(200);
    await route.abort('failed');
  }, { times: 1 });
  await page.getByRole('button', { name: retryAcceptance, exact: true }).click();
  await expect(page.getByRole('button', { name: retryAcceptance, exact: true })).toBeEnabled();
  await page.reload();
  const retry = await respond(page, path, () => page.getByRole('button', { name: retryAcceptance, exact: true }).click());
  expect(retry.request().postDataJSON().operationKey).toBe(frozenKey);
  expect(retry.status()).toBe(200);
  return retry.status();
}

test('explicit coverage, named owner consent and payer shop isolation', async ({ browser }) => {
  const payerClient = await browser.newContext({ viewport: { width: 1366, height: 900 } });
  const ownerClient = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
  let boundaryLeak = false;
  for (const context of [payerClient, ownerClient]) {
    context.on('request', request => {
      const url = new URL(request.url());
      boundaryLeak ||= url.origin !== web.origin && url.hostname === '127.0.0.1';
      boundaryLeak ||= url.pathname.startsWith('/internal/licensing/') || 'x-billing-request-id' in request.headers();
    });
  }
  const payer = await payerClient.newPage();
  const owner = await ownerClient.newPage();
  const payerToken = await signIn(payer, 'PAYER');
  const ownerToken = await signIn(owner, 'OWNER');
  const before = await api(payer, payerToken, '/api/shops/me');
  await payer.getByRole('dialog').getByRole('button', { name: 'Billing accounts' }).click();
  const accountRow = payer.getByTestId('billing-account-row').filter({ hasText: 'Fixture Payer Account' });
  await accountRow.getByRole('link', { name: 'Shop coverage' }).click();
  await expect(payer.locator('#affiliation-term option').filter({ hasText: 'Independent' })).toHaveCount(2);
  const independentBefore = await payer.locator('#affiliation-term option').filter({ hasText: 'Independent' }).allTextContents();
  expect(independentBefore).toHaveLength(2);
  expect(independentBefore[0]).not.toBe(independentBefore[1]);
  expect(independentBefore.every(option => option.includes('IST'))).toBe(true);

  const revoked = await request(payer, 'Affiliation without coverage');
  await grant(owner, revoked);
  await owner.getByRole('button', { name: 'Revoke consent' }).click();
  await expect(owner.getByText('Consent revoked', { exact: true })).toBeVisible();
  const replay = await api(owner, ownerToken, consentApi(revoked) + '/accept', { operationKey: randomUUID() });
  expect(replay.status).toBe(409);

  const accepted = await request(payer, 'Grouped');
  await grant(owner, accepted);
  await expect(owner.getByText(/Grouped.*IST/)).toBeVisible();
  await owner.reload();
  await expect(owner.getByText('Consent granted', { exact: true })).toBeVisible();
  await owner.screenshot({ path: join(evidence, 'owner-consent-mobile.png') });
  const otherOwnerTab = await ownerClient.newPage();
  const frozenKey = await verifyConflictingTabClick(owner, otherOwnerTab, accepted);
  const retriedAcceptance = await loseAcknowledgementAndRecover(otherOwnerTab, accepted, frozenKey);
  await otherOwnerTab.close();
  const requestRow = payer.getByTestId('affiliation-request').and(payer.locator('[data-request-id="' + accepted + '"]'));
  await expect(async () => {
    await payer.getByRole('button', { name: 'Refresh status' }).click();
    await expect(requestRow.getByText('Affiliated', { exact: true })).toBeVisible({ timeout: 1500 });
  }).toPass({ timeout: 15_000 });
  await expect(requestRow).toContainText('Fixture Retail Shop');
  await expect(requestRow).toContainText('Billing Fixture');
  expect(await payer.locator('#affiliation-term option').filter({ hasText: 'Independent' }).allTextContents()).toEqual(independentBefore);
  await payer.screenshot({ path: join(evidence, 'applied-affiliation-desktop.png') });

  const readShop = await api(payer, payerToken, '/api/shops/' + shop);
  const switchShop = await api(payer, payerToken, '/api/shops/switch', { shopId: shop });
  const ownerRoute = await api(payer, payerToken, consentApi(accepted));
  expect(readShop.status).toBeGreaterThanOrEqual(400);
  expect(switchShop.status).toBeGreaterThanOrEqual(400);
  expect(ownerRoute.status).toBe(403);
  expect(await api(payer, payerToken, '/api/shops/me')).toEqual(before);
  expect(boundaryLeak).toBe(false);

  writeFileSync(join(evidence, 'observations.json'), JSON.stringify({
    coverage: 'Grouped, exact existing terms shown in IST', independentTermsUnchanged: true,
    liveOwner: 'Billing Fixture', namedShop: 'Fixture Retail Shop', affiliation: 'Applied',
    conflictingRevokeSentNoRequest: true, retryLabel: retryAcceptance,
    revokedAcceptance: replay.status, retriedAcceptance, sameOperationKey: true,
    payerShopDetails: readShop.status, payerSwitch: switchShop.status, payerConsent: ownerRoute.status,
    payerShopListUnchanged: true, boundaryLeak,
  }, null, 2));
});
