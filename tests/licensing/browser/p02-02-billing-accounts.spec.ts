import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type BrowserContext, type Page, type Response } from '@playwright/test';

// Local fixture verification only: loopback hosts, disposable databases, a local signing key.
function setting(name: string): string {
  const value = process.env['P0202_' + name];
  if (!value) throw new Error('Missing local fixture setting ' + name);
  return value;
}

function loopback(name: string): string {
  const url = new URL(setting(name));
  if (url.protocol !== 'http:' || url.hostname !== '127.0.0.1') {
    throw new Error('Browser fixture accepts loopback HTTP only.');
  }
  return url.origin;
}

interface PageResponse {
  status: number;
  noStore: boolean;
  type: string;
  body: string;
}

async function pageCall(page: Page, method: string, path: string, token: string, body?: unknown): Promise<PageResponse> {
  return page.evaluate(
    async ({ method, path, token, body }) => {
      const headers: Record<string, string> = { Authorization: 'Bearer ' + token };
      if (body !== undefined) headers['Content-Type'] = 'application/json';
      const response = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
      return {
        status: response.status,
        noStore: response.headers.get('cache-control')?.includes('no-store') ?? false,
        type: response.headers.get('content-type') ?? '',
        body: await response.text(),
      };
    },
    { method, path, token, body },
  );
}

function hasDelegation(value: string): boolean {
  return (value.match(/[\w-]+\.[\w-]+\.[\w-]+/g) ?? []).some((candidate) => {
    try {
      const typ = JSON.parse(Buffer.from(candidate.split('.')[0], 'base64url').toString()).typ;
      return typ === 'intelibill-merchant-billing-delegation+jwt';
    } catch {
      return false;
    }
  });
}

class BoundaryWatch {
  readonly destinations = new Set<string>();
  leaked = false;
  billingPosts = 0;

  attach(context: BrowserContext): void {
    context.on('request', (request) => {
      const url = new URL(request.url());
      this.destinations.add(url.origin);
      if (request.method() === 'POST' && url.pathname === '/api/billing/accounts') this.billingPosts++;
      const headers = request.headers();
      this.leaked ||= hasDelegation(headers['authorization'] ?? '') || hasDelegation(headers['cookie'] ?? '');
      this.leaked ||= 'x-billing-request-id' in headers;
    });
    context.on('response', (response) => {
      const headers = response.headers();
      this.leaked ||= 'x-billing-request-id' in headers || hasDelegation(JSON.stringify(headers));
    });
  }
}

async function signIn(page: Page, merchant: string, email: string): Promise<string> {
  await page.goto(merchant + '/login');
  await page.locator('#identifier').fill(email);
  await page.locator('#password').fill(setting('PASSWORD'));
  const login = page.waitForResponse((response) => new URL(response.url()).pathname === '/api/auth/login');
  await page.locator('button[type=submit]').click();
  const answer = await login;
  expect(answer.status()).toBe(200);
  return (await answer.json()).accessToken as string;
}

function isList(response: Response): boolean {
  return new URL(response.url()).pathname === '/api/billing/accounts' && response.request().method() === 'GET';
}

/** Submit the visible form and return the merchant's answer to that one explicit creation. */
async function createThroughForm(page: Page, name: string): Promise<Response> {
  const answer = page.waitForResponse(
    (r) => r.request().method() === 'POST' && new URL(r.url()).pathname === '/api/billing/accounts',
  );
  await page.getByTestId('billing-account-name').fill(name);
  await page.getByTestId('billing-account-create').click();
  return answer;
}

async function storageHasDelegation(page: Page, context: BrowserContext): Promise<boolean> {
  const storage = await page.evaluate(() => JSON.stringify([localStorage, sessionStorage, document.cookie]));
  return hasDelegation(storage) || (await context.cookies()).some((cookie) => hasDelegation(cookie.value));
}

