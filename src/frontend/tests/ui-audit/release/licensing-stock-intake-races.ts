import { expect, test, type Page, type Route } from '@playwright/test';
import { BUILD, STORAGE, frozen, records, seedRows } from './licensing-stock-intake-fixtures';
import { session, SHOP_A, SHOP_B, startPurchaseReceivingServer } from './purchase-receiving-server';

const endpoint = '**/api/inventory/inbound/batch';
const form = (page: Page) => page.locator('.row-form');
const save = (page: Page) => page.getByRole('button', { name: 'Save', exact: true });
const finish = (page: Page) => page.getByRole('button', { name: 'Finish save', exact: true });
const add = (page: Page) => page.getByRole('button', { name: 'Add Row', exact: true });
const count = (page: Page) => page.locator('.row-count');
const name = (page: Page) => page.locator('.item-name-autocomplete input');

async function withIntake(page: Page, run: (origin: string) => Promise<void>) {
  const server = await startPurchaseReceivingServer(BUILD);
  try {
    await page.addInitScript(({ key, value }) => {
      if (!localStorage.getItem(key)) localStorage.setItem(key, JSON.stringify(value));
    }, { key: STORAGE, value: session() });
    await page.goto(server.origin);
    await run(server.origin);
  } finally {
    await server.close();
  }
}

function intakeResult(route: Route, successfulRows = Infinity) {
  const { items } = route.request().postDataJSON();
  const succeeded = items.slice(0, successfulRows).map((row: { clientRowId: string }) => ({
    clientRowId: row.clientRowId,
    result: { ...row, itemId: 'item-' + row.clientRowId, inventoryBatchId: 'lot-' + row.clientRowId,
      stockTransactionId: 'movement-' + row.clientRowId, batchQuantity: 2, totalQuantity: 2 },
  }));
  return { requestedCount: items.length, successCount: succeeded.length, failedCount: 0, failed: [], succeeded };
}

async function openSibling(page: Page, origin: string) {
  const sibling = await page.context().newPage();
  await sibling.goto(origin + '/inventory/batch');
  return sibling;
}

async function enterRow(page: Page, id: string) {
  await name(page).fill('Fixture goods ' + id);
  await page.locator('.barcode-autocomplete input').fill(id);
  const details = page.getByRole('button', { name: 'Expand optional details' });
  if (await details.isVisible()) await details.click();
  await page.locator('[formcontrolname=batchNumber]').fill('lot-' + id);
  await page.locator('[formcontrolname=mrp] input').fill('20');
  const price = page.locator('[formcontrolname=salesPrice] input');
  await price.fill('16');
  await price.press('Tab');
  const taxRate = page.locator('[formcontrolname=taxRatePercent] input');
  await taxRate.fill('5');
  await taxRate.press('Tab');
  await expect.poll(() => form(page).locator('.ng-invalid[formcontrolname]')
    .evaluateAll(elements => elements.map(element => element.getAttribute('formcontrolname')))).toEqual([]);
}

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

test('R005 a role refusal hides the legacy draft until eligible recovery', async ({ page }) => {
  await withIntake(page, async origin => {
    await seedRows(page, ['A']);
    await seedLegacySubmission(page);
    const original = await frozen(page);
    await page.goto(origin + '/inventory/batch');
    await expect(finish(page)).toBeEnabled();
    const posts: string[] = [];
    let denied = true;
    await page.route(endpoint, async route => {
      posts.push(route.request().postData()!);
      if (denied) await route.fulfill({ status: 403, json: { code: 'Inventory.UserIsNotOwnerOrManager' } });
      else await route.fulfill({ json: intakeResult(route) });
    });
    await finish(page).click();
    await expect(finish(page)).toHaveCount(0);
    await expect(page.getByText('Fixture goods A')).toHaveCount(0);
    expect(await frozen(page)).toEqual(original);
    denied = false;
    await page.reload();
    await expect(finish(page)).toBeEnabled();
    await finish(page).click();
    await expect(count(page)).toHaveText('Rows: 0');
    expect(posts).toHaveLength(2);
    expect(posts[1]).toBe(posts[0]);
    expect(JSON.parse(posts[0]).operationKey).toBe('legacy-original-key');
    expect(await frozen(page)).toEqual([]);
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
