import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page } from '@playwright/test';

function required(name: string): string {
  const value = process.env['P0601_' + name];
  if (!value) throw new Error('Missing capacity quote fixture variable ' + name);
  return value;
}

const origin = required('MERCHANT_WEB');
const account = required('ACCOUNT');
const term = required('TERM');
const target = required('TARGET');
const evidence = required('EVIDENCE_DIR');
const amount = required('AMOUNT');
const shops = ['Leap Central', 'Leap East'];
const desktop = { width: 1440, height: 900 };
const mobile = { width: 390, height: 844 };
const status = origin + '/billing/accounts/' + account + '/status';
const quotes = '/subscriptions/' + term + '/capacity-change-quotes';

async function open(browser: Browser, viewport = desktop) {
  const context = await browser.newContext({ viewport, locale: 'en-IN', timezoneId: 'Asia/Kolkata',
    isMobile: viewport === mobile, hasTouch: viewport === mobile });
  const page = await context.newPage();
  await page.goto(origin + '/login');
  await page.locator('#identifier').fill(required('OWNER'));
  await page.locator('#password').evaluate((input: HTMLInputElement, password) => {
    input.value = password;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }, required('PASSWORD'));
  const response = page.waitForResponse(result => new URL(result.url()).pathname === '/api/auth/login'
    && result.request().method() === 'POST');
  await page.locator('button[type=submit]').click();
  expect((await response).status()).toBe(200);
  await expect(page).not.toHaveURL(/\/login/);
  return page;
}

async function noOverflow(page: Page) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
}

function card(page: Page) {
  return page.locator(`[data-testid="account-term"]:has([data-term-id="${term}"])`);
}

/** Every rendered fact is licensing's frozen quote: nothing is recomputed in the browser. */
async function review(page: Page) {
  const quote = card(page).getByTestId('capacity-change-quote');
  await expect(quote).toBeVisible();
  await expect(quote.getByTestId('capacity-amount')).toHaveText(amount);
  await expect(quote.getByTestId('capacity-old')).toHaveText('3');
  await expect(quote.getByTestId('capacity-new')).toHaveText('4');
  await expect(quote.getByTestId('capacity-seconds')).toHaveText('1296000 of 2505600 seconds');
  await expect(quote.getByTestId('capacity-interval-start')).toHaveText('31 Jan 2028, 10:00:00 IST');
  await expect(quote.getByTestId('capacity-paid-through')).toHaveText('29 Feb 2028, 10:00:00 IST');
  await expect(quote.getByTestId('capacity-anchor')).toHaveText('Your renewal date stays 29 Feb 2028, 10:00:00 IST.');
  await expect(quote.getByTestId('capacity-shops').locator('li')).toHaveCount(shops.length);
  for (const shop of shops) await expect(quote.getByTestId('capacity-shops').getByText(shop)).toBeVisible();
  await expect(card(page).getByTestId('paid-through')).toHaveText('29 Feb 2028, 10:00 IST');
  return quote.getAttribute('data-quote-id');
}

test('@quote owner reviews a prorated increase, then recovers the same frozen quote after reload', async ({ browser }) => {
  const page = await open(browser);
  await page.goto(status);
  await card(page).getByTestId('capacity-change-open').click();
  await card(page).getByTestId('capacity-change-plan').selectOption(target);
  await card(page).getByTestId('capacity-change-capacity').fill('4');
  const created = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith(quotes));
  await card(page).getByTestId('capacity-change-submit').click();
  const response = await created;
  expect(response.status()).toBe(200);
  expect(response.headers()['cache-control']).toContain('no-store');
  const quote = await response.json();
  expect(await review(page)).toBe(quote.quoteId);
  await noOverflow(page);
  await page.screenshot({ path: join(evidence, 'capacity-quote-desktop.png'), fullPage: true });

  const replayed = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith(quotes));
  await page.reload();
  const replay = await (await replayed).json();
  expect(replay.quoteId).toBe(quote.quoteId);
  expect(replay.snapshotHash).toBe(quote.snapshotHash);
  expect(await review(page)).toBe(quote.quoteId);

  const phone = await open(browser, mobile);
  await phone.goto(status);
  await card(phone).getByTestId('capacity-change-open').click();
  await expect(card(phone).getByTestId('capacity-change-form')).toBeVisible();
  await noOverflow(phone);
  await phone.screenshot({ path: join(evidence, 'capacity-form-mobile.png'), fullPage: true });
  await phone.context().close();

  writeFileSync(join(evidence, 'quote.txt'), quote.quoteId);
  writeFileSync(join(evidence, 'quote-observations.json'), JSON.stringify({
    quoteId: quote.quoteId, outcome: quote.outcome, amountPaise: quote.amountPaise, renderedAmount: amount,
    purchasedGrossPaise: quote.purchasedGrossPaise, targetGrossPaise: quote.targetGrossPaise,
    differencePaise: quote.differencePaise, remainingSeconds: quote.remainingSeconds, totalSeconds: quote.totalSeconds,
    intervalStartsAt: quote.intervalStartsAt, paidThrough: quote.paidThrough, anchorLocal: quote.anchorLocal,
    shops, recoveredAfterReload: replay.quoteId === quote.quoteId, viewports: [desktop, mobile],
  }, null, 2));
});