test('a verified merchant explicitly creates and lists billing accounts it alone owns', async ({ browser }) => {
  const merchant = loopback('MERCHANT_WEB');
  const licensing = loopback('LICENSING_API');
  const watch = new BoundaryWatch();
  let delegatedCalls = 0;

  // User A on a desktop viewport.
  const contextA = await browser.newContext({ viewport: { width: 1366, height: 900 } });
  watch.attach(contextA);
  const page = await contextA.newPage();
  const tokenA = await signIn(page, merchant, setting('EMAIL_A'));
  const shopsBefore = await pageCall(page, 'GET', '/api/shops/me', tokenA);
  expect(shopsBefore.status).toBe(200);

  const firstList = page.waitForResponse(isList);
  await page.locator('.sidebar-profile .shell-panelmenu-header-link').first().click();
  await page.locator('.sidebar-profile').getByText(setting('MENU_LABEL'), { exact: true }).click();
  await expect(page).toHaveURL(/\/billing$/);
  expect((await firstList).status()).toBe(200);
  delegatedCalls++;
  await expect(page.getByTestId('billing-accounts-empty')).toBeVisible();
  expect(watch.billingPosts).toBe(0);

  // 1. Explicit creation through the form.
  const createdResponse = await createThroughForm(page, 'Acme Billing');
  delegatedCalls++;
  expect(createdResponse.status()).toBe(201);
  expect(createdResponse.headers()['cache-control']).toContain('no-store');
  const createdBody = createdResponse.request().postDataJSON() as { operationKey: string; name: string };
  const first = await createdResponse.json();
  expect(first).toMatchObject({ name: 'Acme Billing', role: 'Owner' });
  await expect(page.getByTestId('billing-account-row')).toHaveCount(1);
  await expect(page.getByTestId('billing-account-row').first()).toContainText('Acme Billing');

  // 2. Retry with the same actor-bound key answers the same account; another name conflicts.
  const retry = await pageCall(page, 'POST', '/api/billing/accounts', tokenA, createdBody);
  delegatedCalls++;
  expect(retry.status).toBe(200);
  expect(JSON.parse(retry.body)).toEqual(first);
  const conflict = await pageCall(page, 'POST', '/api/billing/accounts', tokenA, { ...createdBody, name: 'Other' });
  delegatedCalls++;
  expect(conflict.status).toBe(409);
  expect(JSON.parse(conflict.body).code).toBe('Billing.OperationPayloadMismatch');

  // 3. The same name again is a second, separate account; reload reads both from the server.
  const secondResponse = await createThroughForm(page, 'Acme Billing');
  delegatedCalls++;
  expect(secondResponse.status()).toBe(201);
  const secondAccount = await secondResponse.json();
  expect(secondAccount.accountId).not.toBe(first.accountId);
  const reloadList = page.waitForResponse(isList);
  await page.reload();
  expect((await reloadList).status()).toBe(200);
  delegatedCalls++;
  await expect(page.getByTestId('billing-account-row')).toHaveCount(2);

  for (const id of [first.accountId, secondAccount.accountId]) {
    const access = await pageCall(page, 'GET', `/api/billing/accounts/${id}/access`, tokenA);
    delegatedCalls++;
    expect(access.status).toBe(200);
  }

  // Billing membership grants no merchant shop access.
  expect((await pageCall(page, 'GET', '/api/shops/me', tokenA)).body).toBe(shopsBefore.body);
  expect((await pageCall(page, 'GET', '/api/shops/' + setting('SHOP_B'), tokenA)).status).not.toBe(200);
  const internalPath = '/internal/licensing/v1/customer/accounts';
  expect((await pageCall(page, 'GET', internalPath, tokenA)).type).not.toContain('json');
  expect(await storageHasDelegation(page, contextA)).toBe(false);

  // User B, fresh context at a narrow mobile width: own empty list, A's account refused.
  const contextB = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
  watch.attach(contextB);
  const pageB = await contextB.newPage();
  const tokenB = await signIn(pageB, merchant, setting('EMAIL_B'));
  const listB = pageB.waitForResponse(isList);
  await pageB.goto(merchant + '/billing');
  expect((await listB).status()).toBe(200);
  delegatedCalls++;
  await expect(pageB.getByTestId('billing-accounts-empty')).toBeVisible();
  await expect(pageB.getByTestId('billing-account-create')).toBeVisible();
  const overflow = await pageB.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(overflow).toBeLessThanOrEqual(1);
  const foreign = await pageCall(pageB, 'GET', `/api/billing/accounts/${first.accountId}/access`, tokenB);
  delegatedCalls++;
  expect(foreign.status).toBe(403);
  expect(JSON.parse(foreign.body).code).toBe('Billing.AccountAccessDenied');
  expect(await storageHasDelegation(pageB, contextB)).toBe(false);

  expect(watch.leaked).toBe(false);
  expect(watch.destinations.has(licensing)).toBe(false);
  expect(watch.billingPosts).toBe(4);

  const summary = {
    mode: 'local fixture verification',
    created: createdResponse.status(), retried: retry.status, conflict: conflict.status,
    secondCreated: secondResponse.status(), listedAfterReload: 2, foreignAccess: foreign.status,
    mobileOverflowPx: overflow, delegatedCalls, browserSawDelegation: false, browserContactedLicensing: false,
    accountIds: [first.accountId, secondAccount.accountId],
  };
  writeFileSync(join(setting('EVIDENCE'), 'observations.json'), JSON.stringify(summary, null, 2), { mode: 0o600 });
  console.log(JSON.stringify(summary));
  await contextA.close();
  await contextB.close();
});
