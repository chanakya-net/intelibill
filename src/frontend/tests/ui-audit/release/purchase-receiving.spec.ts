import type { ReceivingDraftRecord } from '../../../src/app/features/purchase-orders/services/receiving.models';
import { existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test, type Locator, type Page } from '@playwright/test';
import {
  OTHER_PO,
  PO,
  session,
  SHOP_A,
  SHOP_B,
  startPurchaseReceivingServer,
  USER,
} from './purchase-receiving-server';

/** Build first: bun run build. Run with playwright.release.config.ts. Uses shipped Angular routes,
 * real IndexedDB and effects. Fixture ledger counts prove UI behavior only; xUnit proves posting. */
const BUILD_ROOT = resolve(__dirname, '../../../dist/INVENTORY/browser');
let server: Awaited<ReturnType<typeof startPurchaseReceivingServer>>;
test.beforeAll(() => {
  if (!existsSync(`${BUILD_ROOT}/index.html`)) throw new Error('Run bun run build first.');
});
test.beforeEach(async ({ page }) => {
  server = await startPurchaseReceivingServer(BUILD_ROOT);
  await page.addInitScript((value) => {
    if (!localStorage.getItem('inventory.auth.session.local'))
      localStorage.setItem('inventory.auth.session.local', JSON.stringify(value));
  }, session());
});
test.afterEach(async () => {
  await server.close();
});
const dialog = (page: Page) =>
  page.getByRole('dialog', { name: 'Receive purchase order', exact: true });
async function open(page: Page, entry: 'detail' | 'list' = 'detail', id = PO) {
  await page.goto(
    `${server.origin}/inventory/purchase-orders${entry === 'detail' ? `/${id}` : ''}`,
  );
  const trigger =
    entry === 'detail'
      ? page.locator('.po-detail-actions')
      : page.locator('tr').filter({ hasText: id === PO ? 'PO-RECEIVING-1' : 'PO-RECEIVING-2' });
  await trigger.getByRole('button', { name: /^(Receive|Recover receipt)$/ }).click();
  await expect(dialog(page)).toBeVisible();
  await expect(dialog(page).getByRole('button', { name: 'Cancel', exact: true })).toBeEnabled();
}
async function fillNumber(row: Locator, name: string, value: string) {
  const input = row.locator(`p-inputnumber[formcontrolname="${name}"] input`);
  await input.fill(value);
  await input.press('Tab');
}
async function fillLot(page: Page, index: number, batch: string, quantity: number, cost: number) {
  const row = dialog(page).getByTestId('receipt-row').nth(index);
  await row.locator('app-inventory-barcode-field input').first().fill('REPEATED-PRODUCT');
  await row.locator('[formcontrolname="batchNumber"]').fill(batch);
  for (const [name, value] of Object.entries({
    quantity,
    totalPurchaseCost: cost,
    mrp: 30,
    salesPrice: 20,
    taxRatePercent: 5,
  }))
    await fillNumber(row, name, String(value));
  for (const [name, value] of Object.entries({
    expiryDate: '31/12/2027',
    manufacturingDate: '01/11/2026',
  })) {
    const input = row.locator(`p-datepicker[formcontrolname="${name}"] input`);
    await input.fill('');
    await input.pressSequentially(value);
    await input.press('Tab');
    await expect(input).toHaveValue(value);
  }
  await row.locator('p-checkbox[formcontrolname="taxIncluded"] input').uncheck();
  await row.locator('p-checkbox[formcontrolname="purchaseTaxIncluded"] input').check();
}
async function submit(page: Page) {
  await dialog(page).getByRole('button', { name: 'Receive', exact: true }).click();
}
async function unknown(page: Page) {
  await expect(
    dialog(page).getByRole('status').filter({ hasText: 'Receipt outcome is unknown' }),
  ).toBeVisible();
  await expect(dialog(page).locator('[formcontrolname="batchNumber"]').first()).toBeDisabled();
}
async function record(page: Page, shopId = SHOP_A, poId = PO) {
  return page.evaluate(
    async ({ userId, shopId, poId }) => {
      const db = await new Promise<IDBDatabase>((resolve, reject) => {
        const r = indexedDB.open('intelibill-purchase-receiving', 2);
        r.onsuccess = () => resolve(r.result);
        r.onerror = () => reject(r.error);
      });
      try {
        return await new Promise<ReceivingDraftRecord | null>((resolve, reject) => {
          const r = db.transaction('drafts').objectStore('drafts').get([userId, shopId, poId]);
          r.onsuccess = () => resolve(r.result ?? null);
          r.onerror = () => reject(r.error);
        });
      } finally {
        db.close();
      }
    },
    { userId: USER, shopId, poId },
  );
}
async function closeDialog(page: Page) {
  await dialog(page).getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(dialog(page)).toBeHidden();
}

