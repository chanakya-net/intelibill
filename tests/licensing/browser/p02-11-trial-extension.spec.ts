import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Locator, type Page } from '@playwright/test';
import { adminPost, createOwner, origin, signIn } from './p01-12-fixture';
import { confirm, draftTrial, enableCatalogue, prepare, signInMerchant } from './p02-07-fixture';

const merchant = process.env['P0207_MERCHANT_WEB']!;
const evidence = process.env['P0211_EVIDENCE_DIR']!;
const account = process.env['P0211_ACCOUNT']!;
const shop = process.env['P0211_SHOP']!;
const extension = `/api/admin/accounts/${account}/shops/${shop}/trial-extensions`;
const storageState = join(process.env['P0211_TEMPORARY']!, 'manager-session.json');
const originalEnd = '24 Feb 2027, 05:30:00.123456 IST';
const deadline = '10 Mar 2027, 18:45:30.123456 IST';
const endsAt = '2027-03-10T13:15:30.123456Z';
const reason = 'Evaluation delayed by inventory count';
const reviewReference = 'EVAL-857';
const timezoneId = 'America/Los_Angeles';

function record(phase: string, observations: object) {
  writeFileSync(join(evidence, `observations-${phase}.json`), JSON.stringify(observations, null, 2));
}

async function openAccount(page: Page) {
  await page.goto(origin + '/accounts');
  await page.getByLabel('Billing account ID').fill(account);
  const status = page.waitForResponse(response => new URL(response.url()).pathname === `/api/admin/accounts/${account}/status`);
  await page.getByRole('button', { name: 'Read access status' }).click();
  expect((await status).status()).toBe(200);
  return page.getByTestId('trial-extension-editor');
}

async function preview(editor: Locator) {
  await editor.getByLabel('Trial shop').selectOption(shop);
  await editor.getByLabel('New trial end (IST)').fill('2027-03-10 18:45:30.123456');
  await editor.getByLabel('Reason', { exact: true }).fill(reason);
  await editor.getByLabel('Review reference').fill(reviewReference);
  await editor.getByRole('button', { name: 'Preview extension' }).click();
  const panel = editor.getByTestId('trial-extension-preview');
  await expect(panel.getByTestId('trial-extension-preview-end')).toHaveText(deadline);
  await expect(panel).toContainText(reason);
  await expect(panel).toContainText(reviewReference);
  await expect(panel).toContainText('No grace period');
  await expect(panel).toContainText('None recorded');
  return panel;
}

async function readMerchantStatus(page: Page, state: 'Active' | 'Expired', filename: string) {
  const read = page.waitForResponse(response => new URL(response.url()).pathname === `/api/licensing/shops/${shop}/status`);
  await page.goto(merchant + '/billing/status');
  const response = await read;
  expect(response.status()).toBe(200);
  expect(response.headers()['cache-control']).toContain('no-store');
  const body = await response.json();
  expect(body).toMatchObject({ state, observeOnly: true, term: { basis: 'Trial', graceEndsAt: null, commercial: null } });
  expect(body.term.accessEndsAt.replace('+00:00', 'Z')).toBe(endsAt);
  expect(JSON.stringify(body)).not.toContain(reviewReference);
  expect(JSON.stringify(body)).not.toContain(reason);
  await expect(page.getByTestId('status-state')).toHaveText(state);
  await expect(page.getByTestId('paid-through')).toHaveText(deadline);
  await expect(page.getByTestId('grace-ends')).toHaveText('No grace period');
  await expect(page.getByTestId('observe-only')).toBeVisible();
  await page.screenshot({ path: join(evidence, filename), fullPage: true });
  return { state, displayedDeadline: deadline, accessEndsAt: body.term.accessEndsAt, evaluatedAt: body.evaluatedAt,
    graceEndsAt: null, observeOnly: true };
}

