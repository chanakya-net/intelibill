import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page } from '@playwright/test';

function env(name: string): string {
  const v = process.env['P0607A_' + name];
  if (!v) throw new Error('Missing P0607A_' + name);
  return v;
}

const web = env('MERCHANT_WEB');
const scratch = env('TEMPORARY');
const evidence = env('EVIDENCE_DIR');
const source = env('SOURCE_ACCOUNT');
const destination = env('DESTINATION_ACCOUNT');
const term = env('TERM');
const northId = env('SHOP');
const secrets = JSON.parse(readFileSync(join(scratch, 'sessions.json'), 'utf8')) as Record<string, string>;

function signal(name: string) { writeFileSync(join(scratch, name + '.txt'), name); }
async function waitFor(name: string) {
  await expect.poll(() => existsSync(join(scratch, name + '.txt')), { timeout: 60_000 }).toBe(true);
}

async function login(browser: Browser, email: string): Promise<Page> {
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'en-IN', timezoneId: 'Asia/Kolkata' });
  const page = await context.newPage();
  await page.goto(web + '/login');
  await page.locator('#identifier').fill(email);
  await page.locator('#password').fill(secrets['merchantPassword']);
  await page.locator('button[type=submit]').click();
  await expect(page).not.toHaveURL(/\/login/);
  return page;
}

/** Lets the next matching POST reach the server and commit, then loses its response on the way back. */
async function dropNextResponse(page: Page, suffix: string) {
  let dropped = false;
  const matcher = (url: URL) => url.pathname.endsWith(suffix);
  await page.route(matcher, async route => {
    if (dropped || route.request().method() !== 'POST') return route.fallback();
    dropped = true;
    await route.fetch();
    await route.abort('connectionreset');
  });
  return () => page.unroute(matcher);
}

/** The frozen decision survives the lost response and a reload, and only the same decision can be sent again. */
async function recoverSameDecision(page: Page) {
  await expect(page.getByTestId('transfer-pending')).toBeVisible();
  await page.reload();
  await expect(page.getByTestId('transfer-pending')).toBeVisible();
  await expect(page.getByTestId('transfer-request-form')).toHaveCount(0);
  await page.getByTestId('transfer-retry').click();
  await expect(page.getByTestId('transfer-pending')).toHaveCount(0);
}

// 1. The source Billing Owner reviews the named impact, then requests. The response is lost after commit.
async function requestTransfer(payer: Page) {
  await payer.goto(`${web}/billing/accounts/${source}/subscriptions/${term}/transfers`);
  await expect(payer.getByTestId('transfer-no-change')).toContainText(/changes nothing/i);
  await payer.getByTestId('transfer-shop').selectOption(northId);
  await payer.getByTestId('transfer-destination').fill(destination);
  await expect(payer.getByTestId('transfer-submit')).toHaveCount(0);
  await payer.getByTestId('transfer-preview').click();
  await expect(payer.getByTestId('transfer-review-shop')).toContainText('North');
  await expect(payer.getByTestId('transfer-review-shop')).toContainText(destination);
  await expect(payer.getByTestId('transfer-review-paid-through')).toBeVisible();
  await expect(payer.getByTestId('transfer-review-treatment')).toContainText(/paid-through/i);
  await expect(payer.getByTestId('transfer-review-permit')).toBeVisible();
  await expect(payer.getByTestId('transfer-unknown-queues')).toContainText(/unknown/i);
  await payer.getByTestId('transfer-reason').fill('Branch sold to the destination company');
  for (const box of await payer.getByRole('checkbox').all()) {
    await expect(payer.getByTestId('transfer-submit')).toBeDisabled();
    await box.check();
  }
  await payer.screenshot({ path: join(evidence, '01-request-review.png'), fullPage: true });
  const restore = await dropNextResponse(payer, `/subscriptions/${term}/coverage-transfers`);
  await payer.getByTestId('transfer-submit').click();
  await recoverSameDecision(payer);
  await restore();

  const item = payer.getByTestId('transfer-item');
  await expect(item).toHaveCount(1);
  await expect(item.getByTestId('transfer-item-shop')).toHaveText('North');
  await expect(item.getByTestId('transfer-parties')).toContainText('Renewal Fixture');
  await expect(item.getByTestId('transfer-parties')).toContainText(destination);
  await expect(item.getByTestId('transfer-state')).toHaveAttribute('data-state', 'AwaitingConsents');
  await expect(item.getByTestId('transfer-progress')).toContainText('0 of 3');
  await payer.screenshot({ path: join(evidence, '02-requested-zero-consents.png'), fullPage: true });
}

