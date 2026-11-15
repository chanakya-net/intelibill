import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { expect, test, type Browser, type BrowserContext, type Page, type Response } from '@playwright/test';

function setting(name: string): string {
  const value = process.env['P0204_' + name];
  if (!value) throw new Error('Missing local fixture setting ' + name);
  return value;
}

function loopback(name: string): string {
  const url = new URL(setting(name));
  if (url.protocol !== 'http:' || url.hostname !== '127.0.0.1') throw new Error('Loopback fixture required.');
  return url.origin;
}

async function signIn(page: Page, actor: string) {
  await page.goto(loopback('WEB') + '/login');
  await page.locator('#identifier').fill(setting(actor));
  await page.locator('#password').fill(setting('PASSWORD'));
  const response = await respond(page, '/api/auth/login', () => page.locator('button[type=submit]').click());
  expect(response.status()).toBe(200);
  return (await response.json()).accessToken as string;
}

async function respond(page: Page, path: string, action: () => Promise<unknown>, method = 'POST'): Promise<Response> {
  const reply = page.waitForResponse(r => new URL(r.url()).pathname === path && r.request().method() === method);
  await action();
  return reply;
}

async function api(page: Page, token: string, path: string, body?: unknown) {
  return page.evaluate(async ({ token, path, body }) => {
    const response = await fetch(path, {
      headers: { Authorization: 'Bearer ' + token, ...(body ? { 'Content-Type': 'application/json' } : {}) },
      method: body ? 'POST' : 'GET',
      body: body ? JSON.stringify(body) : undefined,
    });
    return { status: response.status, text: await response.text(), cache: response.headers.get('cache-control') };
  }, { token, path, body });
}

function isDelegation(header: string | undefined) {
  try {
    const value = header?.replace(/^Bearer /, '').split('.')[0] ?? '';
    return JSON.parse(Buffer.from(value, 'base64url').toString()).typ === 'intelibill-merchant-billing-delegation+jwt';
  } catch {
    return false;
  }
}

type Observe = (context: BrowserContext) => void;

function watchBoundary(licensing: string) {
  let leaked = false;
  const observe = (context: BrowserContext) => {
    context.on('request', request => {
      const headers = request.headers();
      const url = new URL(request.url());
      leaked ||= url.origin === licensing || url.pathname.startsWith('/internal/licensing/') ||
        'x-billing-request-id' in headers || isDelegation(headers['authorization']);
      if (url.pathname.endsWith('/accept')) leaked ||= 'token' in (request.postDataJSON() ?? {});
    });
    context.on('response', response => {
      leaked ||= 'x-billing-request-id' in response.headers();
    });
  };
  return { observe, leaked: () => leaked };
}

const accountName = 'Local Ownership Transfer';

async function ownerIssues(browser: Browser, observe: Observe) {
  const context = await browser.newContext({ viewport: { width: 1366, height: 900 } });
  observe(context);
  const page = await context.newPage();
  const token = await signIn(page, 'OWNER');
  await page.goto(loopback('WEB') + '/billing');
  await page.getByTestId('billing-account-name').fill(accountName);
  const created = await respond(page, '/api/billing/accounts', () => page.getByTestId('billing-account-create').click());
  expect(created.status()).toBe(201);
  const id = (await created.json()).accountId as string;
  const account = `/api/billing/accounts/${id}`;
  await page.getByTestId('billing-members-link').click();
  await page.getByTestId('billing-transfer-link').click();
  await page.getByTestId('billing-transfer-recipient').fill(setting('RECIPIENT'));
  await page.getByTestId('billing-transfer-reason').fill('Fixture ownership handover');
  await page.getByTestId('billing-transfer-review').click();
  const preview = page.getByTestId('billing-transfer-preview');
  await expect(preview).toContainText(accountName);
  await expect(preview).toContainText(setting('RECIPIENT'));
  await expect(preview).toContainText('Reason for transfer · Fixture ownership handover');
  await expect(preview).toContainText('Shop roles and access stay the same');
  await expect(preview).toContainText('Existing financial obligations');
  await expect(page.getByTestId('billing-transfer-consent')).not.toBeChecked();
  await page.getByTestId('billing-transfer-consent').check();
  const response = await respond(page, account + '/ownership-transfers', () => page.getByTestId('billing-transfer-confirm').click());
  expect(response.status()).toBe(202);
  expect(response.headers()['cache-control']).toContain('no-store');
  await expect(page.getByTestId('billing-transfer-result')).toContainText('Ownership changes only after');
  const roster = JSON.parse((await api(page, token, account + '/members')).text);
  expect(roster.members).toHaveLength(1);
  expect(roster.members[0].role).toBe('Owner');
  return { page, token, account, membersPath: `/billing/accounts/${id}/members`, status: response.status() };
}