for (const entry of ['detail', 'list'] as const)
  test(`${entry}: rejection preserves editable fields across changed PO responses`, async ({
    page,
  }) => {
    server.setMode('reject');
    await open(page, entry);
    await fillLot(page, 0, 'MANUAL-A', 4, 40);
    await dialog(page).locator('[formcontrolname="referenceNumber"]').fill('MY-REF');
    await dialog(page).locator('[formcontrolname="notes"]').fill('Do not lose these notes');
    await submit(page);
    await expect.poll(() => server.posts.length).toBe(1);
    await expect(dialog(page).getByRole('alert').first()).toBeVisible();
    const row = dialog(page).getByTestId('receipt-row');
    await expect(row.locator('[formcontrolname="batchNumber"]')).toBeEditable();
    await expect(row.locator('[formcontrolname="batchNumber"]')).toHaveValue('MANUAL-A');
    await expect(row.locator('app-inventory-barcode-field input').first()).toHaveValue(
      'REPEATED-PRODUCT',
    );
    await expect(
      row.locator('p-inputnumber[formcontrolname="totalPurchaseCost"] input'),
    ).toHaveValue('40.00');
    await expect(row.locator('p-inputnumber[formcontrolname="taxRatePercent"] input')).toHaveValue(
      '5',
    );
    await expect(row.locator('p-datepicker[formcontrolname="expiryDate"] input')).toHaveValue(
      '31/12/2027',
    );
    await expect(
      row.locator('p-datepicker[formcontrolname="manufacturingDate"] input'),
    ).toHaveValue('01/11/2026');
    await expect(
      row.locator('p-checkbox[formcontrolname="purchaseTaxIncluded"] input'),
    ).toBeChecked();
    await expect(row.locator('p-checkbox[formcontrolname="taxIncluded"] input')).not.toBeChecked();
    server.changeDescription(PO);
    await closeDialog(page);
    await open(page, entry);
    await expect(dialog(page).locator('[formcontrolname="batchNumber"]')).toHaveValue('MANUAL-A');
    await expect(dialog(page).locator('[formcontrolname="notes"]')).toHaveValue(
      'Do not lose these notes',
    );
    expect((await record(page))!.draft.lines[0]).toMatchObject({
      batchNumber: 'MANUAL-A',
      totalPurchaseCost: 40,
      expiryDate: '2027-12-31',
      manufacturingDate: '2026-11-01',
      taxIncluded: false,
      purchaseTaxIncluded: true,
    });
  });

test('4 + 6 lots submit once and render two history rows', async ({ page }) => {
  await open(page);
  await fillLot(page, 0, 'A', 4, 40);
  await dialog(page).getByRole('button', { name: 'Add another lot' }).click();
  await dialog(page)
    .getByTestId('receipt-row')
    .nth(1)
    .locator('p-select[formcontrolname="purchaseOrderLineId"]')
    .click();
  await page.getByRole('option', { name: 'Widget', exact: true }).click();
  await fillLot(page, 1, 'B', 6, 90);
  await submit(page);
  await expect(dialog(page)).toBeHidden();
  expect(server.posts).toHaveLength(1);
  expect(server.operations.size).toBe(1);
  expect(server.posts[0].payload.lines.map((l) => l.quantity)).toEqual([4, 6]);
  const rows = page.locator('app-purchase-order-receipt-history tbody tr');
  await expect(rows).toHaveCount(2);
  await expect(rows.nth(0)).toContainText('40.00');
  await expect(rows.nth(1)).toContainText('90.00');
  await expect(rows.nth(0).getByRole('link', { name: 'A', exact: true })).toHaveAttribute(
    'href',
    /batchId=batch-0/,
  );
  await expect(rows.nth(1)).toContainText('stock-1');
});

for (const status of ['Received', 'Closed'] as const)
  test(`lost response reloads frozen and confirms original on ${status} PO`, async ({ page }) => {
    server.setMode('accept-drop');
    await open(page);
    await fillLot(page, 0, 'LOST', 10, 100);
    await submit(page);
    await unknown(page);
    const original = server.posts[0];
    expect((await record(page))!.pending!.payload).toEqual(original.payload);
    server.setStatus(PO, status);
    await open(page);
    await unknown(page);
    expect((await record(page))!.pending!.operationId).toBe(original.payload.operationId);
    await dialog(page).getByRole('button', { name: 'Check receipt', exact: true }).click();
    await expect(dialog(page)).toBeHidden();
    expect(server.lookups).toEqual([original.payload.operationId]);
    expect(server.posts).toHaveLength(1);
    expect(server.operations.size).toBe(1);
    await expect(page.locator('app-purchase-order-receipt-history tbody tr')).toHaveCount(1);
    await open(page);
    await expect(dialog(page).getByRole('button', { name: 'Retry same receipt' })).toHaveCount(0);
  });