// 2. Partial consent: the source owner alone, again through a lost response and the same key.
async function giveSourceConsent(payer: Page): Promise<string> {
  const item = payer.getByTestId('transfer-item');
  const restore = await dropNextResponse(payer, '/consents/source-billing-owner/accept');
  await payer.getByTestId('transfer-accept-SourceBillingOwner').click();
  await recoverSameDecision(payer);
  await restore();
  await expect(item.getByTestId('transfer-consent-SourceBillingOwner')).toHaveAttribute('data-status', 'Accepted');
  await expect(item.getByTestId('transfer-consent-DestinationBillingOwner')).toHaveAttribute('data-status', 'AwaitingConsent');
  await expect(item.getByTestId('transfer-consent-ShopOwner')).toHaveAttribute('data-status', 'AwaitingConsent');
  await expect(item.getByTestId('transfer-progress')).toContainText('1 of 3');
  await expect(item.getByTestId('transfer-state')).toHaveAttribute('data-state', 'AwaitingConsents');
  await expect(item.getByTestId('transfer-accept-DestinationBillingOwner')).toHaveCount(0);
  await payer.screenshot({ path: join(evidence, '03-partial-consent.png'), fullPage: true });
  const shopLink = await item.getByTestId('transfer-shop-link').getAttribute('href');
  expect(shopLink).toContain(`/billing/transfer-consent/${source}/${northId}/`);
  return shopLink!;
}

// 3. An unrelated signed-in owner is refused; nothing about the request is shown to them.
async function refuseOutsider(browser: Browser) {
  const outsider = await login(browser, secrets['outsider']);
  await outsider.goto(`${web}/billing/accounts/${source}/transfers`);
  await expect(outsider.getByTestId('transfer-error')).toBeVisible();
  await expect(outsider.getByTestId('transfer-item')).toHaveCount(0);
  await outsider.context().close();
}

// 4. The destination Billing Owner finds the incoming request under their own account and accepts their purpose.
async function giveDestinationConsent(browser: Browser) {
  const receiver = await login(browser, secrets['destination']);
  await receiver.goto(`${web}/billing/accounts/${destination}/transfers`);
  const incoming = receiver.getByTestId('transfer-item');
  await expect(incoming.getByTestId('transfer-item-shop')).toHaveText('North');
  await expect(incoming.getByTestId('transfer-parties')).toContainText('Renewal Fixture');
  await expect(incoming.getByTestId('transfer-treatment-shown')).toContainText(/paid-through/i);
  await expect(incoming.getByTestId('transfer-accept-SourceBillingOwner')).toHaveCount(0);
  await incoming.getByTestId('transfer-accept-DestinationBillingOwner').click();
  await expect(incoming.getByTestId('transfer-consent-DestinationBillingOwner')).toHaveAttribute('data-status', 'Accepted');
  await expect(incoming.getByTestId('transfer-progress')).toContainText('2 of 3');
  await expect(incoming.getByTestId('transfer-parties')).toContainText('Destination Company');
}

// 5. The named shop's owner sees their shop, the parties and the unknown queues, then accepts.
async function giveShopOwnerConsent(browser: Browser, shopLink: string) {
  const north = await login(browser, secrets['north']);
  await north.goto(web + shopLink);
  const owner = north.getByTestId('transfer-owner');
  await expect(owner.getByTestId('transfer-owner-shop')).toHaveText('North');
  await expect(owner.getByTestId('transfer-parties')).toContainText('Destination Company');
  await expect(owner.getByTestId('transfer-unknown-queues')).toBeVisible();
  await owner.getByTestId('transfer-accept-ShopOwner').click();
  await expect(owner.getByTestId('transfer-owner-status')).toHaveAttribute('data-status', 'Accepted');
  await expect(owner.getByTestId('transfer-owner-status')).toHaveAttribute('data-delivery', 'Pending');
  await north.screenshot({ path: join(evidence, '04-shop-owner-consent.png'), fullPage: true });
  signal('shop-consented');
  await waitFor('delivered');
  await north.reload();
  await expect(owner.getByTestId('transfer-owner-status')).toHaveAttribute('data-delivery', 'Recorded');
}

// 6. All three consents are shown as collected and awaiting provider review. Nothing else has happened.
async function expectCollected(payer: Page) {
  const item = payer.getByTestId('transfer-item');
  await payer.reload();
  await expect(item.getByTestId('transfer-progress')).toContainText('3 of 3');
  await expect(item.getByTestId('transfer-state')).toHaveAttribute('data-state', 'ConsentsCollected');
  await expect(item.getByTestId('transfer-state')).toContainText(/awaiting provider review/i);
  await expect(payer.getByTestId('transfer-no-change')).toBeVisible();
  await payer.screenshot({ path: join(evidence, '05-consents-collected.png'), fullPage: true });
}

// Each signal hands control to the test host, which proves from PostgreSQL that nothing moved before continuing.
test('Request a coverage transfer and collect the three owner consents without changing access', async ({ browser }) => {
  const payer = await login(browser, secrets['payer']);
  await requestTransfer(payer);
  signal('requested');
  await waitFor('requested-checked');

  const shopLink = await giveSourceConsent(payer);
  signal('partial');
  await waitFor('partial-checked');

  await refuseOutsider(browser);
  await giveDestinationConsent(browser);
  await giveShopOwnerConsent(browser, shopLink);
  await expectCollected(payer);
  signal('collected');
  await waitFor('collected-checked');
});
