import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page } from '@playwright/test';
import { fillSecret, otp } from './p01-12-fixture';

interface Fixture {
  account: string;
  job: string;
  step: number;
  password: string;
  operators: Record<string, { Email: string; Seed: string }>;
}

const scratch = process.env['P0703_TEMPORARY']!;
const origin = process.env['P0703_ADMIN_WEB']!;
const fixture: Fixture = JSON.parse(readFileSync(join(scratch, 'fixture.json'), 'utf8'));
const path = '/api/admin/accounts/' + fixture.account + '/notifications';

async function signIn(page: Page, role: string) {
  const operator = fixture.operators[role];
  await page.goto(origin + '/auth/sign-in');
  await page.getByLabel('Email', { exact: true }).fill(operator.Email);
  await fillSecret(page, 'Password', fixture.password);
  await fillSecret(page, 'Authenticator code', otp(operator.Seed, fixture.step));
  const signed = page.waitForResponse(r => r.url().endsWith('/api/admin/auth/sign-in') && r.request().method() === 'POST');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  expect((await signed).status()).toBe(200);
  await expect(page).toHaveURL(origin + '/');
}

async function openPreview(page: Page) {
  await page.goto(origin + '/accounts');
  const read = page.waitForResponse(r => r.url().endsWith(path));
  await page.getByLabel('Billing account ID').fill(fixture.account);
  await page.getByRole('button', { name: 'Read access status' }).click();
  const response = await read;
  expect(response.status()).toBe(200);
  expect(response.headers()['cache-control']).toContain('no-store');
  return response;
}

async function checkRenewal(page: Page) {
  await signIn(page, 'billing');
  await openPreview(page);
  const row = page.locator('[data-job-id="' + fixture.job + '"]');
  await expect(row.getByTestId('email-state')).toHaveText('Queued');
  writeFileSync(join(scratch, 'renew.req'), 'renew');
  await expect.poll(() => existsSync(join(scratch, 'renew.ack')), { timeout: 30_000 }).toBe(true);

  const refreshed = page.waitForResponse(r => r.url().endsWith(path));
  await page.getByTestId('provider-notifications-refresh').click();
  const body = await (await refreshed).json();
  const current = body.items.find((item: { jobId: string }) => item.jobId === fixture.job);
  expect(current.emailDelivery).toMatchObject({ state: 'Suppressed', suppressionReasonCode: 'SourceNoLongerCurrent' });
  expect(current.emailDelivery.captureUrl ?? null).toBeNull();
  const reason = row.getByTestId('email-reason');
  await expect(reason).toHaveText('The agreement changed.');
  await expect(row.getByTestId('mailpit-capture')).toHaveCount(0);
  const summary = row.locator('summary');
  await summary.focus();
  await page.keyboard.press('Enter');
  await expect(reason).toBeHidden();
  await page.keyboard.press('Enter');
  await expect(reason).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByTestId('provider-notifications-locale').selectOption('hi-IN');
  await expect(reason).toHaveText('अनुबंध बदल गया है।');
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
  await row.screenshot({ path: join(process.env['P0703_EVIDENCE_DIR']!, 'suppressed-hi.png') });
}

async function checkRole(browser: Browser, role: string) {
  const page = await browser.newPage();
  try {
    await signIn(page, role);
    if (role === 'security-administrator') {
      const response = await page.request.get(origin + path);
      expect(response.status()).toBe(403);
      expect(await response.text()).not.toContain('suppressionReasonCode');
      await expect(page.getByTestId('email-detail')).toHaveCount(0);
      return;
    }
    const response = await openPreview(page);
    if (role === 'support') {
      const body = await response.json();
      expect(body.commercialIncluded).toBe(false);
      expect(JSON.stringify(body)).not.toContain('suppressionReasonCode');
      expect(JSON.stringify(body)).not.toContain('destinationHint');
      expect(JSON.stringify(body)).not.toContain('evidencedTermPricePaise');
      await expect(page.getByTestId('provider-notifications-redacted')).toBeVisible();
      await expect(page.getByTestId('email-detail')).toHaveCount(0);
    } else {
      const body = await response.json();
      const job = body.items.find((item: { jobId: string }) => item.jobId === fixture.job);
      expect(job.emailDelivery.suppressionReasonCode).toBe('SourceNoLongerCurrent');
      await expect(page.locator('[data-job-id="' + fixture.job + '"]').getByTestId('email-reason')).toHaveText('The agreement changed.');
    }
  } finally {
    await page.close();
  }
}

test('OperatorSeesWhyAReminderWasSuppressed', async ({ page, browser }) => {
  await checkRenewal(page);
  for (const role of ['licensing-manager', 'support', 'security-administrator']) {
    await checkRole(browser, role);
  }
});
