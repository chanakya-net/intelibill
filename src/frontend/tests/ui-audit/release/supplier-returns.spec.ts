import { expect, test, type Page, type Route } from '@playwright/test';
import { resolve } from 'node:path';
import { session, startPurchaseReceivingServer } from './purchase-receiving-server';
import type {
  PurchaseReceiptDetail,
  SupplierReturnDetail,
} from '../../../src/app/features/supplier-returns/services/supplier-return.models';
const RECEIPT = 'receipt-one';
const RETURN = 'return-one';
let server: Awaited<ReturnType<typeof startPurchaseReceivingServer>>;
const json = (route: Route, value: unknown, status = 200) =>
  route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) });
const receipt = (id = RECEIPT): PurchaseReceiptDetail => ({
  receiptValueId: id,
  inventoryBatchId: 'batch-' + id,
  itemId: 'item-one',
  supplierId: 'supplier-one',
  purchaseOrderReceiptLineId: null,
  batchNumber: 'Lot A',
  itemName: 'Frozen rice',
  uqc: 'NOS',
  quantityScale: 0,
  receiptVersion: 10,
  batchVersion: 10,
  receivedQuantity: 10,
  availableQuantity: 6,
  returnedQuantity: 0,
  reversedQuantity: 0,
  netRetainedQuantity: 10,
  returnableQuantity: 6,
  effectiveTotalCost: 1000,
  returnCreditAmount: 0,
  allocatedPaymentAmount: 1000,
  netPayableAmount: 1000,
  outstandingAmount: 0,
  supplierCreditAmount: 0,
  suggestedCreditAmount: 300,
  isVoided: false,
  reversalEligibilityReasons: ['Inventory.BatchConsumed'],
  returns: [],
  reversals: [],
});
const returned = (): SupplierReturnDetail => ({
  returnId: RETURN,
  supplierId: 'supplier-one',
  returnNumber: 'SR-FROZEN-1',
  status: 'PendingSupplierCredit',
  version: 11,
  reason: 'Damaged packaging',
  shippedAt: '2026-11-07T12:00:00Z',
  businessDate: '2026-11-07',
  businessTimeZoneId: 'Asia/Kolkata',
  shopName: 'Frozen shop',
  supplierName: 'Frozen supplier',
  credit: null,
  lines: [
    {
      returnLineId: 'line-one',
      receiptValueId: RECEIPT,
      inventoryBatchId: 'batch-one',
      itemId: 'item-one',
      purchaseOrderReceiptLineId: null,
      quantity: 3,
      proposedCreditAmount: 300,
      receivedQuantity: 10,
      effectiveTotalCost: 1000,
      itemName: 'Frozen rice',
      uqc: 'NOS',
      sourceReference: 'Supplier bill',
      stockTransactionId: 'movement-one',
      acceptedAmount: null,
      supplierLedgerEntryId: null,
    },
  ],
});
test.beforeEach(async () => {
  server = await startPurchaseReceivingServer(
    resolve(__dirname, '../../../dist/INVENTORY/browser'),
  );
});
test.afterEach(async () => {
  await server.close();
});
async function setup(page: Page, role = 'Owner') {
  const value = session();
  value.shops.forEach((x) => (x.role = role));
  await page.route('**/api/supplier-returns/preview', (route) => {
    const input = route.request().postDataJSON();
    return json(route, {
      lines: input.lines.map((line: { receiptValueId: string; quantity: number }) => ({
        ...receipt(line.receiptValueId),
        suggestedCreditAmount: line.quantity * 100,
      })),
    });
  });
  await page.addInitScript(
    (value) => localStorage.setItem('inventory.auth.session.local', JSON.stringify(value)),
    value,
  );
}
test(
  'pending shipment, acceptance, paid supplier credit and immutable print',
  { tag: '@supplier-returns' },
  async ({ page }) => {
    await setup(page);
    let doc = returned();
    let lot = receipt();
    await page.route('**/api/purchase-receipts/**', (r) => json(r, lot));
    await page.route('**/api/supplier-returns**', async (r) => {
      if (r.request().url().endsWith('/preview'))
        return json(r, {
          lines: r
            .request()
            .postDataJSON()
            .lines.map((line: { receiptValueId: string; quantity: number }) => ({
              ...receipt(line.receiptValueId),
              suggestedCreditAmount: line.quantity * 100,
            })),
        });
      if (r.request().method() === 'POST' && r.request().url().endsWith('/credit')) {
        doc = {
          ...doc,
          status: 'CreditAccepted',
          credit: {
            creditId: 'credit',
            acceptedAt: '2026-11-07T13:00:00Z',
            businessDate: '2026-11-07',
            acceptedBy: 'owner',
            reason: 'Supplier agreed',
            supplierReference: 'AGREE-1',
          },
          lines: doc.lines.map((l) => ({
            ...l,
            acceptedAmount: 300,
            supplierLedgerEntryId: 'ledger',
          })),
        };
        lot = {
          ...lot,
          availableQuantity: 3,
          returnedQuantity: 3,
          netRetainedQuantity: 7,
          returnCreditAmount: 300,
          netPayableAmount: 700,
          supplierCreditAmount: 300,
          returns: [doc],
        };
        return json(r, doc);
      }
      if (r.request().url().endsWith('/preview'))
        return json(r, {
          lines: r
            .request()
            .postDataJSON()
            .lines.map((line: { receiptValueId: string; quantity: number }) => ({
              ...receipt(line.receiptValueId),
              suggestedCreditAmount: line.quantity * 100,
            })),
        });
      if (r.request().method() === 'POST') {
        const body = r.request().postDataJSON();
        expect(body.creditAccepted).toBe(false);
        expect(body.lines[0].quantity).toBe(3);
        lot = {
          ...lot,
          returns: [doc],
          availableQuantity: 3,
          returnedQuantity: 3,
          netRetainedQuantity: 7,
        };
        return json(r, doc);
      }
      return json(
        r,
        r
          .request()
          .url()
          .endsWith('/' + RETURN)
          ? doc
          : [doc],
      );
    });
    await page.goto(`${server.origin}/purchase-receipts/${RECEIPT}`);
    await page.getByRole('spinbutton', { name: /Quantity/ }).fill('3');
    await page.getByRole('textbox', { name: 'Reason', exact: true }).fill('Damaged packaging');
    await page
      .getByRole('button', { name: 'Record supplier shipment', exact: true })
      .last()
      .click();
    await expect(page).toHaveURL(new RegExp('/supplier-returns/' + RETURN));
    await expect(page.getByRole('heading', { name: 'Supplier return reference' })).toBeVisible();
    await expect(
      page.getByText('Commercial reference — not a GST credit/debit note'),
    ).toBeVisible();
    await page.getByRole('textbox', { name: 'Reason', exact: true }).fill('Supplier agreed');
    await page.getByRole('spinbutton', { name: 'Frozen rice', exact: true }).fill('0.29');
    await expect(
      page.getByRole('button', { name: 'Accept supplier credit', exact: true }),
    ).toBeEnabled();
    await page.getByRole('spinbutton', { name: 'Frozen rice', exact: true }).fill('300');
    await page.getByRole('button', { name: 'Accept supplier credit', exact: true }).click();
    await expect(page.getByText('Credit accepted on:', { exact: false })).toBeVisible();
    await page.goto(`${server.origin}/purchase-receipts/${RECEIPT}`);
    await expect(page.locator('tbody tr').first()).toContainText('700.00');
    await expect(page.locator('tbody tr').first()).toContainText('300.00');
  },
);
test('uncertain reload and lookup404 retry the exact frozen payload', async ({ page }) => {
  await setup(page);
  let first = true;
  const payloads: string[] = [];
  await page.route('**/api/purchase-receipts/**', (r) => json(r, receipt()));
  await page.route('**/api/financial-operations/**', (r) =>
    json(r, { code: 'Operation.NotFound' }, 404),
  );
  await page.route('**/api/supplier-returns**', async (r) => {
    if (r.request().url().endsWith('/preview'))
      return json(r, {
        lines: r
          .request()
          .postDataJSON()
          .lines.map((line: { receiptValueId: string; quantity: number }) => ({
            ...receipt(line.receiptValueId),
            suggestedCreditAmount: line.quantity * 100,
          })),
      });
    if (r.request().method() === 'POST') {
      payloads.push(r.request().postData()!);
      if (first) {
        first = false;
        return r.abort('connectionfailed');
      }
      return json(r, returned());
    }
    return json(r, returned());
  });
  await page.goto(`${server.origin}/purchase-receipts/${RECEIPT}`);
  await page.getByRole('spinbutton', { name: /Quantity/ }).fill('3');
  await page.getByRole('textbox', { name: 'Reason', exact: true }).fill('Damaged packaging');
  await page.getByRole('button', { name: 'Record supplier shipment', exact: true }).last().click();
  await expect(page.getByRole('button', { name: 'Check / retry saved operation' })).toBeVisible();
  await page.reload();
  await expect(page.getByRole('spinbutton', { name: /Quantity/ })).toBeDisabled();
  await page.getByRole('button', { name: 'Check / retry saved operation' }).click();
  await expect(page).toHaveURL(new RegExp('/supplier-returns/' + RETURN));
  expect(payloads).toHaveLength(2);
  expect(payloads[1]).toBe(payloads[0]);
});
test('manager sees pending-only controls at narrow width', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await setup(page, 'Manager');
  await page.route('**/api/purchase-receipts/**', (r) => json(r, receipt()));
  await page.goto(`${server.origin}/purchase-receipts/${RECEIPT}`);
  await expect(
    page.getByText('Managers may record pending shipments.', { exact: false }),
  ).toBeVisible();
  await expect(page.getByRole('button', { name: 'Reverse receipt lot', exact: true })).toHaveCount(
    0,
  );
  await expect(page.getByRole('combobox', { name: 'Supplier credit agreement' })).toHaveCount(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  );
});
test('owner grouped reversal sends every lot version', async ({ page }) => {
  await setup(page);
  let reversed = false;
  await page.route('**/api/purchase-receipts/**', (r) => {
    const id = r.request().url().split('/').pop();
    return json(r, { ...receipt(id), availableQuantity: 10, reversalEligibilityReasons: [] });
  });
  await page.route('**/api/purchase-order-receipts/group-one/reversal', (r) => {
    expect(r.request().postDataJSON().lines).toHaveLength(2);
    reversed = true;
    return json(r, { lots: [] });
  });
  await page.goto(
    `${server.origin}/purchase-receipts/${RECEIPT}?group=group-one&lots=${RECEIPT},receipt-two`,
  );
  await page.getByRole('button', { name: 'Reverse entire receipt group', exact: true }).click();
  await page.getByRole('textbox', { name: 'Reason', exact: true }).fill('Wrong entire delivery');
  await page.getByRole('button', { name: 'Confirm full receipt reversal' }).click();
  await expect(page.getByText('Saved successfully')).toBeVisible();
  expect(reversed).toBe(true);
});