test('lookup miss and retry 404 stay frozen; subsequent retry sends identical operation', async ({
  page,
}) => {
  server.setMode('drop');
  await open(page);
  await fillLot(page, 0, 'PENDING', 4, 40);
  await submit(page);
  await unknown(page);
  const original = server.posts[0];
  await dialog(page).getByRole('button', { name: 'Check receipt', exact: true }).click();
  await expect(dialog(page).getByRole('alert')).toContainText('No committed receipt');
  expect((await record(page))!.pending!.payload).toEqual(original.payload);
  server.setMode('retry-404');
  await dialog(page).getByRole('button', { name: 'Retry same receipt' }).click();
  await expect.poll(() => server.posts.length).toBe(2);
  await unknown(page);
  await expect(dialog(page).getByRole('button', { name: 'Retry same receipt' })).toBeEnabled();
  expect(server.posts[1].raw).toBe(original.raw);
  await dialog(page).locator('.p-dialog-header button').click();
  await expect(dialog(page)).toBeHidden();
  await open(page);
  await unknown(page);
  server.setMode('accept');
  await dialog(page).getByRole('button', { name: 'Retry same receipt' }).click();
  await expect(dialog(page)).toBeHidden();
  expect(server.posts[2].raw).toBe(original.raw);
  expect(server.operations.size).toBe(1);
});

test('storage rejection prevents network dispatch and retains editable values', async ({
  page,
}) => {
  await open(page);
  await fillLot(page, 0, 'UNSAVED', 4, 40);
  await page.evaluate(() => {
    const open = indexedDB.open.bind(indexedDB);
    indexedDB.open = ((name: string, version?: number) => {
      if (name === 'intelibill-purchase-receiving')
        throw new DOMException('Fixture storage rejection', 'QuotaExceededError');
      return open(name, version);
    }) as typeof indexedDB.open;
  });
  await submit(page);
  await expect(dialog(page).getByRole('alert')).toContainText('Unable to save receiving work');
  expect(server.posts).toHaveLength(0);
  await expect(dialog(page).locator('[formcontrolname="batchNumber"]')).toHaveValue('UNSAVED');
  await expect(dialog(page).locator('[formcontrolname="batchNumber"]')).toBeEditable();
});

for (const failure of ['already known', 'on closing flush'] as const)
  test(`header close retains editable work when storage fails ${failure}`, async ({ page }) => {
    await open(page);
    await fillLot(page, 0, 'UNSAVED-X', 4, 40);
    await dialog(page).locator('[formcontrolname="notes"]').fill('manual notes');
    await expect.poll(async () => (await record(page))?.draft.notes).toBe('manual notes');
    await expect(dialog(page).getByRole('alert')).toHaveCount(0);
    await page.evaluate(() => {
      const original = indexedDB.open.bind(indexedDB);
      const state = window as unknown as {
        restoreReceivingStorage: () => void;
        receivingStorageFailures: number;
      };
      state.receivingStorageFailures = 0;
      state.restoreReceivingStorage = () => {
        indexedDB.open = original;
      };
      indexedDB.open = ((name: string, version?: number) => {
        if (name === 'intelibill-purchase-receiving') {
          state.receivingStorageFailures++;
          throw new DOMException('Fixture closing storage rejection', 'QuotaExceededError');
        }
        return original(name, version);
      }) as typeof indexedDB.open;
    });
    if (failure === 'already known') {
      await dialog(page).locator('[formcontrolname="notes"]').fill('manual unsaved notes');
      await expect(dialog(page).getByRole('alert')).toContainText('Unable to save receiving work');
    }
    const failuresBeforeClose = await page.evaluate(
      () => (window as unknown as { receivingStorageFailures: number }).receivingStorageFailures,
    );
    await dialog(page).locator('.p-dialog-header button').click();
    await expect
      .poll(() =>
        page.evaluate(
          () =>
            (window as unknown as { receivingStorageFailures: number }).receivingStorageFailures,
        ),
      )
      .toBeGreaterThan(failuresBeforeClose);
    await expect(dialog(page)).toBeVisible();
    await expect(dialog(page).getByRole('alert')).toContainText('Unable to save receiving work');
    await expect(dialog(page).locator('[formcontrolname="batchNumber"]')).toHaveValue('UNSAVED-X');
    await expect(dialog(page).locator('[formcontrolname="batchNumber"]')).toBeEditable();
    const notes = failure === 'already known' ? 'manual unsaved notes' : 'manual notes';
    await expect(dialog(page).locator('[formcontrolname="notes"]')).toHaveValue(notes);
    expect(server.posts).toHaveLength(0);
    await expect(dialog(page).locator('.p-dialog-header button')).toHaveAccessibleName('Close');
    await page.keyboard.press('Escape');
    await expect(dialog(page)).toBeVisible();
    await page.evaluate(() =>
      (window as unknown as { restoreReceivingStorage: () => void }).restoreReceivingStorage(),
    );
    if (failure === 'already known') {
      await dialog(page).locator('.p-dialog-header button').focus();
      await page.keyboard.press('Enter');
    } else await dialog(page).locator('.p-dialog-header button').click();
    await expect(dialog(page)).toBeHidden();
    await open(page);
    await expect(dialog(page).locator('[formcontrolname="batchNumber"]')).toHaveValue('UNSAVED-X');
    await expect(dialog(page).locator('[formcontrolname="notes"]')).toHaveValue(notes);
    await expect(
      dialog(page).locator('p-inputnumber[formcontrolname="totalPurchaseCost"] input'),
    ).toHaveValue('40.00');
    expect(server.posts).toHaveLength(0);
  });