async function recipientReviews(browser: Browser, observe: Observe) {
  const context = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
  observe(context);
  const page = await context.newPage();
  const token = await signIn(page, 'RECIPIENT');
  const shops = await api(page, token, '/api/shops/me');
  expect(shops.status).toBe(200);
  await page.goto(loopback('WEB') + '/billing');
  await page.getByTestId('billing-inbox-link').click();
  await page.getByTestId('billing-transfer-inbox-link').click();
  await expect(page.getByTestId('billing-transfer-inbox-row')).toHaveCount(1);
  await page.getByTestId('billing-transfer-inbox-review').click();
  const preview = page.getByTestId('billing-transfer-preview');
  await expect(preview).toContainText(accountName);
  await expect(preview).toContainText('Requested by billing owner');
  await expect(preview).toContainText(/Accept before .* IST/);
  await expect(preview).toContainText('Existing financial obligations');
  await expect(preview).toContainText('Shop roles and access stay the same');
  await expect(page.getByTestId('billing-transfer-consent')).not.toBeChecked();
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(overflow).toBeLessThanOrEqual(1);
  const path = '/api' + new URL(page.url()).pathname;
  return { page, token, path, shops: shops.text, overflow };
}

async function denyStranger(browser: Browser, observe: Observe, path: string) {
  const context = await browser.newContext();
  observe(context);
  const page = await context.newPage();
  const token = await signIn(page, 'STRANGER');
  await page.goto(loopback('WEB') + '/billing/ownership-transfers');
  await expect(page.getByTestId('billing-transfer-inbox-empty')).toBeVisible();
  expect((await api(page, token, path)).status).toBe(404);
  const denied = await api(page, token, path + '/accept', {
    operationKey: randomUUID(), expectedVersion: 1, responsibilityPolicyVersion: 'prospective-billing-owner/1',
    acceptResponsibility: true,
  });
  expect(denied.status).toBe(404);
  return denied.status;
}

async function acceptAndRecover(page: Page, path: string) {
  let status = 0;
  let operationKey = '';
  await page.route('**' + path + '/accept', async route => {
    operationKey = route.request().postDataJSON().operationKey as string;
    const result = await route.fetch();
    status = result.status();
    expect(result.headers()['cache-control']).toContain('no-store');
    expect((await result.json()).operationKey).toBe(operationKey);
    await route.abort('failed');
  }, { times: 1 });

  await page.getByTestId('billing-transfer-consent').check();
  await page.getByTestId('billing-transfer-confirm').click();
  await expect(page.getByRole('alert')).toContainText('outcome is unavailable');
  expect(status).toBe(200);
  await page.reload();
  const recovery = await respond(page, '/api/billing/ownership-transfer-operations/accept/' + operationKey,
    () => page.getByTestId('billing-transfer-lookup').click(), 'GET');
  expect(recovery.status()).toBe(200);
  expect(recovery.headers()['cache-control']).toContain('no-store');
  expect((await recovery.json()).operationKey).toBe(operationKey);
  await expect(page.getByTestId('billing-transfer-result')).toContainText('You accepted billing ownership');
  await page.getByTestId('billing-transfer-roster').click();
  await expect(page.getByTestId('billing-member-row').filter({ hasText: 'You' })).toContainText('Owner');
  return { accept: status, recovered: recovery.status() };
}

test('two verified actors explicitly transfer commercial responsibility', async ({ browser }) => {
  const watch = watchBoundary(loopback('LICENSING'));
  const owner = await ownerIssues(browser, watch.observe);
  const recipient = await recipientReviews(browser, watch.observe);
  const stranger = await denyStranger(browser, watch.observe, recipient.path);
  const acceptance = await acceptAndRecover(recipient.page, recipient.path);

  await owner.page.goto(loopback('WEB') + owner.membersPath);
  await expect(owner.page.getByTestId('billing-member-row').filter({ hasText: 'You' })).toContainText('Manager');
  await expect(owner.page.getByTestId('billing-transfer-link')).toHaveCount(0);
  const denied = await api(owner.page, owner.token, owner.account + '/ownership-transfers', {
    operationKey: randomUUID(), expectedVersion: 2, recipientKind: 'email', recipient: setting('STRANGER'),
    reason: 'Former owner authority check', confirmTransfer: true,
  });
  expect(denied.status).toBe(403);
  const after = await api(recipient.page, recipient.token, '/api/shops/me');
  expect(after.status).toBe(200);
  expect(after.text === recipient.shops).toBe(true);
  const foreign = await api(recipient.page, recipient.token, '/api/shops/' + setting('OWNER_SHOP'));
  expect([403, 404]).toContain(foreign.status);
  expect(watch.leaked()).toBe(false);
  writeFileSync(join(setting('EVIDENCE'), 'observations.json'), JSON.stringify({
    issue: owner.status, ...acceptance, stranger, formerOwnerCanInitiate: denied.status,
    verifiedParties: ['issuing owner', 'named recipient'], responsibilityPreview: true, explicitCheckboxes: true,
    ownerBeforeAcceptance: 'Owner', priorOwnerAfter: 'Manager', recipientAfter: 'Owner',
    exactKeyRecoveredAfterLostResponse: true, shopAccessUnchanged: true, foreignShop: foreign.status,
    narrowViewportOverflowPx: recipient.overflow, browserPrivateDelegationObserved: false,
  }, null, 2) + '\n');
});
