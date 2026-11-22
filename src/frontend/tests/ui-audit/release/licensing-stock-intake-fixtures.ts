import { resolve } from 'node:path';
import { expect, type Page, type Route } from '@playwright/test';
import { session, SHOP_A, startPurchaseReceivingServer } from './purchase-receiving-server';

export const STORAGE = 'inventory.auth.session.local';
const DB = 'intelibill-offline-drafts';
const DRAFT = 'inventory-inbound-batch';
const SUBMISSION = 'inventory-inbound-submission';
export const BUILD = resolve(__dirname, '../../../dist/INVENTORY/browser');

export async function seedRows(page: Page, ids = ['A', 'B']) {
  await page.evaluate(({ db, store, shop, ids }) => new Promise<void>((done, reject) => {
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
        rows: ids.map(id => ({
          clientRowId: id, itemName: 'Fixture goods ' + id, barcode: id, batchNumber: id, uom: 'pcs',
          quantity: 2, totalPurchaseCost: 20, mrp: 20, salesPrice: 16, taxRatePercent: 0,
          taxIncluded: false, purchaseTaxIncluded: false, purchaseTaxModeConfirmed: true,
        })),
      });
      tx.oncomplete = () => { opened.result.close(); done(); };
      tx.onerror = () => reject(tx.error);
    };
  }), { db: DB, store: DRAFT, shop: SHOP_A, ids });
}

export function frozen(page: Page) {
  return records(page, SUBMISSION);
}

export function records(page: Page, store: string, db = DB) {
  return page.evaluate(({ db, store }) => new Promise<unknown[]>((done, reject) => {
    const opened = indexedDB.open(db);
    opened.onerror = () => reject(opened.error);
    opened.onsuccess = () => {
      const read = opened.result.transaction(store).objectStore(store).getAll();
      read.onsuccess = () => { opened.result.close(); done(read.result); };
      read.onerror = () => reject(read.error);
    };
  }), { db, store });
}

export const endpoint = '**/api/inventory/inbound/batch';
export const form = (page: Page) => page.locator('.row-form');
export const save = (page: Page) => page.getByRole('button', { name: 'Save', exact: true });
export const finish = (page: Page) => page.getByRole('button', { name: 'Finish save', exact: true });
export const add = (page: Page) => page.getByRole('button', { name: 'Add Row', exact: true });
export const count = (page: Page) => page.locator('.row-count');
export const name = (page: Page) => page.locator('.item-name-autocomplete input');

export async function withIntake(page: Page, run: (origin: string) => Promise<void>) {
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

export function intakeResult(route: Route, successfulRows = Infinity) {
  const { items } = route.request().postDataJSON();
  const succeeded = items.slice(0, successfulRows).map((row: { clientRowId: string }) => ({
    clientRowId: row.clientRowId,
    result: { ...row, itemId: 'item-' + row.clientRowId, inventoryBatchId: 'lot-' + row.clientRowId,
      stockTransactionId: 'movement-' + row.clientRowId, batchQuantity: 2, totalQuantity: 2 },
  }));
  return { requestedCount: items.length, successCount: succeeded.length, failedCount: 0, failed: [], succeeded };
}

export async function openSibling(page: Page, origin: string) {
  const sibling = await page.context().newPage();
  await sibling.goto(origin + '/inventory/batch');
  return sibling;
}

export async function enterRow(page: Page, id: string) {
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
