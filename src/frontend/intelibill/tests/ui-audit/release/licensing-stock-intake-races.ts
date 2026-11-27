import { expect, test, type Page } from '@playwright/test';
import {
  add, count, endpoint, enterRow, finish, form, frozen, intakeResult, name, openSibling, records, save, seedRows,
  withIntake,
} from './licensing-stock-intake-fixtures';
import { SHOP_A, SHOP_B } from './purchase-receiving-server';

test('R001 Finish save cannot send a replacement manifest seen only in another tab', async ({ page }) => {
  await withIntake(page, async origin => {
    const posts: string[] = [];
    let drop = true;
    await page.context().route(endpoint, async route => {
      posts.push(route.request().postData()!);
      if (drop) await route.abort('connectionreset');
      else await route.fulfill({ json: intakeResult(route) });
    });
    await seedRows(page, ['B']);
    await page.goto(origin + '/inventory/batch');
    await save(page).click();
    await expect(finish(page)).toBeEnabled();

    const sibling = await openSibling(page, origin);
    drop = false;
    await finish(sibling).click();
    await expect(count(sibling)).toHaveText('Rows: 0');
    await enterRow(sibling, 'C');
    await add(sibling).click();
    await expect(count(sibling)).toHaveText('Rows: 1');
    drop = true;
    await save(sibling).click();
    await expect(finish(sibling)).toBeEnabled();
    const originalC = await frozen(sibling);
    expect(posts).toHaveLength(3);
    expect(posts[1]).toBe(posts[0]);

    await expect(page.locator('.table-card')).toContainText('Fixture goods B');
    drop = false;
    await finish(page).click();
    await expect.poll(async () => posts.length > 3 ||
      (await page.locator('.table-card').innerText()).includes('Fixture goods C')).toBe(true);
    expect(posts).toHaveLength(3);
    await expect(page.locator('.table-card')).toContainText('Fixture goods C');
    expect(await frozen(page)).toEqual(originalC);

    await finish(page).click();
    await expect(count(page)).toHaveText('Rows: 0');
    expect(posts).toHaveLength(4);
    expect(posts[3]).toBe(posts[2]);
  });
});

for (const editSibling of [false, true]) {
  test(`R002 stale Save preserves the sibling's ${editSibling ? 'new rows' : 'settled draft'}`, async ({ page }) => {
    await withIntake(page, async origin => {
      const posts: string[] = [];
      await page.context().route(endpoint, async route => {
        posts.push(route.request().postData()!);
        await route.fulfill({ json: intakeResult(route) });
      });
      await seedRows(page, ['A']);
      await page.goto(origin + '/inventory/batch');
      await expect(save(page)).toBeEnabled();

      const sibling = await openSibling(page, origin);
      await save(sibling).click();
      await expect(count(sibling)).toHaveText('Rows: 0');
      if (editSibling) {
        await enterRow(sibling, 'C');
        await add(sibling).click();
        await expect(count(sibling)).toHaveText('Rows: 1');
      }
      const durable = await records(sibling, 'inventory-inbound-batch');
      await save(page).click();
      if (editSibling) {
        await expect.poll(async () => posts.length > 1 ||
          (await page.locator('.table-card').innerText()).includes('Fixture goods C')).toBe(true);
      } else {
        await expect(count(page)).toHaveText('Rows: 0');
      }
      expect(posts).toHaveLength(1);
      if (editSibling) await expect(page.locator('.table-card')).toContainText('Fixture goods C');
      expect(await frozen(page)).toEqual([]);
      expect(await records(page, 'inventory-inbound-batch')).toEqual(durable);
    });
  });
}