test('a second tab confirms and cleans an uncertain operation without resurrection', async ({
  page,
  context,
}) => {
  await setup(page);
  const payloads: string[] = [];
  let first = true;
  await context.route('**/api/purchase-receipts/**', (r) => json(r, receipt()));
  await context.route('**/api/financial-operations/**', (r) =>
    json(r, { code: 'Operation.NotFound' }, 404),
  );
  await context.route('**/api/supplier-returns**', (r) => {
    if (r.request().method() === 'POST') {
      payloads.push(r.request().postData()!);
      if (first) {
        first = false;
        return r.abort('connectionfailed');
      }
    }
    return json(r, returned());
  });
  await page.goto(`${server.origin}/purchase-receipts/${RECEIPT}`);
  await page.getByRole('spinbutton', { name: /Quantity/ }).fill('3');
  await page.getByRole('textbox', { name: 'Reason', exact: true }).fill('Damaged packaging');
  await page.getByRole('button', { name: 'Record supplier shipment', exact: true }).last().click();
  await expect(page.getByRole('button', { name: 'Check / retry saved operation' })).toBeVisible();
  const other = await context.newPage();
  await other.goto(`${server.origin}/purchase-receipts/${RECEIPT}`);
  await other.getByRole('button', { name: 'Check / retry saved operation' }).click();
  await expect(other).toHaveURL(new RegExp('/supplier-returns/' + RETURN));
  await page.getByRole('button', { name: 'Check / retry saved operation' }).click();
  await expect(page.getByRole('spinbutton', { name: /Quantity/ })).toBeEnabled();
  expect(payloads).toHaveLength(2);
});