test('late unrelated success cannot close or overwrite another active PO draft', async ({
  page,
}) => {
  server.setMode('hold');
  await open(page);
  await fillLot(page, 0, 'HELD-A', 4, 40);
  await submit(page);
  await expect.poll(() => server.posts.length).toBe(1);
  await closeDialog(page);
  // SPA navigation keeps the original effect alive while another PO dialog is being edited.
  await page.locator('.po-detail-back').click();
  await page
    .locator('tr')
    .filter({ hasText: 'PO-RECEIVING-2' })
    .getByRole('button', { name: 'Receive', exact: true })
    .click();
  await expect(dialog(page)).toBeVisible();
  await fillLot(page, 0, 'ACTIVE-B', 3, 33);
  server.releaseHeld();
  await expect.poll(() => server.operations.size).toBe(1);
  await expect.poll(async () => (await record(page))?.pending ?? null).toBeNull();
  await expect(dialog(page)).toBeVisible();
  await expect(dialog(page).locator('[formcontrolname="batchNumber"]')).toHaveValue('ACTIVE-B');
  await expect(dialog(page).locator('[formcontrolname="batchNumber"]')).toBeEditable();
  expect((await record(page, SHOP_A, OTHER_PO))!.draft.lines[0].batchNumber).toBe('ACTIVE-B');
});

async function switchShop(page: Page, label: string) {
  await page.locator('.active-shop-trigger-desktop').click();
  // Installing the new session changes the label before pending draft saves finish.
  // The product then deliberately restarts at '/', which redirects these Owners to dashboard.
  // Wait for that new-document navigation before opening a scoped PO route.
  await Promise.all([
    page.waitForURL((url) => url.pathname === '/dashboard', { waitUntil: 'load' }),
    page.locator('.shop-menu-item').filter({ hasText: label }).click(),
  ]);
  await expect(page.locator('.active-shop-trigger-desktop')).toContainText(label);
}

test('scope transition detaches pending work; original owner recovers exact operation on return', async ({
  page,
}) => {
  server.setMode('accept-drop');
  await open(page);
  await fillLot(page, 0, 'OWNER-A', 4, 40);
  await submit(page);
  await unknown(page);
  const original = server.posts[0].payload;
  await closeDialog(page);
  await switchShop(page, 'Shop B');
  await open(page);
  await expect(dialog(page).getByRole('button', { name: 'Retry same receipt' })).toHaveCount(0);
  await fillLot(page, 0, 'OWNER-B', 2, 20);
  await closeDialog(page);
  expect((await record(page, SHOP_B))!.draft.lines[0].batchNumber).toBe('OWNER-B');
  expect((await record(page, SHOP_A))!.pending!.payload).toEqual(original);
  await switchShop(page, 'Shop A');
  await open(page);
  await unknown(page);
  await expect(dialog(page).locator('[formcontrolname="batchNumber"]')).toHaveValue('OWNER-A');
  await dialog(page).getByRole('button', { name: 'Check receipt', exact: true }).click();
  await expect(dialog(page)).toBeHidden();
  expect(server.posts).toHaveLength(1);
  expect(server.lookups).toEqual([original.operationId]);
  expect((await record(page, SHOP_B))!.draft.lines[0].batchNumber).toBe('OWNER-B');
});
