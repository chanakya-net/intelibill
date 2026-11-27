import { existsSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page } from '@playwright/test';

function required(name: string): string {
  const value = process.env['P0603_' + name];
  if (!value) throw new Error('Missing reduction fixture variable ' + name);
  return value;
}

const origin = required('MERCHANT_WEB');
const account = required('ACCOUNT');
const term = required('TERM');
const target = required('TARGET');
const retainedShop = required('RETAINED_SHOP');
const conflictedShop = required('CONFLICTED_SHOP');
const evidence = required('EVIDENCE_DIR');
const amount = required('AMOUNT');
const drift = required('OUTCOME') === 'drift';
const desktop = { width: 1440, height: 900 };
const mobile = { width: 390, height: 844 };
const statusUrl = origin + '/billing/accounts/' + account + '/status';

async function open(browser: Browser, viewport = desktop) {
  const context = await browser.newContext({
    viewport, locale: 'en-IN', timezoneId: 'Asia/Kolkata',
    isMobile: viewport === mobile, hasTouch: viewport === mobile,
  });
  const page = await context.newPage();
  await page.goto(origin + '/login');
  await page.locator('#identifier').fill(required('OWNER'));
  await page.locator('#password').evaluate((input: HTMLInputElement, password) => {
    input.value = password;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }, required('PASSWORD'));
  const login = page.waitForResponse(r => new URL(r.url()).pathname === '/api/auth/login' && r.request().method() === 'POST');
  await page.locator('button[type=submit]').click();
  expect((await login).status()).toBe(200);
  await expect(page).not.toHaveURL(/\/login/);
  return page;
}

async function noOverflow(page: Page) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
}

function termCard(page: Page) {
  return page.locator(`[data-testid="account-term"]:has([data-term-id="${term}"])`);
}

async function waitForFile(file: string) {
  await expect.poll(() => existsSync(file), { timeout: 60_000 }).toBe(true);
}

test('owner previews, selects retained shop, funds early, and verifies renewal reduction in Chromium', async ({ browser }) => {
  const page = await open(browser, drift ? mobile : desktop);
  await page.goto(statusUrl);

  // Open reduction flow and request live preview.
  await termCard(page).getByTestId('reduction-open').click();
  await termCard(page).getByTestId('reduction-plan').selectOption(target);
  await termCard(page).getByTestId('reduction-preview-submit').click();
  await expect(termCard(page).getByTestId('reduction-preview')).toBeVisible();
  await expect(termCard(page).getByTestId('reduction-amount')).toHaveText(amount);
  await expect(termCard(page).getByTestId('reduction-conflict-summary')).toBeVisible();

  // Assert zero shops are preselected and conflicted shop checkbox is disabled.
  const boxes = termCard(page).getByTestId('reduction-shop-checkbox');
  expect(await boxes.count()).toBe(2);
  await expect(boxes.nth(0)).not.toBeChecked();
  await expect(boxes.nth(1)).not.toBeChecked();

  const conflictedRow = termCard(page).locator(`[data-shop-id="${conflictedShop}"]`);
  await expect(conflictedRow.getByTestId('reduction-shop-conflict')).toBeVisible();
  await expect(conflictedRow.getByTestId('reduction-shop-checkbox')).toBeDisabled();

  // Owner explicitly selects retained shop and confirms schedule.
  const retainedRow = termCard(page).locator(`[data-shop-id="${retainedShop}"]`);
  await retainedRow.getByTestId('reduction-shop-checkbox').check();
  await termCard(page).getByTestId('reduction-confirm-check').check();
  await termCard(page).getByTestId('reduction-schedule-submit').click();

  await expect(termCard(page).getByTestId('reduction-scheduled')).toBeVisible();
  await expect(termCard(page).getByTestId('reduction-state')).toContainText('Scheduled for renewal');

  // Reload status page and verify scheduled reduction recovers cleanly.
  await page.reload();
  await expect(termCard(page).getByTestId('reduction-state')).toContainText('Scheduled for renewal');

  // Proceed to explicit P03 checkout and pay early via local development simulator.
  await termCard(page).getByTestId('reduction-order-start').click();
  await expect(page.getByTestId('checkout-total')).toHaveText(amount);
  const orderId = new URL(page.url()).searchParams.get('order')!;
  expect(orderId).toBeTruthy();
  await page.getByRole('checkbox').check();
  await page.getByRole('button', { name: /accept/i }).click();
  await expect(page.getByTestId('checkout-accepted')).toBeVisible();
  await page.getByTestId('payment-start').click();
  await expect(page.getByTestId('payment-simulator-notice')).toBeVisible();
  await page.getByTestId('payment-simulator-capture').click();
  await expect(page.getByTestId('payment-confirmed')).toHaveText('Payment confirmed');
  await expect(page.getByTestId('reduction-pending')).toBeVisible();

  // Reload checkout page and verify funded pending state survives reload.
  await page.reload();
  await expect(page.getByTestId('reduction-pending')).toBeVisible();
  await page.getByTestId('confirmation-list').click();
  await expect(page.getByTestId('confirmation-download')).toBeVisible();

  // Signal test harness that early simulator payment is captured.
  writeFileSync(join(evidence, 'captured.txt'), orderId);

  if (drift) {
    // Wait for harness to advance to renewal boundary with drifted member count -> NeedsResolution!
    await waitForFile(join(evidence, 'blocked.txt'));
    await page.goto(statusUrl);
    await expect(termCard(page).getByTestId('reduction-state')).toContainText('Needs resolution');
    await expect(termCard(page).getByTestId('reduction-conflict')).toBeVisible();
    // Signal harness to suspend extra member so owner can resolve cleanly without paying again.
    writeFileSync(join(evidence, 'ready-to-resolve.txt'), '1');
    await waitForFile(join(evidence, 'staff-resolved.txt'));
    await termCard(page).getByTestId('reduction-resolve-submit').click();
    await expect(termCard(page).getByTestId('reduction-state')).toContainText('Applying at renewal');
    writeFileSync(join(evidence, 'resolved.txt'), '1');
  }

  await waitForFile(join(evidence, 'applied.txt'));
  await page.goto(statusUrl);
  await expect(termCard(page).getByTestId('reduction-state')).toContainText('Applied');
  await expect(termCard(page).getByTestId('reduction-applied-term')).toBeVisible();
  await noOverflow(page);
  await page.screenshot({ path: join(evidence, 'capacity-reduction.png'), fullPage: true });
});