test('supplier history reaches older pending approvals with the same filters', async ({ page }) => {
  await setup(page);
  const queries: URL[] = [];
  await page.route('**/api/supplier-returns**', (route) => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/older-return'))
      return json(route, {
        ...returned(),
        returnId: 'older-return',
        returnNumber: 'SR-OLDER-PENDING',
      });
    queries.push(url);
    const pageNumber = Number(url.searchParams.get('page') ?? 1);
    return json(
      route,
      pageNumber === 1
        ? Array.from({ length: 20 }, (_, i) => ({
            ...returned(),
            returnId: `r-${i}`,
            returnNumber: `SR-${i}`,
          }))
        : [{ ...returned(), returnId: 'older-return', returnNumber: 'SR-OLDER-PENDING' }],
    );
  });
  await page.goto(`${server.origin}/supplier-returns?supplierId=supplier-one`);
  await page
    .getByRole('combobox', { name: 'Status', exact: true })
    .selectOption('PendingSupplierCredit');
  await page.getByRole('button', { name: 'Load more', exact: true }).click();
  await page.getByRole('link', { name: 'SR-OLDER-PENDING', exact: true }).click();
  await expect(
    page.getByRole('heading', { name: 'Accept supplier credit', exact: true }),
  ).toBeVisible();
  const older = queries.find((url) => url.searchParams.get('page') === '2');
  expect(older?.searchParams.get('supplierId')).toBe('supplier-one');
  expect(older?.searchParams.get('status')).toBe('PendingSupplierCredit');
});

