import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page } from '@playwright/test';

const origin = process.env['P0210_MERCHANT_WEB']!;
const evidence = process.env['P0210_EVIDENCE_DIR']!;
const owner = process.env['P0210_OWNER']!;
const branch = process.env['P0210_BRANCH']!;
const password = process.env['P0210_PASSWORD']!;
const timezoneId = 'America/Los_Angeles';
const desktop = { width: 1440, height: 900 };
const mobile = { width: 390, height: 844 };
const deadline = '24 Feb 2027, 05:30:00.123456 IST';

async function login(page: Page, email: string) {
  await page.goto(origin + '/login');
  await page.locator('#identifier').fill(email);
  await page.locator('#password').evaluate((input: HTMLInputElement, value) => {
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }, password);
  const response = page.waitForResponse(result => new URL(result.url()).pathname === '/api/auth/login' &&
    result.request().method() === 'POST');
  await page.locator('button[type=submit]').click();
  expect((await response).status()).toBe(200);
  await expect(page).not.toHaveURL(/\/login/);
}

async function open(browser: Browser, email: string, viewport = desktop) {
  const context = await browser.newContext({ viewport, timezoneId, locale: 'en-IN',
    isMobile: viewport === mobile, hasTouch: viewport === mobile });
  const page = await context.newPage();
  await login(page, email);
  return page;
}

function record(filename: string, value: object) {
  writeFileSync(join(evidence, filename), JSON.stringify(value, null, 2));
}

function monitor(page: Page) {
  const counts = { activation: 0, checkoutOrCharge: 0 };
  page.on('request', request => {
    if (request.method() !== 'POST') return;
    const path = new URL(request.url()).pathname;
    if (path.endsWith('/trial')) counts.activation++;
    if (/\/(orders|checkout|payments|charges)(\/|$)/.test(path)) counts.checkoutOrCharge++;
  });
  return counts;
}

test('@activation explicit consent, lost response recovery and a full account cap', async ({ browser }) => {
  const page = await open(browser, owner);
  const second = await open(browser, branch);
  const requests = monitor(page);
  const branchRequests = monitor(second);
  await page.goto(origin + '/billing/status');
  await page.getByRole('link', { name: 'Start a shop trial' }).click();
  await second.goto(origin + '/billing/trial');
  await expect(second.getByRole('button', { name: 'Start 14-day trial' })).toBeDisabled();
  const start = page.getByRole('button', { name: 'Start 14-day trial' });
  await expect(start).toBeDisabled();
  await expect(page.getByText('No card required. No automatic charge or grace period.')).toBeVisible();
  await expect(page.getByTestId('trial-prospective-end')).toHaveText(deadline);
  await expect(page.getByTestId('trial-member-cap')).toHaveText('1 / 5');
  await expect(page.getByTestId('trial-account-cap')).toHaveText('1');
  await expect(page.getByTestId('trial-terms')).toContainText('trial-local-1');
  const terms = await page.getByTestId('trial-terms').innerText();
  expect(requests.activation).toBe(0);
  await page.screenshot({ path: join(evidence, 'owner-trial-confirmation-desktop.png') });

  const pending = await discardCommittedResponse(page, start);
  await expect(page.getByRole('alert')).toContainText('Trial outcome is unknown');
  const recovered = page.waitForResponse(async response => response.url().endsWith('/operations/' + pending.operationKey)
    && response.ok() && (await response.json()).status === 'Applied');
  await page.reload();
  const applied = await (await recovered).json();
  await expect(page.getByTestId('trial-operation')).toHaveText('Trial activated');
  await expect(page.getByTestId('trial-end')).toHaveText('Trial ends ' + deadline + '.');
  expect(applied.operationKey).toEqual(pending.operationKey);
  expect(applied.endsAt).toEqual(pending.endsAt);
  expect(requests.activation).toBe(1);

  await second.getByRole('checkbox', { name: 'I accept these trial terms.' }).check();
  await second.getByRole('button', { name: 'Start 14-day trial' }).click();
  await expect(second.getByTestId('trial-operation')).toHaveText('Trial activation needs review');
  await expect(second.getByText('This billing account has reached its concurrent trial shop limit.')).toBeVisible();
  expect(branchRequests.activation).toBe(1);
  await second.screenshot({ path: join(evidence, 'trial-cap-refusal.png') });

  const phone = await open(browser, owner, mobile);
  await phone.goto(origin + '/billing/status');
  await expect(phone.getByTestId('status-state')).toHaveText('Active');
  await expect(phone.getByTestId('grace-ends')).toHaveText('No grace period');
  await expect(phone.getByTestId('paid-through')).toHaveText(deadline);
  await phone.screenshot({ path: join(evidence, 'owner-trial-active-mobile.png') });
  expect(requests.checkoutOrCharge + branchRequests.checkoutOrCharge).toBe(0);
  record('observations.json', {
    fixtures: 'Local merchant and licensing Kestrel hosts with PostgreSQL',
    browser: 'Chromium', desktop, mobile, timezoneId, terms, memberCap: 5, accountTrialCap: 1, deadline,
    uncheckedActivationRequests: 0, ownerRequests: requests, branchRequests,
    pending, applied, branch: { status: 'NeedsReview', reason: 'AccountCap' },
    noCard: true, noAutomaticCharge: true, noGrace: true,
  });
  await Promise.all([page.context().close(), second.context().close(), phone.context().close()]);
});

async function discardCommittedResponse(page: Page, start: ReturnType<Page['getByRole']>) {
  let pending: { operationKey: string; status: string; endsAt: string } | undefined;
  await page.route('**/trial', async route => {
    if (route.request().method() !== 'POST') { await route.continue(); return; }
    const response = await route.fetch();
    expect(response.status()).toBe(200);
    pending = await response.json();
    expect(pending!.status).toBe('Pending');
    expect(route.request().postDataJSON()?.termsAccepted).toBe(true);
    expect(route.request().postDataJSON()?.operationKey).toBe(pending!.operationKey);
    await route.abort();
  });
  await page.getByRole('checkbox', { name: 'I accept these trial terms.' }).check();
  await start.click();
  await expect.poll(() => pending?.operationKey).toBeTruthy();
  return pending!;
}

test('@expired exact boundary is expired with no grace', async ({ browser }) => {
  const page = await open(browser, owner, mobile);
  await page.goto(origin + '/billing/status');
  await expect(page.getByTestId('status-state')).toHaveText('Expired');
  await expect(page.getByTestId('grace-ends')).toHaveText('No grace period');
  await expect(page.getByTestId('paid-through')).toHaveText(deadline);
  await page.screenshot({ path: join(evidence, 'trial-expired.png') });
  record('expiry-observation.json', { state: 'Expired', grace: null, timezoneId, mobile });
  await page.context().close();
});
