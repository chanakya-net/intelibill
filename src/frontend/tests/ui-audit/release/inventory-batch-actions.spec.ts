import { expect, test, type Page, type Route } from '@playwright/test';
import { resolve } from 'node:path';
import { session, SHOP_A, shops, startPurchaseReceivingServer } from './purchase-receiving-server';

test.use({ serviceWorkers: 'block', trace: 'on' });

const batch = {
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
const targetBatch = {
  ...batch,
  id: 'batch-target',
  itemName: 'Target yoghurt',
  batchNumber: 'TARGET-LOT',
  receiptValueId: 'receipt-target',
};
const json = (route: Route, value: unknown) =>
  route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify(value),
  });

for (const width of [1440, 390]) {
  test(`batch menu pointer actions at ${width}px`, async ({ page }) => {
    const server = await startPurchaseReceivingServer(
      resolve(__dirname, '../../../dist/INVENTORY/browser'),
    );
    await page.setViewportSize({ width, height: 1000 });
    await page.addInitScript(
      (value) => localStorage.setItem('inventory.auth.session.local', JSON.stringify(value)),
      session(),
    );
    const voids: string[] = [];
    await page.route('**/api/inventory/batches', (route) => json(route, [batch, targetBatch]));
    await page.route('**/api/inventory/batches/batch-target/void', (route) => {
      voids.push(route.request().method());
      return route.fulfill({ status: 204 });
    });
    try {
      for (const label of [
        'Adjust Batch',
        'Count stock',
        'Edit prices & details',
        'Correct receipt cost',
        'Print labels',
        'Void',
      ]) {
        await page.goto(`${server.origin}/inventory/batches`);
        await page
          .getByRole('button', { name: 'Actions', exact: true })
          .filter({ visible: true })
          .last()
          .click();
        const item = page.locator('.p-menu:visible').getByText(label, { exact: true });
        await expect(item).toBeVisible();
        // Exercise pointer focus/blur between down and up, like a physical click.
        await item.click({ delay: 80 });
        if (label === 'Void') await expect.poll(() => voids).toEqual(['POST']);
        else {
          const dialog = page.getByRole('dialog');
          await expect(dialog).toBeVisible();
          await expect(dialog).toContainText(
            label === 'Print labels' ? targetBatch.itemName : targetBatch.batchNumber,
          );
          if (label !== 'Print labels') await expect(dialog).toHaveAccessibleName(label);
        }
      }
      await page.goto(`${server.origin}/inventory/batches`);
      const actions = page
        .getByRole('button', { name: 'Actions', exact: true })
        .filter({ visible: true });
      await actions.first().click();
      await expect(page.getByRole('menu')).toBeVisible();
      await actions.last().click();
      await expect(page.getByRole('menu')).toBeVisible();
      await page.getByRole('menuitem', { name: 'Adjust Batch', exact: true }).click({ delay: 80 });
      await expect(page.getByRole('dialog', { name: 'Adjust Batch', exact: true })).toContainText(
        targetBatch.batchNumber,
      );
      await page.goto(`${server.origin}/inventory/batches`);
      await actions.first().click();
      await expect(page.getByRole('menu')).toBeVisible();
      await actions.first().click();
      await expect(page.getByRole('menu')).toBeHidden();
      // Reopening for another row refreshes the shared popup's target.
      await actions.first().focus();
      await page.keyboard.press('Enter');
      const menu = page.getByRole('menu');
      await expect(menu).toBeFocused();
      await page.keyboard.press('Home');
      await expect(menu).toHaveAttribute('aria-activedescendant', 'printLabels');
      await page.keyboard.press('ArrowDown');
      await expect(menu).toHaveAttribute('aria-activedescendant', 'adjust');
      await page.keyboard.press('Enter');
      await expect(page.getByRole('dialog', { name: 'Adjust Batch', exact: true })).toContainText(
        batch.batchNumber,
      );
    } finally {
      await server.close();
    }
  });
}

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