test('@start publish a local policy and explicitly activate the original trial', async ({ browser }) => {
  const admin = await browser.newPage();
  const owner = await signIn(admin, await createOwner(admin));
  await admin.goto(origin + '/accounts');
  await expect(admin.getByTestId('trial-extension-editor')).toHaveCount(0);
  const denied = await adminPost(admin, extension + '/preview', { endsAt, reason, reviewReference });
  expect(denied.status()).toBe(403);
  expect((await denied.json()).code).toBe('TrialExtension.AccessDenied');

  await enableCatalogue(admin, owner);
  await draftTrial(admin);
  await prepare(admin, 'trial policy');
  await confirm(admin);
  await admin.context().storageState({ path: storageState });
  await admin.context().close();

  const customer = await browser.newPage({ locale: 'en-IN', timezoneId });
  await signInMerchant(customer);
  await customer.goto(merchant + '/billing/trial');
  await expect(customer.getByTestId('trial-prospective-end')).toHaveText(originalEnd);
  await customer.getByRole('checkbox', { name: 'I accept these trial terms.' }).check();
  await customer.getByRole('button', { name: 'Start 14-day trial' }).click();
  await expect(customer.getByTestId('trial-operation')).toHaveText('Trial activated');
  await expect(customer.getByTestId('trial-end')).toHaveText('Trial ends ' + originalEnd + '.');
  record('start', { platformOwnerPreview: 403, originalEnd, state: 'Trial activated' });
  await customer.context().close();
});

test('@extension approve the reviewed exact deadline and read it as the merchant', async ({ browser }) => {
  const provider = await browser.newContext({ storageState, viewport: { width: 1280, height: 900 } });
  const tabA = await provider.newPage();
  const panelA = await preview(await openAccount(tabA));
  await tabA.screenshot({ path: join(evidence, 'provider-preview.png'), fullPage: true });
  const tabB = await provider.newPage();
  const panelB = await preview(await openAccount(tabB));
  let tabBPosts = 0;
  tabB.on('request', request => {
    if (request.method() === 'POST' && new URL(request.url()).pathname === extension) tabBPosts++;
  });

  // The actual PostgreSQL-backed confirmation commits. Chromium loses the reply and retains the same operation.
  let committed: { endsAt: string; operationKey: string; paymentRecorded: boolean; graceEndsAt: null } | undefined;
  await tabA.route('**' + extension, async route => {
    if (route.request().method() !== 'POST') return route.continue();
    const response = await route.fetch();
    expect(response.status()).toBe(201);
    committed = await response.json();
    await route.abort('connectionreset');
  });
  await panelA.getByRole('checkbox').check();
  await panelA.getByRole('button', { name: 'Approve extension' }).click();
  await expect(tabA.getByTestId('trial-extension-error')).toContainText('unconfirmed');
  expect(committed).toMatchObject({ endsAt, paymentRecorded: false, graceEndsAt: null });

  await panelB.getByRole('checkbox').check();
  await panelB.getByRole('button', { name: 'Approve extension' }).click();
  await expect(tabB.getByTestId('trial-extension-error')).toContainText('Another tab');
  await expect(tabB.getByTestId('trial-extension-pending')).toContainText(committed!.operationKey);
  expect(tabBPosts).toBe(0);
  await tabB.close();

  await tabA.unroute('**' + extension);
  const editor = await openAccount(tabA);
  await expect(editor.getByTestId('trial-extension-pending')).toContainText(committed!.operationKey);
  await editor.getByRole('button', { name: 'Check extension outcome' }).click();
  await expect(editor.getByTestId('trial-extension-result')).toContainText(deadline);
  await expect(tabA.getByTestId('provider-trial-ends')).toHaveText(deadline);
  await tabA.screenshot({ path: join(evidence, 'provider-approved.png'), fullPage: true });
  await provider.close();

  const customer = await browser.newPage({ locale: 'en-IN', timezoneId });
  await signInMerchant(customer);
  const observed = await readMerchantStatus(customer, 'Active', 'merchant-updated.png');
  record('extension', { reason, reviewReference, preview: deadline, committed, secondTabApprovalPosts: tabBPosts, merchant: observed });
  await customer.context().close();
});

for (const state of ['Active', 'Expired'] as const) {
  const phase = state.toLowerCase();
  test(`@${phase} staff reads the reviewed deadline on a non-IST browser`, async ({ browser }) => {
    const customer = await browser.newPage({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true,
      locale: 'en-IN', timezoneId });
    await signInMerchant(customer);
    const observed = await readMerchantStatus(customer, state, `merchant-${phase}.png`);
    record(phase, { ...observed, browserZone: timezoneId });
    await customer.context().close();
  });
}
