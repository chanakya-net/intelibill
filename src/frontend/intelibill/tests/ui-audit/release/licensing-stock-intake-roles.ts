import { expect, test, type Page } from '@playwright/test';
import {
  STORAGE, count, endpoint, finish, frozen, intakeResult, records, seedRows, withIntake,
} from './licensing-stock-intake-fixtures';
import { session, SHOP_A } from './purchase-receiving-server';

async function seedLegacySubmission(page: Page) {
  const legacy = await records(page, 'inventory-inbound-batch') as { rows: unknown[] }[];
  const createdAt = new Date().toISOString();
  await page.evaluate(({ rows, createdAt, shopId }) => new Promise<void>((resolve, reject) => {
    const db = indexedDB.open('intelibill-offline-drafts');
    db.onerror = () => reject(db.error);
    db.onsuccess = () => {
      const tx = db.result.transaction('inventory-inbound-submission', 'readwrite');
      tx.objectStore('inventory-inbound-submission').put({
        shopId, createdAt, chunks: [{ operationKey: 'legacy-original-key', rows: rows.map(row => ({
          ...(row as object), performedAt: createdAt,
        })) }],
      });
      tx.oncomplete = () => { db.result.close(); resolve(); };
      tx.onabort = () => reject(tx.error);
    };
  }), { rows: legacy[0].rows, createdAt, shopId: SHOP_A });
}

test('R005 Staff cannot see the legacy draft', async ({ page }) => {
  await withIntake(page, async origin => {
    await seedRows(page, ['A']);
    await seedLegacySubmission(page);
    const original = await frozen(page);
    const ownerSession = session();
    const staff = { ...ownerSession, shops: ownerSession.shops.map(shop => ({ ...shop, role: 'Staff' })) };
    await page.evaluate(({ key, staff }) => localStorage.setItem(key, JSON.stringify(staff)), { key: STORAGE, staff });
    await page.route('**/api/shops/me', route => route.fulfill({ json: staff.shops }));
    await page.goto(origin + '/inventory/batch');
    await expect(page.getByRole('heading', { level: 1, name: 'Add Inventory' })).toBeVisible();
    await expect(page.getByText('Fixture goods A')).toHaveCount(0);
    await expect(finish(page)).toHaveCount(0);
    expect(await frozen(page)).toEqual(original);

  });
});

const heading = (page: Page) => page.getByRole('heading', { level: 1, name: 'Add Inventory' });
const cachedRole = (page: Page) => page.evaluate(key =>
  JSON.parse(localStorage.getItem(key)!).shops[0].role as string, STORAGE);

async function expectWithheld(page: Page) {
  await expect(heading(page)).toBeVisible();
  await expect(finish(page)).toHaveCount(0);
  await expect(page.getByText('Fixture goods A')).toHaveCount(0);
  await expect(count(page)).toHaveCount(0);
}

/** Live membership for the fixture user. Each read waits until the test lets it answer. */
async function holdLiveRole(page: Page, initial: string) {
  const live = { role: initial, reads: 0, answer: Promise.withResolvers<void>() };
  await page.route('**/api/shops/me', async route => {
    live.reads++;
    await live.answer.promise;
    await route.fulfill({ json: session().shops.map(shop => ({ ...shop, role: live.role })) }).catch(() => undefined);
  });
  return live;
}

test('R005 a role refusal hides the legacy draft until membership is confirmed again', async ({ page }) => {
  await withIntake(page, async origin => {
    await seedRows(page, ['A']);
    await seedLegacySubmission(page);
    const original = await frozen(page);
    await page.goto(origin + '/inventory/batch');
    await expect(finish(page)).toBeEnabled();
    const posts: string[] = [];
    await page.route(endpoint, async route => {
      posts.push(route.request().postData()!);
      if (posts.length === 1) await route.fulfill({ status: 403, json: { code: 'Inventory.UserIsNotOwnerOrManager' } });
      else await route.fulfill({ json: intakeResult(route) });
    });
    // The live membership is ineligible from here on. The cached session still lists Owner.
    const live = await holdLiveRole(page, 'Staff');
    const recheck = page.locator('.inventory-role-notice').getByRole('button', { name: 'Check again' });
    try {
      await finish(page).click();
      await expectWithheld(page);
      expect(await frozen(page)).toEqual(original);

      // Page recreation: nothing in memory remembers the refusal.
      const readsBeforeReload = live.reads;
      await page.reload();
      await expectWithheld(page);
      await expect.poll(() => live.reads).toBeGreaterThan(readsBeforeReload);
      await expectWithheld(page);
      expect(await cachedRole(page)).toBe('Owner');
      live.answer.resolve();
      await expect(recheck).toBeEnabled();
      await expectWithheld(page);
      expect(await cachedRole(page)).toBe('Owner');
      expect(await frozen(page)).toEqual(original);
      expect(posts).toHaveLength(1);

      // Membership is restored. The draft stays withheld until the live answer arrives.
      live.role = 'Owner';
      live.answer = Promise.withResolvers<void>();
      const readsBeforeRecheck = live.reads;
      await recheck.click();
      await expect.poll(() => live.reads).toBeGreaterThan(readsBeforeRecheck);
      await expectWithheld(page);
      live.answer.resolve();
      await expect(finish(page)).toBeEnabled();
      await expect(page.locator('.table-card')).toContainText('Fixture goods A');
      await finish(page).click();
      await expect(count(page)).toHaveText('Rows: 0');
      expect(posts).toHaveLength(2);
      expect(posts[1]).toBe(posts[0]);
      expect(JSON.parse(posts[0]).operationKey).toBe('legacy-original-key');
      expect(await frozen(page)).toEqual([]);
    } finally {
      live.answer.resolve();
    }
  });
});
