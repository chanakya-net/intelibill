import { resolve } from 'node:path';
import { expect, test, type Page, type TestInfo } from '@playwright/test';
import {
  OTHER_PO,
  PO,
  session,
  SHOP_A,
  startPurchaseReceivingServer,
  USER,
} from './purchase-receiving-server';

const buildRoot = resolve(__dirname, '../../../dist/INVENTORY/browser');
const listPath = '/inventory/purchase-orders';
const expired = 'This shop’s trading access has ended.';
let server: Awaited<ReturnType<typeof startPurchaseReceivingServer>>;

test.beforeEach(async ({ page }) => {
  server = await startPurchaseReceivingServer(buildRoot);
  await page.addInitScript((value) => {
    if (!localStorage.getItem('inventory.auth.session.local'))
      localStorage.setItem('inventory.auth.session.local', JSON.stringify(value));
  }, session());
});
test.afterEach(async () => server.close());

for (const viewport of [
  { width: 1280, height: 800 },
  { width: 390, height: 844 },
]) {
  test(`blocks fresh purchasing and keeps PO history available at ${viewport.width}px`, async ({
    page,
  }) => {
    await page.setViewportSize(viewport);
    server.setAccess('expired');
    server.setStatus(OTHER_PO, 'Draft');
    await page.goto(server.origin + listPath);
    await expect(page.getByText('PO-RECEIVING-1', { exact: true })).toBeVisible();
    const create = page.getByRole('button', { name: 'New Purchase Order' });
    await expect(create).toBeDisabled({ timeout: 5_000 });
    await expect(page.getByText(expired, { exact: true })).toBeVisible();
    const draft = page.locator('tr').filter({ hasText: 'PO-RECEIVING-2' });
    if (viewport.width < 400) {
      await draft.getByRole('button', { name: 'Actions' }).click();
      await expect(page.getByRole('menuitem', { name: 'Place Order' })).toHaveAttribute(
        'aria-disabled',
        'true',
      );
    } else {
      await expect(draft.getByRole('button', { name: 'Place Order' })).toBeDisabled();
    }
    await page.goto(`${server.origin}${listPath}/${PO}`);
    await expect(page.getByRole('button', { name: 'Receive', exact: true })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Recover receipt', exact: true })).toBeEnabled();
    expect(server.posts).toHaveLength(0);
    await page.goto(`${server.origin}${listPath}/${OTHER_PO}`);
    await expect(page.getByRole('button', { name: 'Place Order' })).toBeDisabled();
    await page.getByRole('button', { name: 'Edit Purchase Order Draft' }).click();
    await expect(page.getByRole('button', { name: 'Save Draft' })).toBeDisabled();
    expect(server.purchaseWrites).toHaveLength(0);
  });
}

const detailPath = `${listPath}/${PO}`;
const receiptDialog = (page: Page) =>
  page.getByRole('dialog', { name: 'Receive purchase order', exact: true });

async function fillReceipt(page: Page) {
  await page.goto(server.origin + detailPath);
  await page.getByRole('button', { name: 'Receive', exact: true }).click();
  const dialog = receiptDialog(page);
  const row = dialog.getByTestId('receipt-row');
  await row.locator('app-inventory-barcode-field input').first().fill('FIXTURE-PRODUCT');
  await row.locator('[formcontrolname="batchNumber"]').fill('LICENCE-FIXTURE');
  for (const [name, value] of Object.entries({
    quantity: 4,
    totalPurchaseCost: 40,
    mrp: 30,
    salesPrice: 20,
  })) {
    const input = row.locator(`p-inputnumber[formcontrolname="${name}"] input`);
    await input.fill(String(value));
    await input.press('Tab');
  }
  return dialog;
}

async function beginReceipt(page: Page) {
  server.setMode('accept-drop');
  const dialog = await fillReceipt(page);
  await dialog.getByRole('button', { name: 'Receive', exact: true }).click();
  await expect(
    dialog.getByRole('status').filter({ hasText: 'Receipt outcome is unknown' }),
  ).toBeVisible();
  return server.posts[0].payload;
}

async function pending(page: Page) {
  return page.evaluate(
    async (owner) => {
      const database = await new Promise<IDBDatabase>((resolve, reject) => {
        const open = indexedDB.open('intelibill-purchase-receiving', 2);
        open.onsuccess = () => resolve(open.result);
        open.onerror = () => reject(open.error);
      });
      try {
        return await new Promise<unknown>((resolve, reject) => {
          const read = database.transaction('drafts').objectStore('drafts').get(owner);
          read.onsuccess = () => resolve(read.result?.pending?.payload ?? null);
          read.onerror = () => reject(read.error);
        });
      } finally {
        database.close();
      }
    },
    [USER, SHOP_A, PO],
  );
}

test('recovers original receipt after restriction and reload', async ({ page }, info) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const original = await beginReceipt(page);
  expect(await pending(page)).toEqual(original);
  const stored = server.operations.get(original.operationId!)!.response;
  server.setAccess('expired');
  server.missNextLookup();
  await page.reload();
  await expect(page.getByText(expired, { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Recover receipt', exact: true }).click();
  const dialog = receiptDialog(page);
  await dialog.getByRole('button', { name: 'Check receipt' }).click();
  await expect(dialog.getByRole('alert').filter({ hasText: 'No committed receipt' })).toBeVisible();
  expect(await pending(page)).toEqual(original);
  await dialog.getByRole('button', { name: 'Check receipt' }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByText('POR-FIXTURE-1', { exact: true })).toBeVisible();
  expect(await pending(page)).toBeNull();
  expect(server.posts).toHaveLength(1);
  expect(server.lookups).toEqual([original.operationId, original.operationId]);
  await info.attach('observed-recovery', {
    contentType: 'application/json',
    body: JSON.stringify({
      fixture: 'Chromium with local HTTP fixture and real IndexedDB',
      path: detailPath,
      viewport: page.viewportSize(),
      message: expired,
      freshRequestsAfterRestriction: server.purchaseWrites.length - 1,
      receiptId: stored.receipts![0].receiptId,
      operationKey: original.operationId,
      received: stored.lines[0].receivedQuantity,
      remaining: stored.lines[0].remainingQuantity,
    }),
  });
});

test('open receiving dialog rechecks access before creating a fresh operation', async ({
  page,
}) => {
  const dialog = await fillReceipt(page);
  server.setAccess('expired');
  await dialog.getByRole('button', { name: 'Receive', exact: true }).click();
  await expect(dialog.getByRole('alert').filter({ hasText: expired })).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Receive', exact: true })).toBeDisabled();
  await expect(dialog.locator('[formcontrolname="batchNumber"]')).toHaveValue('LICENCE-FIXTURE');
  expect(server.posts).toHaveLength(0);
  expect(await pending(page)).toBeNull();
});

test('security suspension withholds pending receipt until recovery access returns', async ({
  page,
}) => {
  const original = await beginReceipt(page);
  server.setAccess('security');
  await receiptDialog(page).getByRole('button', { name: 'Check receipt' }).click();
  await expect(
    page.getByText('Security suspension is enforced for this shop.', { exact: true }),
  ).toBeVisible();
  await expect(page.getByText('PO-RECEIVING-1', { exact: true })).toBeHidden();
  await expect(receiptDialog(page)).toBeHidden();
  expect(await pending(page)).toEqual(original);
  server.setAccess('expired');
  await page.reload();
  await expect(page.getByText(expired, { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Recover receipt', exact: true }).click();
  await receiptDialog(page).getByRole('button', { name: 'Check receipt' }).click();
  await expect(receiptDialog(page)).toBeHidden();
  expect(await pending(page)).toBeNull();
  expect(server.posts).toHaveLength(1);
  expect(server.lookups).toEqual([original.operationId, original.operationId]);
});

test('retries a failed IndexedDB read on the expired detail page', async ({ page }, info) => {
  const original = await beginReceipt(page);
  server.setAccess('expired');
  await page.addInitScript(() => {
    const open = IDBFactory.prototype.open;
    let failOnce = true;
    IDBFactory.prototype.open = function (name, version) {
      if (failOnce && name === 'intelibill-purchase-receiving') {
        failOnce = false;
        throw new DOMException('One-shot receiving fixture failure', 'UnknownError');
      }
      return open.call(this, name, version);
    };
  });
  await page.reload();
  const dialog = await openRestrictedRecovery(page);
  await expect(dialog.getByRole('alert').filter({ hasText: 'Unable to load saved receiving work' }))
    .toBeVisible();
  await verifyOnPageRecovery(page, original.operationId!, info);
});

test('discovers a receipt saved later in another tab on the same expired detail page', async ({
  page,
}, info) => {
  server.setAccess('expired');
  await page.goto(server.origin + detailPath);
  const dialog = await openRestrictedRecovery(page);
  await expect(dialog.getByRole('status').filter({ hasText: 'No pending receipt is saved' }))
    .toBeVisible();

  // The second tab commits while trading is allowed. The first tab's scoped hint still refuses
  // fresh work until refreshed, and its already-open recovery dialog stays available.
  server.setAccess('active');
  const otherTab = await page.context().newPage();
  const original = await beginReceipt(otherTab);
  await otherTab.close();
  server.setAccess('expired');
  expect(await pending(page)).toEqual(original);
  await verifyOnPageRecovery(page, original.operationId!, info);
});

async function openRestrictedRecovery(page: Page) {
  await expect(page.getByText(expired, { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Recover receipt', exact: true }).click({ timeout: 5_000 });
  const dialog = receiptDialog(page);
  await expect(dialog.getByRole('button', { name: 'Receive', exact: true })).toHaveCount(0);
  return dialog;
}

async function verifyOnPageRecovery(
  page: Page,
  key: string,
  info: TestInfo,
) {
  const navigations: string[] = [];
  page.on('framenavigated', (frame) => {
    if (frame === page.mainFrame()) navigations.push(frame.url());
  });
  const original = server.operations.get(key)!.response.receipts![0];
  const dialog = receiptDialog(page);
  await dialog.getByRole('button', { name: 'Look for saved receipt' }).click();
  await dialog.getByRole('button', { name: 'Check receipt' }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByText(original.receiptNumber, { exact: true })).toBeVisible();
  expect(server.lookups).toEqual([key]);
  expect(server.posts).toHaveLength(1);
  expect(await pending(page)).toBeNull();
  expect(navigations).toEqual([]);
  await info.attach('observed-on-page-recovery', {
    contentType: 'application/json',
    body: JSON.stringify({
      fixture: 'Chromium with local HTTP fixture and real IndexedDB',
      path: detailPath,
      viewport: page.viewportSize(),
      message: expired,
      freshRequestsAfterRestriction: server.purchaseWrites.length - 1,
      receiptId: original.receiptId,
      operationKey: key,
      navigations,
    }),
  });
}
