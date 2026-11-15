import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Page } from '@playwright/test';

function setting(name: string): string {
  const value = process.env['P0201_' + name];
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

async function pageRead(page: Page, path: string, token?: string) {
  return page.evaluate(async ({ path, token }) => {
    const headers = token ? { Authorization: 'Bearer ' + token } : undefined;
    const response = await fetch(path, { headers });
    const type = response.headers.get('content-type') ?? '';
    return {
      status: response.status,
      noStore: response.headers.get('cache-control')?.includes('no-store') ?? false,
      type,
      body: await response.text(),
    };
  }, { path, token });
}

function hasDelegation(value: string): boolean {
  return (value.match(/[\w-]+\.[\w-]+\.[\w-]+/g) ?? []).some(candidate => {
      try {
        const typ = JSON.parse(Buffer.from(candidate.split('.')[0], 'base64url').toString()).typ;
        return typ === 'intelibill-merchant-billing-delegation+jwt';
      } catch {
        return false;
      }
    });
}

test('merchant-origin requests cross only the authenticated backend boundary', async ({ page, request }) => {
  const merchant = loopback('MERCHANT_WEB');
  const admin = loopback('ADMIN_WEB');
  const licensing = loopback('LICENSING_API');
  const account = setting('ACCOUNT');
  const internal = '/internal/licensing/v1/customer/accounts/' + account + '/access';
  const billing = (id: string) => '/api/billing/accounts/' + id + '/access';
  const destinations = new Set<string>();
  let delegationInBrowser = false;
  let requestIdInBrowser = false;
  const observed: Promise<void>[] = [];
  page.on('request', req => {
    const url = new URL(req.url());
    destinations.add(url.origin);
    observed.push(req.allHeaders().then(headers => {
      delegationInBrowser ||= hasDelegation(headers['authorization'] ?? '') || hasDelegation(headers['cookie'] ?? '');
      requestIdInBrowser ||= 'x-billing-request-id' in headers;
    }));
  });

  await page.goto(merchant + '/login');
  await page.locator('#identifier').fill(setting('EMAIL'));
  await page.locator('#password').fill(setting('PASSWORD'));
  const login = page.waitForResponse(response => new URL(response.url()).pathname === '/api/auth/login');
  await page.locator('button[type=submit]').click();
  const loggedIn = await login;
  expect(loggedIn.status()).toBe(200);
  const token = (await loggedIn.json()).accessToken as string;
  expect(typeof token).toBe('string');

  const allowed = await pageRead(page, billing(account), token);
  expect(allowed.status).toBe(200);
  expect(allowed.noStore).toBe(true);
  expect(JSON.parse(allowed.body)).toEqual({ accountId: account, actions: ['billing.account.read'] });
  const denied = await pageRead(page, billing(setting('DENIED_ACCOUNT')), token);
  expect(denied.status).toBe(403);
  expect(denied.noStore).toBe(true);
  expect(JSON.parse(denied.body).code).toBe('Billing.AccountAccessDenied');
  const existing = await pageRead(page, '/api/shops/me', token);
  expect(existing.status).toBe(200);
  expect(existing.noStore).toBe(true);
  const merchantProxy = await pageRead(page, internal, token);
  expect(merchantProxy.type).not.toContain('json');
  const storage = await page.evaluate(() => JSON.stringify([localStorage, sessionStorage, document.cookie]));
  expect(hasDelegation(storage)).toBe(false);

  await page.goto(admin);
  const adminProxy = await pageRead(page, internal);
  expect(adminProxy.type).not.toContain('json');
  const adminAccess = await pageRead(page, '/api/admin/access');
  expect(adminAccess.status).toBe(401);
  const direct = await request.get(licensing + internal, { headers: { authorization: 'Bearer ' + token } });
  expect(direct.status()).toBe(401);
  expect(direct.headers()['cache-control']).toContain('no-store');
  await Promise.all(observed);
  expect(delegationInBrowser || requestIdInBrowser).toBe(false);
  expect([allowed, denied, existing, merchantProxy, adminProxy, adminAccess].some(r => hasDelegation(r.body))).toBe(false);
  expect(destinations.has(licensing)).toBe(false);
  expect((await page.context().cookies()).some(cookie => hasDelegation(cookie.value))).toBe(false);

  const summary = {
    merchant: allowed.status, denied: denied.status, existing: existing.status,
    merchantProxy: { status: merchantProxy.status, type: merchantProxy.type },
    adminProxy: { status: adminProxy.status, type: adminProxy.type }, adminAccess: adminAccess.status,
    direct: direct.status(), noStore: true, delegationInBrowser: false,
  };
  writeFileSync(join(setting('EVIDENCE'), 'observations.json'), JSON.stringify(summary, null, 2), { mode: 0o600 });
  console.log(JSON.stringify(summary));
});
