import { createHash } from 'node:crypto';
import { resolve } from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { session, SHOP_A, startPurchaseReceivingServer } from './purchase-receiving-server';

const STORAGE = 'inventory.auth.session.local';
const DB = 'intelibill-offline-drafts';
const DRAFT = 'inventory-inbound-batch';
const SUBMISSION = 'inventory-inbound-submission';
const BUILD = resolve(__dirname, '../../../dist/INVENTORY/browser');
test.use({ actionTimeout: 8000 });
test.setTimeout(25000);

async function seedRows(page: Page) {
  await page.evaluate(({ db, store, shop }) => new Promise<void>((done, reject) => {
    const opened = indexedDB.open(db, 2);
    opened.onupgradeneeded = () => {
      opened.result.createObjectStore(store, { keyPath: 'shopId' });
      opened.result.createObjectStore('inventory-inbound-submission', { keyPath: 'shopId' });
    };
    opened.onerror = () => reject(opened.error);
    opened.onsuccess = () => {
      const tx = opened.result.transaction(store, 'readwrite');
      tx.objectStore(store).put({
        shopId: shop,
        rows: ['A', 'B'].map(id => ({
          clientRowId: id, itemName: 'Fixture goods ' + id, barcode: id, batchNumber: id, uom: 'pcs',
          quantity: 2, totalPurchaseCost: 20, mrp: 20, salesPrice: 16, taxRatePercent: 0,
          taxIncluded: false, purchaseTaxIncluded: false, purchaseTaxModeConfirmed: true,
        })),
      });
      tx.oncomplete = () => { opened.result.close(); done(); };
      tx.onerror = () => reject(tx.error);
    };
  }), { db: DB, store: DRAFT, shop: SHOP_A });
}

function frozen(page: Page) {
  return page.evaluate(({ db, store }) => new Promise<unknown[]>((done, reject) => {
    const opened = indexedDB.open(db);
    opened.onerror = () => reject(opened.error);
    opened.onsuccess = () => {
      const read = opened.result.transaction(store).objectStore(store).getAll();
      read.onsuccess = () => { opened.result.close(); done(read.result); };
      read.onerror = () => reject(read.error);
    };
  }), { db: DB, store: SUBMISSION });
}

function switchOwner(page: Page, value: ReturnType<typeof session>) {
  return page.evaluate(value => {
    const channel = new BroadcastChannel('intelibill.auth.session');
    channel.postMessage({ type: 'SESSION_UPDATED', session: value });
    channel.close();
  }, value);
}

function differentOwner() {
  const other = session();
  other.user.id = '30000000-0000-4000-8000-000000000099';
  const [header, , signature] = other.accessToken.split('.');
  const claims = Buffer.from(JSON.stringify({ sub: other.user.id, active_shop_id: SHOP_A,
    exp: Math.floor(Date.now() / 1000) + 3600 })).toString('base64url');
  other.accessToken = [header, claims, signature].join('.');
  return other;
}