const pointers = 'shop-pointers';
const snapshotDb = 'intelibill-offline-sales-snapshot';
async function seedSnapshots(page: Page) {
  await page.evaluate(({ shops, dbName, pointers }) => new Promise<void>((resolve, reject) => {
    const request = indexedDB.open(dbName, 2);
    request.onupgradeneeded = () => {
      for (const [store, keyPath] of [[pointers, 'shopId'], ['snapshot-attempts', 'snapshotId'], ...[
        'batches', 'customers', 'discount-rules', 'active-leases', 'services',
      ].map(store => [store, 'key'])]) {
        if (!request.result.objectStoreNames.contains(store)) request.result.createObjectStore(store, { keyPath });
      }
    };
    request.onerror = () => reject(request.error);
    request.onsuccess = () => {
      const tx = request.result.transaction([pointers, 'snapshot-attempts'], 'readwrite');
      const completedAt = new Date().toISOString();
      for (const shopId of shops) {
        tx.objectStore(pointers).put({ shopId, usableSnapshotId: shopId, usableCompletedAt: completedAt, stale: false });
        tx.objectStore('snapshot-attempts').put({ snapshotId: shopId, shopId, schemaVersion: 4,
          status: 'complete', startedAt: completedAt, completedAt, catalogVersion: 1, businessTimeZoneId: 'Asia/Kolkata' });
      }
      tx.oncomplete = () => { request.result.close(); resolve(); };
      tx.onabort = () => reject(tx.error);
    };
  }), { shops: [SHOP_A, SHOP_B], dbName: snapshotDb, pointers });
}

test('R003 a dropped partial response invalidates the original shop snapshot across tabs', async ({ page }) => {
  await withIntake(page, async origin => {
    let committed: ReturnType<typeof intakeResult> | null = null;
    await page.route(endpoint, async route => {
      committed = intakeResult(route, 1);
      await route.abort('connectionreset');
    });
    await seedRows(page);
    await page.goto(origin + '/inventory/batch');
    const sibling = await openSibling(page, origin);
    const broadcasts: string[] = [];
    await sibling.exposeFunction('recordIntakeInvalidation', (shop: string) => broadcasts.push(shop));
    await sibling.evaluate(() => {
      new BroadcastChannel('intelibill-expiry-catalog').onmessage = event => {
        void (window as unknown as { recordIntakeInvalidation(shop: string): Promise<void> }).recordIntakeInvalidation(event.data);
      };
    });
    await seedSnapshots(page);
    await save(page).click();
    await expect(finish(page)).toBeEnabled();
    expect(committed?.succeeded.map((row: { clientRowId: string }) => row.clientRowId)).toEqual(['A']);
    const original = await frozen(page);
    expect(original).toHaveLength(1);
    await expect.poll(() => broadcasts).toContain(SHOP_A);
    expect(broadcasts).not.toContain(SHOP_B);
    const savedPointers = await records(sibling, pointers, snapshotDb);
    expect(savedPointers).toEqual([
      expect.objectContaining({ shopId: SHOP_A, stale: true }),
      expect.objectContaining({ shopId: SHOP_B, stale: false }),
    ]);
    await page.reload();
    await expect(finish(page)).toBeEnabled();
    expect(await frozen(sibling)).toEqual(original);
  });
});

test('R006 a pending POST or interrupted save does not consume form input', async ({ page }) => {
  await withIntake(page, async origin => {
    const response = Promise.withResolvers<void>();
    let received = false;
    await page.route(endpoint, async route => {
      received = true;
      await response.promise;
      await route.abort('connectionreset');
    });
    try {
      await seedRows(page, ['A']);
      await page.goto(origin + '/inventory/batch');
      await save(page).click();
      await expect.poll(() => received).toBe(true);
      await enterRow(page, 'C');
      if (await add(page).isEnabled()) await add(page).click();
      await expect(name(page)).toHaveValue('Fixture goods C');
      await expect(add(page)).toBeDisabled();
      response.resolve();
      await expect(finish(page)).toBeEnabled();
      await form(page).evaluate((element: HTMLFormElement) => element.requestSubmit());
      await expect(name(page)).toHaveValue('Fixture goods C');
      await expect(add(page)).toBeDisabled();
      await expect(count(page)).toHaveText('Rows: 1');
    } finally {
      response.resolve();
    }
  });
});
