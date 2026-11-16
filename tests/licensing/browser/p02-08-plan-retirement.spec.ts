import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { createOwner, signIn } from './p01-12-fixture';
import { confirm, draftPlan, draftTrial, enableCatalogue, history, name, openPlans, prepare, signInMerchant } from './p02-07-fixture';

const evidence = process.env['P0207_EVIDENCE']!;
const reason = 'Withdraw local fixture offer';
const versionApi = '/api/billing/plans/versions/';

async function publishFixture(admin: Page) {
  await enableCatalogue(admin, await signIn(admin, await createOwner(admin)));
  await draftTrial(admin);
  await prepare(admin, 'trial policy');
  await confirm(admin);
  await admin.getByRole('button', { name: 'Close publication' }).click();
  await draftPlan(admin);
  await prepare(admin, 'plan');
  await confirm(admin);
  await admin.getByRole('button', { name: 'Close publication' }).click();
  return (await history(admin)).find(row => row.snapshot.plan?.versionNumber === 1)!;
}

function nextVersionRead(shop: Page) {
  return shop.waitForResponse(response => new URL(response.url()).pathname.startsWith(versionApi)).then(async response => {
    expect(response.status()).toBe(200);
    expect(response.headers()['cache-control']).toContain('no-store');
    return response.json();
  });
}

async function retireAndRecover(admin: Page) {
  await admin.getByText(name + ' · version 1 · published', { exact: true }).click();
  await admin.getByRole('button', { name: 'Retire from new purchase' }).click();
  const panel = admin.getByRole('region', { name: 'Retire plan version' });
  await expect(panel).toContainText('Existing agreements retain their accepted terms and access.');
  await expect(panel).toContainText('Development fixture');
  await expect(panel).toContainText('₹14,990.00');
  const submit = panel.getByRole('button', { name: 'Confirm retirement' });
  await expect(submit).toBeDisabled();
  await panel.getByLabel('Retirement reason').fill(reason);
  await panel.getByLabel('I confirm this version should leave new selection.').check();

  await admin.route('**/api/admin/plans/*/versions/*/retire', async route => {
    const committed = await route.fetch();
    expect(committed.status()).toBe(200);
    expect(route.request().postDataJSON().operationKey === new URL(admin.url()).searchParams.get('retirement')).toBe(true);
    await route.abort('failed');
  }, { times: 1 });
  await submit.click();
  await expect(panel.getByRole('alert')).toContainText('retirement outcome is unconfirmed');
  const key = new URL(admin.url()).searchParams.get('retirement');
  expect(Boolean(key)).toBe(true);
  const recover = admin.waitForResponse(response => new URL(response.url()).pathname === '/api/admin/catalogue/retirements/' + key);
  await admin.reload();
  expect((await recover).status()).toBe(200);
  await expect(panel.getByRole('status')).toHaveText('Plan version retired from new purchase');
  expect(new URL(admin.url()).searchParams.get('retirement') === key).toBe(true);
  await admin.screenshot({ path: join(evidence, 'admin-recovered-desktop.png') });
}

test('retirement disappears from new selection and exact terms remain recoverable', async ({ browser }) => {
  const adminContext = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const merchantContext = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
  try {
    const admin = await adminContext.newPage();
    const shop = await merchantContext.newPage();
    const original = await publishFixture(admin);
    await signInMerchant(shop);
    const [first] = await openPlans(shop);
    expect(first).toMatchObject({ versionId: original.versionId, developmentFixture: true, annualPricePaise: 1499000 });
    const exactTermsRead = nextVersionRead(shop);
    await shop.getByRole('link', { name: 'View exact terms' }).click();
    const before = await exactTermsRead;
    expect(before).toEqual({ terms: first, availableForNewPurchase: true });
    const savedLink = shop.url();
    const card = shop.getByTestId('retained-plan');
    await expect(card).toContainText('Currently available for new purchase');
    await expect(card.locator('.facts')).toContainText('₹14,990.00');
    const facts = await card.locator('.facts').innerText();

    await retireAndRecover(admin);
    await admin.getByText(name + ' · version 1 · retired', { exact: true }).click();
    const historyRow = admin.locator('details').filter({ hasText: name + ' · version 1 · retired' });
    await expect(historyRow).toContainText(reason);
    await expect(historyRow.getByRole('button', { name: 'Retire from new purchase' })).toHaveCount(0);
    const archived = (await history(admin)).find(row => row.versionId === original.versionId)!;
    expect(archived.snapshot).toEqual(original.snapshot);
    expect(archived.snapshotHash).toBe(original.snapshotHash);
    expect(archived.retirement).toMatchObject({ reason });

    expect(await openPlans(shop)).toEqual([]);
    await expect(shop.getByTestId('published-plan')).toHaveCount(0);
    await expect(shop.getByText('No plans are available for new purchase.')).toBeVisible();
    await shop.screenshot({ path: join(evidence, 'merchant-empty-mobile.png') });
    const archivedRead = nextVersionRead(shop);
    await shop.goto(savedLink);
    expect(await archivedRead).toEqual({ terms: before.terms, availableForNewPurchase: false });
    await expect(card).toContainText('No longer available for new purchase');
    await expect(card).toContainText('Development fixture');
    await expect(card.locator('.facts')).toHaveText(facts, { useInnerText: true });
    await shop.evaluate(() => window.scrollTo(0, 0));
    await shop.screenshot({ path: join(evidence, 'merchant-retained-mobile.png'), fullPage: true });

    writeFileSync(join(evidence, 'observations.json'), JSON.stringify({
      environment: 'Development fixture on loopback, shipping admin and merchant web applications',
      confirmationRecoveredAfterReload: true,
      freshCatalogueCount: 0,
      versionBeforeAvailable: true,
      versionAfterAvailable: false,
      exactCustomerTermsEqual: true,
      historySnapshotEqual: true,
      snapshotHashBefore: original.snapshotHash,
      merchantCacheControl: 'no-store',
      retainedFixtureFacts: { annualPricePaise: 1499000, monthlyPricePaise: 149900, includedShops: 3, membersPerShop: 5 },
    }, null, 2));
  } finally {
    await Promise.all([adminContext.close(), merchantContext.close()]);
  }
});