test('return references print frozen lot identity and navigate to the actual source', async ({
  page,
}) => {
  await setup(page);
  let sourceReads = 0;
  await page.route('**/api/supplier-returns/**', (route) =>
    json(route, {
      ...returned(),
      lines: returned().lines.map((line) => ({
        ...line,
        sourceReference: null,
        batchNumberSnapshot: 'Frozen lot A',
      })),
    }),
  );
  await page.route('**/api/purchase-receipts/**', (route) => {
    sourceReads++;
    return json(route, { ...receipt(), batchNumber: 'Renamed live lot' });
  });
  await page.goto(`${server.origin}/supplier-returns/${RETURN}`);
  await expect(page.getByText('Frozen lot A', { exact: true })).toBeVisible();
  expect(sourceReads).toBe(0);
  const source = page.getByRole('link', { name: 'Receipt lot and return history', exact: true });
  await expect(source).toHaveAttribute('href', `/purchase-receipts/${RECEIPT}`);
  await source.click();
  await expect(page.locator('tbody tr').first()).toContainText('Renamed live lot');
});

test('historical references without a frozen lot label use the persisted receipt identity', async ({
  page,
}) => {
  await setup(page);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.route('**/api/supplier-returns/**', (route) =>
    json(route, {
      ...returned(),
      lines: returned().lines.map((line) => ({
        ...line,
        sourceReference: null,
        batchNumberSnapshot: null,
        receiptValueId: '10000000-0000-4000-8000-000000000001',
      })),
    }),
  );
  await page.goto(`${server.origin}/supplier-returns/${RETURN}`);
  await expect(page.locator('app-supplier-return-print-view tbody')).toContainText(
    '10000000-0000-4000-8000-000000000001',
  );
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  );
});
