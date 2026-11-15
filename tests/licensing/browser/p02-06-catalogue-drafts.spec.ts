import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { createOwner, origin, proofFile, signIn } from './p01-12-fixture';

// Real Chromium, real licensing API and PostgreSQL behind the shipping admin web; nothing is intercepted.
// Every amount is a local INR fixture value.
const evidence = process.env['P0206_EVIDENCE']!;
const plansPath = '/api/admin/plans';
const growth = 'Growth (local fixture)';
const observations: string[] = [];

test.skip(!origin || !proofFile || !evidence, 'Run through P0206BrowserTests');

function observe(note: string) {
  observations.push(note);
  console.log(note);
}

async function plans(page: Page) {
  const response = await page.request.get(origin + plansPath);
  expect(response.status()).toBe(200);
  expect(response.headers()['cache-control']).toBe('no-store');
  return (await response.json()).items as { planId: string; kind: string; draft: Record<string, unknown> }[];
}

async function openCatalogue(page: Page) {
  await page.goto(origin + '/');
  await page.getByRole('link', { name: 'Plan catalogue drafts' }).click();
  await expect(page.getByRole('heading', { name: 'Plan catalogue drafts' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Trial policy' })).toBeVisible();
}

async function grantSelfLicensingManager(page: Page, email: string) {
  await page.getByRole('link', { name: 'Manage administrator access' }).click();
  const users = await (await page.request.get(origin + '/api/admin/users')).json();
  const self = users.items.find((row: { email: string }) => row.email.toLowerCase() === email.toLowerCase());
  await page.getByLabel('Administrator', { exact: true }).selectOption(self.id);
  await page.getByRole('checkbox', { name: 'Licensing Manager', exact: true }).check();
  await page.getByLabel('Reason', { exact: true }).selectOption('responsibilities-changed');
  await page.getByLabel('I confirm these access changes.').check();
  await page.getByRole('button', { name: 'Save administrator access' }).click();
  // Changing one's own grants ends the session; the new grant applies only after a fresh sign-in.
  await expect.poll(async () => (await page.request.get(origin + '/api/admin/auth/session')).status()).toBe(401);
  const owner = (await (await page.request.get(origin + '/api/admin/users')).status());
  expect(owner).toBe(401);
}

test('licensing manager drafts plans and the trial policy through the admin console', async ({ browser }) => {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const fresh = await browser.newContext();
  try {
    const page = await context.newPage();
    let owner = await signIn(page, await createOwner(page));

    await expect(page.getByRole('link', { name: 'Plan catalogue drafts' })).toHaveCount(0);
    await page.goto(origin + '/catalogue');
    await expect(page.getByRole('alert')).toContainText('Only a Licensing Manager');
    expect((await page.request.get(origin + plansPath)).status()).toBe(403);
    observe('Platform Owner without the Licensing Manager grant: no catalogue link, refusal shown, GET /api/admin/plans 403.');

    await page.goto(origin + '/');
    await grantSelfLicensingManager(page, owner.email);
    owner = await signIn(page, owner);
    await openCatalogue(page);
    observe('Owner granted itself Licensing Manager; the session ended; after a new password + TOTP sign-in the catalogue link appeared.');

    await page.getByRole('button', { name: 'New plan draft' }).click();
    await page.getByLabel('Plan kind').selectOption('multi-shop');
    await page.getByLabel('Plan name').fill(growth);
    await page.getByLabel('Monthly base price (₹)').fill('1,499.00');
    await page.getByRole('button', { name: 'Save draft' }).click();
    await expect(page.getByRole('status')).toContainText('Draft saved');
    await expect(page.getByTestId('draft-status')).toHaveText('Draft · not published');
    await expect(page.getByTestId('missing-facts')).toContainText('Annual base price');
    await expect(page.getByTestId('missing-facts')).toContainText('Members per shop');

    await page.reload();
    await page.getByRole('button', { name: new RegExp(growth.replace(/[()]/g, '\\$&')) }).click();
    await expect(page.getByLabel('Monthly base price (₹)')).toHaveValue('1499.00');
    await expect(page.getByLabel('Annual base price (₹)')).toHaveValue('');
    let [saved] = await plans(page);
    expect(saved.draft['monthlyPricePaise']).toBe(149900);
    expect(saved.draft['annualPricePaise']).toBeNull();
    expect(saved.draft['complete']).toBe(false);
    observe('Incomplete multi-shop draft saved with only a name and ₹1,499.00 monthly; after reload the API reports monthlyPricePaise 149900, annualPricePaise null, complete false, status draft.');

    await page.getByLabel('Annual base price (₹)').fill('14,990.00');
    await page.getByLabel('Purchased shop slots').fill('3');
    await page.getByLabel('Members per shop').fill('5');
    await page.getByLabel('Extra shops').selectOption('yes');
    await page.getByLabel('Extra-shop monthly price (₹)').fill('399');
    await page.getByLabel('Extra-shop annual price (₹)').fill('3,990');
    await page.getByLabel('Customer terms version').fill('terms-2026.11');
    await page.getByLabel('Refund policy version').fill('refund-2026.11');
    await page.getByRole('button', { name: 'Save draft' }).click();
    await expect(page.getByText('All required facts are recorded.')).toBeVisible();
    await expect(page.getByTestId('draft-status')).toHaveText('Draft · not published');
    await page.reload();
    [saved] = await plans(page);
    expect(saved.draft).toMatchObject({
      status: 'draft', complete: true, annualPricePaise: 1499000, includedShops: 3, membersPerShop: 5,
      extraShopsOffered: true, extraShopMonthlyPricePaise: 39900, extraShopAnnualPricePaise: 399000,
      customerTermsVersion: 'terms-2026.11', refundPolicyVersion: 'refund-2026.11', currency: 'INR',
    });
    await expect(page.getByText('Draft · not published · complete')).toBeVisible();
    await page.screenshot({ path: join(evidence, 'catalogue-desktop.png'), fullPage: true });
    observe('Explicit annual/monthly INR prices, 3 shop slots, 5 members per shop, explicit extra-shop prices and both policy versions saved; after reload the draft is complete and still status draft (not published).');

    await page.getByRole('button', { name: 'New plan draft' }).click();
    await page.getByLabel('Plan kind').selectOption('custom');
    await page.getByLabel('Plan name').fill('Chain (local fixture)');
    await page.getByLabel('Annual base price (₹)').fill('50,000');
    await page.getByLabel('Purchased shop slots').fill('12');
    await page.getByLabel('Members per shop').fill('8');
    await page.getByRole('button', { name: 'Save draft' }).click();
    await expect(page.getByRole('alert')).toContainText('need a reason');
    expect(await plans(page)).toHaveLength(1);
    await page.getByLabel('Reason for negotiated terms').fill('Twelve-shop chain negotiated annual terms');
    await page.getByRole('button', { name: 'Save draft' }).click();
    await expect(page.getByRole('status')).toContainText('Draft saved');
    await page.reload();
    const custom = (await plans(page)).find((item) => item.kind === 'custom')!;
    expect(custom.draft).toMatchObject({
      negotiationReason: 'Twelve-shop chain negotiated annual terms', annualPricePaise: 5000000, status: 'draft',
      complete: false, monthlyPricePaise: null,
    });
    observe('Custom draft without a reason was blocked before any request; with a reason it saved and the reason persisted after reload.');

    await expect(page.getByText('fixed 14 calendar days')).toBeVisible();
    await page.getByLabel('Active member cap').fill('5');
    await page.getByRole('button', { name: 'Save trial policy draft' }).click();
    await expect(page.getByTestId('trial-missing')).toContainText('Concurrent trial shop cap');
    await page.getByLabel('Concurrent trial shop cap').fill('2');
    await page.getByLabel('Trial terms version').fill('trial-2026.11');
    await page.getByRole('button', { name: 'Save trial policy draft' }).click();
    await expect(page.getByText('All trial facts are recorded.')).toBeVisible();
    await expect(page.getByTestId('trial-status')).toHaveText('Draft · not published');
    const trial = await (await page.request.get(origin + '/api/admin/trial-policy/draft')).json();
    expect(trial.draft).toMatchObject({
      durationDays: 14, activeMemberCap: 5, maxConcurrentTrialShops: 2, complete: true, status: 'draft',
    });
    observe('Trial policy: one cap left it incomplete; adding the shop cap and terms version completed it; 14 days fixed; status draft.');

    await page.reload();
    await page.getByRole('button', { name: new RegExp(growth.replace(/[()]/g, '\\$&')) }).click();
    const current = (await plans(page)).find((item) => item.kind === 'multi-shop')!;
    const { requestToken } = await (await page.request.get(origin + '/api/admin/auth/sign-in/context')).json();
    const bump = await page.request.put(origin + plansPath + '/' + current.planId + '/draft', {
      headers: { Origin: origin, 'X-Admin-CSRF': requestToken },
      data: {
        expectedVersion: current.draft['version'], kind: 'multi-shop', name: growth, monthlyPricePaise: 149900,
        annualPricePaise: 1499000, includedShops: 3, membersPerShop: 5, extraShopsOffered: true,
        extraShopMonthlyPricePaise: 39900, extraShopAnnualPricePaise: 399000,
        customerTermsVersion: 'terms-2026.11', refundPolicyVersion: 'refund-2026.11',
      },
    });
    expect(bump.status()).toBe(200);
    await page.getByLabel('Members per shop').fill('9');
    await page.getByRole('button', { name: 'Save draft' }).click();
    await expect(page.getByText('A draft changed elsewhere. Reload before editing.')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Reload catalogue' })).toBeVisible();
    await expect(page.getByLabel('Plan name')).toHaveCount(0);
    saved = (await plans(page)).find((item) => item.kind === 'multi-shop')!;
    expect(saved.draft['membersPerShop']).toBe(5);
    observe('A stale save after another revision answered 409; the editor cleared and required a reload; the saved draft kept 5 members per shop.');

    await page.setViewportSize({ width: 390, height: 844 });
    await page.getByRole('button', { name: 'Reload catalogue' }).click();
    await expect(page.getByText('Draft · not published · complete')).toBeVisible();
    await page.screenshot({ path: join(evidence, 'catalogue-390.png'), fullPage: true });

    const anonymous = await fresh.newPage();
    expect((await anonymous.request.get(origin + plansPath)).status()).toBe(401);
    await anonymous.goto(origin + '/catalogue');
    await expect(anonymous).toHaveURL(origin + '/auth/sign-in');
    expect((await anonymous.request.get(origin + '/api/billing/plans')).status()).toBe(404);
    observe('A fresh browser context got 401 from /api/admin/plans and was sent to sign-in; /api/billing/plans on the admin origin answered 404.');

    writeFileSync(join(evidence, 'observations.json'), JSON.stringify({
      fixture: 'Local INR fixture amounts; real Chromium, real licensing API and PostgreSQL; no live provider.',
      observations,
    }, null, 2));
  } finally {
    await Promise.all([context.close(), fresh.close()]);
  }
});