for (const width of [1440, 390]) {
  test(`P04-06 recovers the original stock manifest at ${width}px`, async ({ page, browser }, info) => {
    const server = await startPurchaseReceivingServer(BUILD);
    const requests: { hash: string; key: string; status: number }[] = [];
    const committed = new Map<string, object>();
    const authorityReads: number[] = [];
    let release: (() => void) | undefined;
    let heldResponse: Promise<void> | undefined;
    let phase: 'drop' | 'forbid' | 'expired' | 'verified' = 'drop';
    try {
      await page.setViewportSize({ width, height: 900 });
      await page.addInitScript(({ key, value }) => {
        if (!localStorage.getItem(key)) localStorage.setItem(key, JSON.stringify(value));
      }, { key: STORAGE, value: session() });
      await page.route('**/api/licensing/shops/*/trading-authority', route => {
        const expired = phase === 'expired';
        authorityReads.push(expired ? 403 : 200);
        return route.fulfill({ status: expired ? 403 : 200, json: expired
          ? { code: 'Licence.Expired', enforced: true }
          : { schemaVersion: 1, shopId: SHOP_A, enforced: true,
            authorityVerifiedAt: new Date().toISOString(), validUntil: new Date(Date.now() + 60_000).toISOString() },
        });
      });
      await page.route('**/api/inventory/inbound/batch', async route => {
        const raw = route.request().postData()!;
        const { operationKey, items } = JSON.parse(raw);
        const status = { drop: 0, forbid: 403, expired: 207, verified: 200 }[phase];
        requests.push({ key: operationKey, hash: createHash('sha256').update(raw).digest('hex'), status });
        if (phase === 'forbid') {
          await route.fulfill({ status, json: { code: 'Inventory.UserIsNotOwnerOrManager' } });
          return;
        }
        for (const row of items) {
          const id = row.clientRowId;
          if (id === 'B' && phase !== 'verified') continue;
          if (!committed.has(id)) committed.set(id, {
            clientRowId: id,
            result: { ...row, itemId: 'item-' + id, inventoryBatchId: 'batch-' + id,
              stockTransactionId: 'movement-' + id, batchQuantity: 2, totalQuantity: 2 },
          });
        }
        if (phase === 'drop') { await route.abort('connectionreset'); return; }
        const failed = phase === 'expired' ? [{ clientRowId: 'B', itemName: 'Fixture goods B', barcode: 'B',
          retryable: false, errors: [{ code: 'Licence.Expired', description: 'fixture raw description' }] }] : [];
        const response = { status, json: { requestedCount: 2, successCount: committed.size,
          failedCount: failed.length, succeeded: [...committed.values()], failed } };
        if (heldResponse) {
          await heldResponse;
          await route.fulfill(response).catch(() => undefined);
          return;
        }
        await route.fulfill(response);
      });

      await page.goto(server.origin);
      await seedRows(page);
      await page.goto(server.origin + '/inventory/batch');
      const save = page.getByRole('button', { name: 'Save', exact: true });
      const finish = page.getByRole('button', { name: 'Finish save', exact: true });
      await expect(save).toBeEnabled();
      await save.click();
      await expect(page.locator('.inventory-interrupted-save')).toBeVisible();
      expect([...committed.keys()]).toEqual(['A']);
      const immutableA = committed.get('A');
      const original = await frozen(page);
      expect(original).toHaveLength(1);

      await page.reload();
      await expect(page.locator('.inventory-interrupted-save')).toBeVisible();
      phase = 'forbid';
      await finish.click();
      await expect.poll(() => requests.at(-1)?.status).toBe(403);
      await expect(finish).toBeEnabled();
      expect(await frozen(page)).toEqual(original);

      phase = 'expired';
      await finish.click();
      await expect.poll(() => requests.at(-1)?.status).toBe(207);
      await expect(page.getByText('fixture raw description')).toHaveCount(0);
      await expect(page.locator('.row-error-text:visible').filter({ hasText: 'This shop’s trading access has ended.' })).toBeVisible();
      await expect(page.locator('.inventory-interrupted-save')).toBeVisible();
      await expect(page.locator('.row-count')).toHaveText('Rows: 1');
      await expect(save).toBeDisabled();
      expect(await frozen(page)).toEqual(original);
      expect(committed.get('A')).toEqual(immutableA);
      await page.screenshot({ path: info.outputPath('partial.png'), fullPage: true });

      await page.route('**/api/items/barcodes/labels', route => route.fulfill({ status: 400, json: {} }));
      await page.getByRole('button', { name: 'Print labels for successful rows' }).click();
      const label = page.getByRole('dialog');
      await expect(label).toContainText('Fixture goods A');
      const printRequest = page.waitForRequest('**/api/items/barcodes/labels');
      await label.getByRole('button', { name: 'Print', exact: true }).click();
      expect((await printRequest).postDataJSON().items).toEqual([{ itemId: 'item-A', quantity: 2, inventoryBatchId: 'batch-A' }]);
      await label.getByRole('button', { name: 'Cancel', exact: true }).click();

      if (width === 1440) {
        heldResponse = new Promise(resolve => { release = resolve; });
        await finish.click();
        await expect.poll(() => requests.length).toBe(4);
        const sibling = await page.context().newPage();
        await sibling.goto(server.origin);
        await switchOwner(sibling, differentOwner());
        await expect(page).toHaveURL(server.origin + '/');
        release?.();
        await page.goto(server.origin + '/inventory/batch');
        await expect(page.locator('.row-count')).toHaveText('Rows: 0');
        await expect(finish).toHaveCount(0);
        expect(await frozen(page)).toEqual(original);
        await switchOwner(sibling, session());
        await expect(page).toHaveURL(server.origin + '/');
        await sibling.close();
        heldResponse = undefined;
        await page.goto(server.origin + '/inventory/batch');
        await expect(finish).toBeEnabled();
      }

      phase = 'verified';
      const notice = page.locator('.inventory-access-notice');
      await notice.getByRole('button', { name: 'Check again' }).click();
      await expect(notice).toHaveCount(0);
      expect(authorityReads.at(-1)).toBe(200);
      await finish.click();
      await expect.poll(() => (frozen(page)).then(rows => rows.length)).toBe(0);
      expect([...committed.keys()]).toEqual(['A', 'B']);
      expect(requests.map(x => x.status)).toEqual(width === 1440 ? [0, 403, 207, 207, 200] : [0, 403, 207, 200]);
      expect(new Set(requests.map(x => x.hash)).size).toBe(1);
      expect(new Set(requests.map(x => x.key)).size).toBe(1);
    } finally {
      release?.();
      await info.attach('local-api-fixture', { body: JSON.stringify({
        chromium: browser.version(), requests, authorityReads, outcomes: Object.fromEntries(committed),
        remainingManifests: (await frozen(page).catch(() => [])).length,
      }, null, 2), contentType: 'application/json' });
      await server.close();
    }
  });
}