function receiptDrafts(page: Page): Promise<ReceiptDraftEvidence[]> {
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

async function installReceiptFixture(page: Page, role = 'Owner') {
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

async function openReceiptCorrection(page: Page) {
  await page.getByRole('button', { name: 'Actions', exact: true }).filter({ visible: true }).first().click();
  await page.getByRole('menuitem', { name: 'Correct receipt cost', exact: true }).click();
  return page.getByRole('dialog', { name: 'Correct receipt cost', exact: true });
}

async function enterReceiptCorrection(page: Page, amount: string) {
  const dialog = await openReceiptCorrection(page);
  await dialog.locator('#revisedTotalCost').fill(amount);
  await dialog.locator('#receiptCostReason').fill('Fixture supplier invoice');
  return dialog;
}

async function switchReceiptShop(page: Page, name: string) {
  await page.locator('.active-shop-trigger-desktop').click();
  await Promise.all([
    page.waitForURL((url) => url.pathname === '/dashboard', { waitUntil: 'load' }),
    page.locator('.shop-menu-item').filter({ hasText: name }).click(),
  ]);
}

test.describe('P04-13d receipt correction recovery', () => {
  const build = resolve(__dirname, '../../../dist/INVENTORY/browser');
  const check = (page: Page) => page.getByTestId('receipt-cost-recovery').getByRole('button', { name: 'Check correction outcome' });

  test('P04-13d desktop restores a lost correction across shops and confirms without reposting', async ({ page, context }, info) => {
    const server = await startPurchaseReceivingServer(build);
    const url = `${server.origin}/inventory/batches`;
    const state = await installReceiptFixture(page);
    state.dropNext = true;
    await page.setViewportSize({ width: 1440, height: 1000 });
    try {
      await page.goto(url);
      const dialog = await enterReceiptCorrection(page, '500');
      await expect(dialog.getByText('This shop’s trading access has ended.')).toBeVisible();
      await expect(dialog.getByTestId('receipt-cost-delta')).toContainText('+₹100.00');
      await page.screenshot({ animations: 'disabled', path: info.outputPath('signed-delta-desktop.png') });
      await dialog.getByRole('button', { name: 'Post correction' }).click();
      await expect(check(page)).toBeEnabled();
      expect(state.posts).toHaveLength(1);
      const frozen = state.posts[0];
      await expect.poll(() => receiptDrafts(page)).toMatchObject([{
        shopId: SHOP_A, targetId: 'receipt-menu:3',
        pending: { status: 'unknown', operationKey: frozen.operationKey, payload: frozen },
      }]);

      await page.reload();
      await expect(check(page)).toBeEnabled();
      await switchReceiptShop(page, 'Shop B');
      await page.goto(url);
      await expect(check(page)).toHaveCount(0);
      await switchReceiptShop(page, 'Shop A');
      state.rows = [targetBatch];
      await page.goto(url);
      await expect(check(page)).toBeEnabled();

      const otherTab = await context.newPage();
      await installReceiptFixture(otherTab);
      await otherTab.goto(url);
      await expect(check(otherTab)).toBeEnabled();

      await expect(page.getByText(targetBatch.itemName, { exact: true }).filter({ visible: true }).first()).toBeVisible();
      state.lookup = 'denied';
      state.security = true;
      await check(page).click();
      await expect(page.locator('section.batches-page > p[role=alert]').filter({ hasText: 'Security suspension is enforced for this shop.' })).toBeVisible();
      await expect(check(page)).toBeEnabled();
      await expect(page.getByTestId('receipt-cost-recovery')).not.toContainText(batch.batchNumber);
      await expect(page.getByText(targetBatch.itemName, { exact: true })).toHaveCount(0);
      expect(state.posts).toHaveLength(1);
      expect((await receiptDrafts(page))[0].pending?.payload).toEqual(frozen);
      await page.screenshot({ animations: 'disabled', path: info.outputPath('security-preserves-intent.png') });

      state.security = false;
      state.lookup = 'mismatch';
      await check(page).click();
      await expect.poll(() => state.sequence.at(-1)).toBe('GET mismatch');
      await expect(check(page)).toBeEnabled();
      expect(state.posts).toHaveLength(1);
      expect((await receiptDrafts(page))[0].pending?.payload).toEqual(frozen);

      state.lookup = 'match';
      state.rows = [{ ...batch, receiptVersion: 4, receiptTotalCost: 500 }];
      await check(page).click();
      await expect(check(page)).toHaveCount(0);
      await expect(check(otherTab)).toHaveCount(0);
      await expect.poll(() => receiptDrafts(page)).toEqual([]);
      expect(state.sequence).toEqual(['POST', 'GET denied', 'GET mismatch', 'GET match']);
      console.log('P04-13d desktop 1440px: persisted key suffix %s; original owner recovery after reload and shop switch; POST → denied GET → mismatched GET → matching GET; zero resends.', frozen.operationKey.slice(-8));

      const later = await enterReceiptCorrection(page, '450');
      await expect(later.getByTestId('receipt-cost-delta')).toContainText('-₹50.00');
      await later.getByRole('button', { name: 'Post correction' }).click();
      await expect(later).toBeHidden();
      expect(state.posts[1].operationKey).not.toBe(frozen.operationKey);
      expect(state.posts[1].expectedVersion).toBe(4);
      await page.screenshot({ animations: 'disabled', path: info.outputPath('confirmed-desktop.png') });
    } finally {
      await server.close();
    }
  });

  test('P04-13d narrow viewport only resends identical frozen JSON after a typed miss', async ({ page }, info) => {
    const server = await startPurchaseReceivingServer(build);
    const state = await installReceiptFixture(page);
    state.dropNext = true;
    state.tradingCode = 'Licence.TradingRestricted';
    await page.setViewportSize({ width: 390, height: 844 });
    try {
      await page.goto(`${server.origin}/inventory/batches`);
      const dialog = await enterReceiptCorrection(page, '500');
      await expect(dialog.getByText('Trading is restricted for this shop.')).toBeVisible();
      await expect(dialog.getByTestId('receipt-cost-delta')).toContainText('+₹100.00');
      await page.screenshot({ animations: 'disabled', path: info.outputPath('signed-delta-narrow.png') });
      await dialog.getByRole('button', { name: 'Post correction' }).click();
      await expect(check(page)).toBeEnabled();
      await page.reload();
      await expect(check(page)).toBeEnabled();

      state.lookup = 'other-404';
      await check(page).click();
      await expect.poll(() => state.sequence.at(-1)).toBe('GET other-404');
      await expect(check(page)).toBeEnabled();
      expect(state.posts).toHaveLength(1);

      state.lookup = 'miss';
      await check(page).click();
      await expect(check(page)).toHaveCount(0);
      expect(state.posts).toHaveLength(2);
      expect(state.postBodies[1]).toBe(state.postBodies[0]);
      expect(state.sequence).toEqual(['POST', 'GET other-404', 'GET miss', 'POST']);
      console.log('P04-13d narrow 390px: POST → unrelated 404 GET → Operation.NotFound GET → one identical POST; key suffix %s.', state.posts[0].operationKey.slice(-8));
      await page.screenshot({ animations: 'disabled', path: info.outputPath('confirmed-narrow.png') });
    } finally {
      await server.close();
    }
  });

  for (const role of ['Owner', 'Manager']) {
    test(`P04-13d ${role} sees the appropriate action for unresolved legacy evidence`, async ({ page }, info) => {
      const server = await startPurchaseReceivingServer(build);
      const state = await installReceiptFixture(page, role);
      state.rows = [{ ...batch, receiptStatus: 'LegacyUnresolved', receiptOriginalTotalCost: null,
        receiptTotalCost: null, receiptVersion: null }] as unknown as typeof state.rows;
      try {
        await page.goto(`${server.origin}/inventory/batches`);
        if (role === 'Owner') {
          const dialog = await openReceiptCorrection(page);
          await expect(dialog.getByTestId('receipt-cost-unresolved')).toBeVisible();
          await expect(dialog.getByRole('button', { name: 'Post correction' })).toHaveCount(0);
          await page.screenshot({ animations: 'disabled', path: info.outputPath('legacy-source-refusal.png') });
        } else {
          await page.getByRole('button', { name: 'Actions', exact: true }).filter({ visible: true }).first().click();
          await expect(page.getByRole('menu')).toBeVisible();
          await expect(page.getByRole('menuitem', { name: 'Correct receipt cost', exact: true })).toHaveCount(0);
        }
        expect(state.posts).toHaveLength(0);
      } finally {
        await server.close();
      }
    });
  }
});
