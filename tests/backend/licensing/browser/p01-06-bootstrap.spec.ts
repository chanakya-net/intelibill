import { execFileSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { expect, test, type Locator, type Page } from '@playwright/test';

const admin = process.env['P0106_ADMIN_WEB'];
const proofFile = process.env['P0106_PROOF_FILE'];
const apiPath = '/api/admin/auth/bootstrap';

test.skip(!admin || !proofFile, 'Run in the isolated P01-06 fixture with recording disabled');

test('first owner remains enrollment-only across reload and restart', async ({ page, context }) => {
  const proof = readFileSync(proofFile!, 'utf8').trim();
  const password = 'Fixture-' + randomBytes(24).toString('base64url');
  await page.goto(admin + '/auth/bootstrap');
  await expect(page.getByRole('heading', { name: 'Set up your account' })).toBeVisible();
  await page.keyboard.press('Tab');
  await expect(page.getByLabel('Setup proof')).toBeFocused();
  await enterSecret(page.getByLabel('Setup proof'), proof);
  await enterSecret(page.getByLabel('Password'), password);
  const response = page.waitForResponse(response =>
    new URL(response.url()).pathname === apiPath && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Create owner account' }).click();
  expect((await response).status()).toBe(200);
  await pending(page);
  await assertRestricted(page);

  const cookie = (await context.cookies()).find(cookie => cookie.name === 'Intelibill.Admin.BootstrapEnrollment');
  expect(Boolean(cookie?.httpOnly && cookie.sameSite === 'Strict')).toBe(true);
  const replay = await page.evaluate(async ({ apiPath, proofToken, password }) => {
    const status = await (await fetch(apiPath + '/context')).json();
    const replay = await fetch(apiPath, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Bootstrap-CSRF': status.requestToken },
      body: JSON.stringify({ proofToken, password }),
    });
    return { available: status.available, status: replay.status };
  }, { apiPath, proofToken: proof, password });
  expect(replay).toEqual({ available: false, status: 409 });

  await page.reload();
  await pending(page);
  execFileSync('bash', [process.env['P0106_RUNNER']!, '--restart'], { stdio: 'inherit' });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.reload();
  await pending(page);
  await assertRestricted(page);
  await page.screenshot({ path: process.env['P0105_ARTIFACTS'] + '/p0106-enrollment-narrow.png' });

  await context.clearCookies({ name: 'Intelibill.Admin.BootstrapEnrollment' });
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Resume MFA enrollment' })).toBeVisible();
  await page.getByLabel('Verified email').fill('fixture.owner@example.test');
  await enterSecret(page.getByLabel('Password'), password);
  await page.getByRole('button', { name: 'Resume enrollment' }).click();
  await pending(page);
  await assertRestricted(page);
  console.log('P01-06 fixture: setup 200, replay 409, enrollment and denial persisted after restart and credential resume');
});

async function enterSecret(input: Locator, value: string) {
  await input.evaluate((element: HTMLInputElement, secret) => {
    element.value = secret;
  }, value);
}

async function pending(page: Page) {
  await expect(page.getByRole('heading', { name: 'MFA setup required' })).toBeVisible();
  await expect(page.getByText('No provider permissions')).toBeVisible();
  await expect(page.getByLabel('Password')).toHaveCount(0);
}

async function assertRestricted(page: Page) {
  const access = await page.evaluate(async () => {
    const business = await fetch('/api/admin/access');
    return {
      business: business.status,
      noStore: business.headers.get('cache-control'),
      localKeys: Object.keys(localStorage).length,
      sessionKeys: Object.keys(sessionStorage).length,
    };
  });
  expect(access).toEqual({ business: 403, noStore: 'no-store', localKeys: 0, sessionKeys: 0 });
}
