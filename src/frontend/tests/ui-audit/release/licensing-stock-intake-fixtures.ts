import { resolve } from 'node:path';
import type { Page } from '@playwright/test';
import { SHOP_A } from './purchase-receiving-server';

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