test('P04-06 blocks fresh intake during an enforced outage and allows an observe-only shop', async ({ page }, info) => {
  const server = await startPurchaseReceivingServer(BUILD);
  let enforced = true;
  let checks = 0;
  let posts = 0;
  try {
    await page.addInitScript(({ key, value }) => {
      if (!localStorage.getItem(key)) localStorage.setItem(key, JSON.stringify(value));
    }, { key: STORAGE, value: session() });
    await page.route('**/api/licensing/shops/*/trading-authority', route => {
      checks++;
      return route.fulfill({ status: 503, json: { code: 'Licence.VerificationUnavailable', enforced } });
    });
    await page.route('**/api/inventory/inbound/batch', async route => {
      posts++;
      const { items } = route.request().postDataJSON();
      await route.fulfill({ status: 200, json: { requestedCount: 2, successCount: 2, failedCount: 0, failed: [],
        succeeded: items.map((row: { clientRowId: string }) => ({ clientRowId: row.clientRowId,
          result: { ...row, itemId: 'item-' + row.clientRowId, inventoryBatchId: 'lot-' + row.clientRowId,
            stockTransactionId: 'movement-' + row.clientRowId, batchQuantity: 2, totalQuantity: 2 } })),
      } });
    });
    await page.goto(server.origin);
    await seedRows(page);
    await page.goto(server.origin + '/inventory/batch');
    const notice = page.locator('.inventory-access-notice');
    const save = page.getByRole('button', { name: 'Save', exact: true });
    await expect(notice).toContainText('Trading access could not be verified.');
    await expect(save).toBeDisabled();
    await expect(page.locator('.row-count')).toHaveText('Rows: 2');
    await page.screenshot({ path: info.outputPath('outage.png'), fullPage: true });
    expect(posts).toBe(0);
    enforced = false;
    await notice.getByRole('button', { name: 'Check again' }).click();
    await expect(notice).toHaveCount(0);
    await expect(save).toBeEnabled();
    await save.click();
    await expect(page.locator('.save-summary')).toContainText('2/2');
    expect(posts).toBe(1);
  } finally {
    await info.attach('local-api-fixture', { body: JSON.stringify({ checks, posts }), contentType: 'application/json' });
    await server.close();
  }
});
