import { expect, type Page, type Route } from '@playwright/test';
import { session, SHOP_A, shops } from './purchase-receiving-server';

export const batch = {
  id: 'batch-menu',
  shopId: SHOP_A,
  itemId: 'item-menu',
  itemName: 'Menu test milk',
  barcode: 'MENU-MILK',
  batchNumber: 'MENU-LOT',
  quantity: 10,
  originalQuantity: 10,
  costPrice: 40,
  salesPrice: 50,
  mrp: 60,
  taxRatePercent: 0,
  taxIncluded: false,
  expiryDate: null,
  manufacturingDate: null,
  supplierId: 'supplier-menu',
  supplierName: 'Menu supplier',
  isVoided: false,
  createdAt: '2026-11-09T08:00:00Z',
  updatedAt: null,
  version: 8,
  quantityScale: 0,
  uom: 'pcs',
  receiptValueId: 'receipt-menu',
  receiptStatus: 'Resolved',
  receiptOriginalTotalCost: 400,
  receiptTotalCost: 400,
  receiptVersion: 3,
};
export const targetBatch = {
  ...batch,
  id: 'batch-target',
  itemName: 'Target yoghurt',
  batchNumber: 'TARGET-LOT',
  receiptValueId: 'receipt-target',
};
export const json = (route: Route, value: unknown) =>
  route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify(value),
  });

type WireCorrection = {
  expectedVersion: number;
  revisedTotalCost: number;
  reason: string;
  operationKey: string;
};
type ReceiptDraftEvidence = {
  shopId: string;
  targetId: string;
  pending: { status: string; operationKey: string; payload: WireCorrection } | null;
};
type LookupMode = 'denied' | 'mismatch' | 'match' | 'miss' | 'other-404';

const correctionPath = '**/api/inventory/receipts/receipt-menu/cost-corrections';
const lookupPath = '**/api/financial-operations/receipt-cost-correction/*';
const savedCorrection = (body: WireCorrection) => ({
  adjustmentId: 'a0000000-0000-4000-8000-000000000001',
  supplierLedgerEntryId: 'a0000000-0000-4000-8000-000000000002',
  batchId: batch.id,
  receiptValueId: batch.receiptValueId,
  priorTotalCost: body.expectedVersion === 3 ? 400 : 500,
  revisedTotalCost: body.revisedTotalCost,
  delta: body.revisedTotalCost - (body.expectedVersion === 3 ? 400 : 500),
  receiptVersion: body.expectedVersion + 1,
  performedAt: '2026-11-21T01:00:00Z',
});

export function receiptDrafts(page: Page): Promise<ReceiptDraftEvidence[]> {
  return page.evaluate(() => new Promise((resolve, reject) => {
    const opening = indexedDB.open('intelibill-receipt-cost-corrections', 3);
    opening.onerror = () => reject(opening.error);
    opening.onsuccess = () => {
      const db = opening.result;
      const tx = db.transaction('drafts', 'readonly');
      const records = tx.objectStore('drafts').getAll();
      tx.oncomplete = () => { db.close(); resolve(records.result); };
      tx.onerror = () => { db.close(); reject(tx.error); };
    };
  }));
}

export async function installReceiptFixture(page: Page, role = 'Owner') {
  const initial = session();
  initial.shops = initial.shops.map((shop) => ({ ...shop, role }));
  await page.addInitScript((value) => {
    const key = 'inventory.auth.session.local';
    if (!localStorage.getItem(key)) localStorage.setItem(key, JSON.stringify(value));
  }, initial);
  await page.route('**/api/shops/me', (route) => json(route, shops.map((shop) => ({ ...shop, role }))));
  const state = {
    rows: [batch],
    security: false,
    tradingCode: 'Licence.Expired',
    dropNext: false,
    conflictNext: false,
    lookup: 'match' as LookupMode,
    posts: [] as WireCorrection[],
    postBodies: [] as string[],
    sequence: [] as string[],
  };
  await page.route('**/api/inventory/batches', (route) => state.security
    ? route.fulfill({ status: 403, json: { code: 'Licence.SecuritySuspended' } })
    : json(route, state.rows));
  await page.route('**/api/licensing/shops/*/trading-authority', (route) =>
    route.fulfill({ status: 403, json: {
      code: state.security ? 'Licence.SecuritySuspended' : state.tradingCode,
      enforced: true,
    } }));
  await page.route(correctionPath, (route) => {
    const body = route.request().postDataJSON() as WireCorrection;
    state.posts.push(body);
    state.postBodies.push(route.request().postData()!);
    state.sequence.push('POST');
    if (state.dropNext) {
      state.dropNext = false;
      return route.abort('failed');
    }
    if (state.conflictNext) {
      state.conflictNext = false;
      return route.fulfill({ status: 409, json: { code: 'PurchaseReceipt.VersionConflict' } });
    }
    return json(route, savedCorrection(body));
  });
  await page.route(lookupPath, (route) => {
    const key = new URL(route.request().url()).pathname.split('/').at(-1);
    expect(key).toBe(state.posts[0].operationKey);
    state.sequence.push(`GET ${state.lookup}`);
    if (state.lookup === 'denied')
      return route.fulfill({ status: 403, json: { code: 'Licence.SecuritySuspended' } });
    if (state.lookup === 'miss' || state.lookup === 'other-404')
      return route.fulfill({ status: 404, json: { code: state.lookup === 'miss' ? 'Operation.NotFound' : 'Other.NotFound' } });
    return json(route, {
      operationType: 'receipt-cost-correction',
      operationKey: key,
      targetId: state.lookup === 'mismatch' ? 'different-receipt' : batch.receiptValueId,
      response: savedCorrection(state.posts[0]),
    });
  });
  return state;
}

export async function openReceiptCorrection(page: Page) {
  await page.getByRole('button', { name: 'Actions', exact: true }).filter({ visible: true }).first().click();
  await page.getByRole('menuitem', { name: 'Correct receipt cost', exact: true }).click();
  return page.getByRole('dialog', { name: 'Correct receipt cost', exact: true });
}

export async function enterReceiptCorrection(page: Page, amount: string) {
  const dialog = await openReceiptCorrection(page);
  await dialog.locator('#revisedTotalCost').fill(amount);
  await dialog.locator('#receiptCostReason').fill('Fixture supplier invoice');
  return dialog;
}

export async function switchReceiptShop(page: Page, name: string) {
  await page.locator('.active-shop-trigger-desktop').click();
  await Promise.all([
    page.waitForURL((url) => url.pathname === '/dashboard', { waitUntil: 'load' }),
    page.locator('.shop-menu-item').filter({ hasText: name }).click(),
  ]);
}
