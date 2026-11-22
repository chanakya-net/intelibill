import { expect, test, type Page, type Route } from '@playwright/test';
import { resolve } from 'node:path';
import { session, startPurchaseReceivingServer } from './purchase-receiving-server';
import type {
  PurchaseReceiptDetail,
  SupplierReturnDetail,
  SupplierReturnDraftRecord,
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

async function chooseReversal(page: Page, name = 'Reverse receipt lot') {
  const button = page.getByRole('button', { name, exact: true });
  await button.click();
  await expect(button).toHaveAttribute('aria-pressed', 'true');
  await expect(button).toBeEnabled();
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
  let lookups = 0;
  await page.route('**/api/financial-operations/**', (r) => {
    lookups++;
    return json(r, { code: 'Operation.NotFound' }, 404);
  });
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
  expect(lookups).toBe(1);
  console.log('Scoped 404 after reload: one outcome GET and two byte-identical POST bodies.');
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
    return json(r, {
      lots: [reversalOutcome(RECEIPT).lots[0], reversalOutcome('receipt-two').lots[0]],
    });
  });
  await page.goto(
    `${server.origin}/purchase-receipts/${RECEIPT}?group=group-one&lots=${RECEIPT},receipt-two`,
  );
  await chooseReversal(page, 'Reverse entire receipt group');
  await page.getByRole('textbox', { name: 'Reason', exact: true }).fill('Wrong entire delivery');
  await page.getByRole('textbox', { name: 'Reason', exact: true }).press('Tab');
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
  let lookups = 0;
  await context.route('**/api/financial-operations/**', (r) => {
    lookups++;
    return json(r, { code: 'Operation.NotFound' }, 404);
  });
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
  expect(payloads[1]).toBe(payloads[0]);
  expect(lookups).toBe(1);
  console.log('Cross-tab confirmation: the original tab issued zero further GETs or POSTs.');
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

test('restricted detail actions follow source eligibility and shop role', async ({
  page,
  context,
}, info) => {
  await setup(page);
  await page.route('**/api/licensing/shops/*/trading-authority', (route) =>
    json(route, { code: 'Licence.TradingRestricted', enforced: true }, 403),
  );
  await page.route('**/api/purchase-receipts/**', (route) => {
    const id = route.request().url().split('/').pop()!;
    return json(
      route,
      id === RECEIPT
        ? { ...receipt(id), reversalEligibilityReasons: [] }
        : { ...receipt(id), batchNumber: 'Lot B', returnableQuantity: 0 },
    );
  });
  await page.goto(
    `${server.origin}/purchase-receipts/${RECEIPT}?group=group-one&lots=${RECEIPT},receipt-two`,
  );
  await expect(page.getByRole('spinbutton', { name: /Quantity.*≤ 0/ })).toBeDisabled();
  await expect(page.getByRole('spinbutton', { name: /Quantity.*≤ 6/ })).toBeEnabled();
  await chooseReversal(page, 'Reverse entire receipt group');
  await expect(page.getByText(/Lot B: Full reversal unavailable/)).toBeVisible();
  await page.getByRole('textbox', { name: 'Reason', exact: true }).fill('Incorrect delivery');
  await expect(page.getByRole('button', { name: 'Confirm full receipt reversal' })).toBeDisabled();

  await page.screenshot({
    path: info.outputPath('supplier-return-group-refused.png'),
    fullPage: true,
  });
  const manager = await context.newPage();
  await setup(manager, 'Manager');
  await manager.route('**/api/licensing/shops/*/trading-authority', (route) =>
    json(route, { code: 'Licence.TradingRestricted', enforced: true }, 403),
  );
  await manager.route('**/api/supplier-returns/**', (route) => json(route, returned()));
  await manager.goto(`${server.origin}/supplier-returns/${RETURN}`);
  await expect(manager.getByRole('heading', { name: 'Supplier return reference' })).toBeVisible();
  await expect(manager.getByRole('button', { name: 'Accept supplier credit' })).toHaveCount(0);
  await manager.screenshot({ path: info.outputPath('supplier-return-manager.png') });
});

async function mockFrozenShipmentOutcome(page: Page) {
  await setup(page);
  const requests: string[] = [];
  let suspended = false;
  let released = false;
  let matched = false;
  let key = '';
  await page.route('**/api/licensing/shops/*/trading-authority', (route) =>
    json(
      route,
      {
        code: suspended && !released ? 'Licence.SecuritySuspended' : 'Licence.Expired',
        enforced: true,
      },
      403,
    ),
  );
  await page.route('**/api/purchase-receipts/**', (route) =>
    json(
      route,
      suspended && !released
        ? { code: 'Licence.SecuritySuspended' }
        : { ...receipt(), returnableQuantity: released ? 0 : 6 },
      suspended && !released ? 403 : 200,
    ),
  );
  await page.route('**/api/supplier-returns', (route) => {
    requests.push(route.request().method());
    key = route.request().postDataJSON().operationKey;
    return route.abort('connectionfailed');
  });
  await page.route('**/api/supplier-returns/' + RETURN, (route) => json(route, returned()));
  await page.route('**/api/financial-operations/**', (route) => {
    requests.push(route.request().method());
    expect(route.request().url()).toContain('/supplier-return/' + key);
    if (!released) {
      suspended = true;
      return json(route, { code: 'Licence.SecuritySuspended' }, 403);
    }
    return json(route, {
      operationType: 'supplier-return',
      operationKey: key,
      targetId: matched ? 'supplier-one' : 'different-supplier',
      response: returned(),
    });
  });
  return {
    requests,
    release: () => {
      released = true;
    },
    match: () => {
      matched = true;
    },
  };
}

test('frozen completion survives denied lookup and recovers the matching outcome', async ({
  page,
}, info) => {
  const outcome = await mockFrozenShipmentOutcome(page);
  const { requests } = outcome;
  await page.goto(`${server.origin}/purchase-receipts/${RECEIPT}`);
  await page.getByRole('spinbutton', { name: /Quantity/ }).fill('3');
  await page.getByRole('textbox', { name: 'Reason', exact: true }).fill('Damaged packaging');
  await page.getByRole('button', { name: 'Record supplier shipment', exact: true }).last().click();
  const recover = page.getByRole('button', { name: 'Check / retry saved operation' });
  await expect(recover).toBeEnabled();
  await recover.click();
  await expect(
    page.getByText('Security suspension is enforced for this shop.', { exact: true }).last(),
  ).toBeVisible();
  await expect(page.getByText('Frozen rice', { exact: true })).toHaveCount(0);
  expect(requests).toEqual(['POST', 'GET']);
  await page.reload();
  // P04-15 keeps the standalone shop-data route hidden while security suspension remains in force.
  await expect(page.getByTestId('shop-data-restricted')).toHaveText('Security suspension is enforced for this shop.');
  await expect(page.getByText('Frozen rice', { exact: true })).toHaveCount(0);
  await expect(recover).toHaveCount(0);
  await page.screenshot({ path: info.outputPath('supplier-return-suspended.png') });

  outcome.release();
  const authorityRefresh = page.waitForResponse((res) => res.url().endsWith('/trading-authority'));
  await page.evaluate(() => window.dispatchEvent(new Event('focus')));
  await authorityRefresh;
  await expect(recover).toBeEnabled();
  await recover.click();
  await expect.poll(() => requests).toEqual(['POST', 'GET', 'GET']);
  await expect(recover).toBeEnabled();
  outcome.match();
  await recover.click();
  await expect(page).toHaveURL(new RegExp('/supplier-returns/' + RETURN));
  await expect(page.getByRole('heading', { name: 'Supplier return reference' })).toBeVisible();
  expect(requests).toEqual(['POST', 'GET', 'GET', 'GET']);
  console.log(
    'Outcome requests: POST, GET denied, GET mismatched, GET matched. No additional POST.',
  );
  await page.screenshot({ path: info.outputPath('supplier-return-recovered.png') });
});

const REVIEW_RECEIPT_A = 'a1000000-0000-4000-8000-000000000001';
const REVIEW_RECEIPT_B = 'b1000000-0000-4000-8000-000000000002';
const reversalOutcome = (id: string) => ({
  lots: [
    {
      reversalId: 'reversal-one',
      receiptValueId: id,
      quantity: 10,
      effectiveAmount: 1000,
      stockTransactionId: 'reversal-movement',
      supplierLedgerEntryId: 'reversal-ledger',
      reversedAt: '2026-11-07T13:00:00Z',
      reason: 'Incorrect delivery',
    },
  ],
});

async function openUncertainReversal(page: Page, id: string) {
  page.setDefaultTimeout(5000);
  await setup(page);
  const posts: string[] = [];
  let key = '';
  await routeReviewReceipt(page, (route, path) => {
    posts.push(path);
    key = route.request().postDataJSON().operationKey;
    return route.abort('connectionfailed');
  });
  await page.goto(`${server.origin}/purchase-receipts/${id}`);
  await chooseReversal(page);
  const reason = page.getByRole('textbox', { name: 'Reason', exact: true });
  await reason.fill('Incorrect delivery');
  await reason.press('Tab');
  await page.getByRole('button', { name: 'Confirm full receipt reversal' }).click();
  const recover = page.getByRole('button', { name: 'Check / retry saved operation' });
  await expect(recover).toBeEnabled();
  await expect.poll(() => posts.length).toBe(1);
  return { recover, posts, key: () => key };
}

function routeReviewReceipt(page: Page, post: (route: Route, path: string) => Promise<void>) {
  return page.route('**/api/purchase-receipts/**', (route) => {
    const path = new URL(route.request().url()).pathname;
    if (route.request().method() === 'POST') return post(route, path);
    const target = path.split('/').pop()!.toLowerCase();
    return json(route, {
      ...receipt(target),
      batchNumber: target === REVIEW_RECEIPT_A ? 'Review lot A' : 'Review lot B',
      reversalEligibilityReasons: [],
    });
  });
}

async function storedSupplierReturnIntent(page: Page, storageTarget: string) {
  return (await storedSupplierReturnDraft(page, storageTarget))?.pending ?? null;
}

async function storedSupplierReturnDraft(page: Page, storageTarget: string) {
  return page.evaluate(async (target) => {
    const db = await new Promise<IDBDatabase>((resolve, reject) => {
      const open = indexedDB.open('intelibill-supplier-returns', 2);
      open.onsuccess = () => resolve(open.result);
      open.onerror = () => reject(open.error);
    });
    try {
      return await new Promise<SupplierReturnDraftRecord | null>((resolve, reject) => {
        const request = db.transaction('drafts').objectStore('drafts').getAll();
        request.onsuccess = () =>
          resolve(request.result.find((value) => value.targetId === target) ?? null);
        request.onerror = () => reject(request.error);
      });
    } finally {
      db.close();
    }
  }, storageTarget);
}

const canonicalReversal = 'reversal:' + REVIEW_RECEIPT_A;
const uppercaseReversal = 'reversal:' + REVIEW_RECEIPT_A.toUpperCase();

async function seedLegacyReversal(page: Page, original: SupplierReturnDraftRecord) {
  await page.evaluate(
    async ({ original, canonicalReversal, uppercaseReversal }) => {
      const db = await new Promise<IDBDatabase>((resolve, reject) => {
        const request = indexedDB.open('intelibill-supplier-returns', 2);
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
      });
      try {
        await new Promise<void>((resolve, reject) => {
          const tx = db.transaction('drafts', 'readwrite');
          tx.oncomplete = () => resolve();
          tx.onabort = () => reject(tx.error);
          const drafts = tx.objectStore('drafts');
          drafts.put({ ...original, targetId: uppercaseReversal });
          drafts.put({
            ...original,
            targetId: canonicalReversal,
            pending: null,
            draft: {
              ...original.draft,
              payload: { ...original.draft.payload, reason: 'Editable shadow' },
            },
          });
        });
      } finally {
        db.close();
      }
    },
    { original, canonicalReversal, uppercaseReversal },
  );
}

async function expectFrozenReversal(page: Page, reason: string) {
  await expect(page.getByRole('button', { name: 'Check / retry saved operation' })).toBeEnabled();
  const editor = page.getByRole('textbox', { name: 'Reason', exact: true });
  await expect(editor).toHaveValue(reason);
  await expect(editor).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Confirm full receipt reversal' })).toBeDisabled();
}

test('a lowercase receipt link recovers an operation dropped from the uppercase link', async ({
  page,
}, info) => {
  const { posts, key } = await openUncertainReversal(page, REVIEW_RECEIPT_A.toUpperCase());
  const before =
    (await storedSupplierReturnIntent(page, canonicalReversal)) ??
    (await storedSupplierReturnIntent(page, uppercaseReversal));
  await page.goto(`${server.origin}/purchase-receipts/${REVIEW_RECEIPT_A}`);
  await expectFrozenReversal(page, 'Incorrect delivery');
  expect(before?.operationKey).toBe(key());
  expect(await storedSupplierReturnIntent(page, canonicalReversal)).toEqual(before);
  expect(posts).toHaveLength(1);
  await page.screenshot({ path: info.outputPath('modify-2-lowercase-recovery.png') });
});

test('legacy noncanonical pending reversal beats an editable draft in both tabs', async ({
  page,
  context,
}) => {
  const { posts, key } = await openUncertainReversal(page, REVIEW_RECEIPT_A);
  const original = (await storedSupplierReturnDraft(page, canonicalReversal))!;
  await seedLegacyReversal(page, original);
  const second = await context.newPage();
  await setup(second);
  await routeReviewReceipt(second, (route, path) => {
    posts.push(path);
    return route.abort('connectionfailed');
  });
  await Promise.all([
    page.goto(`${server.origin}/purchase-receipts/${REVIEW_RECEIPT_A}`),
    second.goto(`${server.origin}/purchase-receipts/${REVIEW_RECEIPT_A.toUpperCase()}`),
  ]);
  await Promise.all([
    expectFrozenReversal(page, 'Incorrect delivery'),
    expectFrozenReversal(second, 'Incorrect delivery'),
  ]);
  expect(original.pending?.operationKey).toBe(key());
  const retryBodies: unknown[] = [];
  await second.route('**/api/financial-operations/**', (route) => {
    expect(route.request().url()).toContain(key());
    return json(route, { code: 'Operation.NotFound' }, 404);
  });
  await second.route('**/api/purchase-receipts/*/reversal', (route) => {
    retryBodies.push(route.request().postDataJSON());
    return route.abort('connectionfailed');
  });
  await second.getByRole('button', { name: 'Check / retry saved operation' }).click();
  await expect.poll(() => retryBodies.length).toBe(1);
  await expectFrozenReversal(second, 'Incorrect delivery');
  expect(retryBodies).toEqual([original.pending?.payload]);
  expect(posts).toHaveLength(1);
  expect(await storedSupplierReturnIntent(page, uppercaseReversal)).toEqual(original.pending);

  await second.route('**/api/financial-operations/**', (route) =>
    json(route, {
      operationType: 'purchase-receipt-reversal',
      operationKey: key(),
      targetId: REVIEW_RECEIPT_A,
      response: reversalOutcome(REVIEW_RECEIPT_A),
    }),
  );
  await second.getByRole('button', { name: 'Check / retry saved operation' }).click();
  await expect(second.getByText('Saved successfully')).toBeVisible();
  let duplicateLookups = 0;
  await page.route('**/api/financial-operations/**', (route) => {
    duplicateLookups++;
    return route.abort();
  });
  const recover = page.getByRole('button', { name: 'Check / retry saved operation' });
  await recover.click();
  await expect(recover).toHaveCount(0);
  expect(duplicateLookups).toBe(0);
  expect(posts).toHaveLength(1);
  expect(await storedSupplierReturnIntent(page, uppercaseReversal)).toBeNull();
});

test('concurrent uppercase and lowercase submissions claim one reversal in shared IndexedDB', async ({
  page,
  context,
}) => {
  const second = await context.newPage();
  const attempts: Array<{ operationKey: string; reason: string }> = [];
  const tabs = [page, second];
  await Promise.all(
    tabs.map(async (tab, index) => {
      await setup(tab);
      await routeReviewReceipt(tab, (route) => {
        attempts.push(route.request().postDataJSON());
        return route.abort('connectionfailed');
      });
      const id = index === 0 ? REVIEW_RECEIPT_A : REVIEW_RECEIPT_A.toUpperCase();
      await tab.goto(`${server.origin}/purchase-receipts/${id}`);
      await chooseReversal(tab);
      await tab.getByRole('textbox', { name: 'Reason', exact: true }).fill('Delivery tab ' + index);
      await expect(
        tab.getByRole('button', { name: 'Confirm full receipt reversal' }),
      ).toBeEnabled();
    }),
  );
  await Promise.all(
    tabs.map((tab) =>
      tab.locator('form').evaluate((form: HTMLFormElement) => form.requestSubmit()),
    ),
  );
  const recover = 'Check / retry saved operation';
  await Promise.all(
    tabs.map((tab) => expect(tab.getByRole('button', { name: recover })).toBeEnabled()),
  );
  expect(attempts).toHaveLength(1);
  await Promise.all(tabs.map((tab) => expectFrozenReversal(tab, attempts[0].reason)));
  expect((await storedSupplierReturnIntent(page, canonicalReversal))?.payload).toEqual(attempts[0]);
  expect(await storedSupplierReturnDraft(page, uppercaseReversal)).toBeNull();
});

test('same-route navigation cancels a delayed recovery retry and preserves its original intent', async ({
  page,
}, info) => {
  const { recover, posts, key } = await openUncertainReversal(page, REVIEW_RECEIPT_A);
  const original = await storedSupplierReturnIntent(page, 'reversal:' + REVIEW_RECEIPT_A);
  const lookupStarted = Promise.withResolvers<void>();
  const releaseLookup = Promise.withResolvers<void>();
  await page.route('**/api/financial-operations/**', async (route) => {
    lookupStarted.resolve();
    await releaseLookup.promise;
    await json(route, { code: 'Operation.NotFound' }, 404);
  });
  await recover.click();
  await lookupStarted.promise;
  try {
    await page.evaluate((target) => {
      history.pushState(history.state, '', '/purchase-receipts/' + target);
      dispatchEvent(new PopStateEvent('popstate', { state: history.state }));
    }, REVIEW_RECEIPT_B);
    const delivered = page.waitForResponse('**/api/financial-operations/**');
    releaseLookup.resolve();
    expect((await delivered).status()).toBe(404);
    await expect(page.locator('tbody tr').first()).toContainText('Review lot B');
    await expect(recover).toHaveCount(0);
    expect(posts).toEqual(['/api/purchase-receipts/' + REVIEW_RECEIPT_A + '/reversal']);
    const preserved = await storedSupplierReturnIntent(page, 'reversal:' + REVIEW_RECEIPT_A);
    expect(preserved?.operationKey).toBe(key());
    expect(preserved?.payload).toEqual(original?.payload);
    expect(await storedSupplierReturnIntent(page, 'reversal:' + REVIEW_RECEIPT_B)).toBeNull();
    await page.screenshot({ path: info.outputPath('modify-1-receipt-b-after-lookup.png') });
  } finally {
    releaseLookup.resolve();
  }
});

for (const matches of [true, false]) {
  test(`uppercase receipt link ${matches ? 'confirms the same GUID' : 'keeps a different GUID unresolved'}`, async ({
    page,
  }) => {
    const uppercase = REVIEW_RECEIPT_A.toUpperCase();
    const { recover, posts, key } = await openUncertainReversal(page, uppercase);
    let reads = 0;
    await page.route('**/api/financial-operations/**', (route) => {
      reads++;
      const targetId = matches ? REVIEW_RECEIPT_A : REVIEW_RECEIPT_B;
      return json(route, {
        operationType: 'purchase-receipt-reversal',
        operationKey: key(),
        targetId,
        response: reversalOutcome(targetId),
      });
    });
    const lookedUp = page.waitForResponse('**/api/financial-operations/**');
    await recover.click();
    expect((await lookedUp).status()).toBe(200);
    if (matches) await expect(page.getByText('Saved successfully')).toBeVisible();
    else {
      await expect(recover).toBeEnabled();
      await expect(
        page.getByRole('alert').filter({ hasText: /outcome is uncertain/i }),
      ).toBeVisible();
    }
    expect(reads).toBe(1);
    expect(posts).toHaveLength(1);
    const stored = await storedSupplierReturnIntent(page, canonicalReversal);
    if (matches) expect(stored).toBeNull();
    else expect(stored?.operationKey).toBe(key());
  });
}

test('matching recovery envelope requires a usable result for the frozen source', async ({
  page,
}) => {
  const { recover, posts, key } = await openUncertainReversal(page, REVIEW_RECEIPT_A);
  const responses = [{}, reversalOutcome(REVIEW_RECEIPT_B), reversalOutcome(REVIEW_RECEIPT_A)];
  let reads = 0;
  await page.route('**/api/financial-operations/**', (route) =>
    json(route, {
      operationType: 'purchase-receipt-reversal',
      operationKey: key(),
      targetId: REVIEW_RECEIPT_A,
      response: responses[reads++],
    }),
  );
  for (let index = 0; index < 2; index++) {
    const lookedUp = page.waitForResponse('**/api/financial-operations/**');
    await recover.click();
    expect((await lookedUp).status()).toBe(200);
    await expect(recover).toBeEnabled();
    await expect(
      page.getByRole('alert').filter({ hasText: /outcome is uncertain/i }),
    ).toBeVisible();
    expect(
      (await storedSupplierReturnIntent(page, 'reversal:' + REVIEW_RECEIPT_A))?.operationKey,
    ).toBe(key());
    expect(posts).toHaveLength(1);
  }
  await recover.click();
  await expect(page.getByText('Saved successfully')).toBeVisible();
  expect(await storedSupplierReturnIntent(page, 'reversal:' + REVIEW_RECEIPT_A)).toBeNull();
  expect(reads).toBe(3);
  expect(posts).toHaveLength(1);
});

test('release restores the selected shipment lot and initializes pending credit amounts', async ({
  page,
}, info) => {
  page.setDefaultTimeout(5000);
  await setup(page);
  let suspended = false;
  const lot = (id: string) => ({
    ...receipt(id),
    batchNumber: id === RECEIPT ? 'Lot A' : 'Lot B',
  });
  await page.route('**/api/licensing/shops/*/trading-authority', (route) =>
    json(
      route,
      { code: suspended ? 'Licence.SecuritySuspended' : 'Licence.Expired', enforced: true },
      403,
    ),
  );
  await page.route('**/api/purchase-receipts/**', (route) => {
    const id = route.request().url().split('/').pop()!;
    return json(route, lot(id));
  });
  await page.route('**/api/supplier-returns/preview', (route) =>
    json(route, {
      lines: route
        .request()
        .postDataJSON()
        .lines.map((line: { receiptValueId: string; quantity: number }) => ({
          ...lot(line.receiptValueId),
          suggestedCreditAmount: line.quantity * 100,
        })),
    }),
  );
  await page.route('**/api/supplier-returns/' + RETURN, (route) => json(route, returned()));
  await page.goto(
    `${server.origin}/purchase-receipts/${RECEIPT}?group=group-one&lots=${RECEIPT},receipt-two`,
  );
  const first = page.getByRole('checkbox', { name: /Lot A/ });
  const second = page.getByRole('checkbox', { name: /Lot B/ });
  const secondLot = page.locator('.lot-inputs').filter({ has: second });
  const quantity = secondLot.getByRole('spinbutton', { name: /Quantity/ });
  const amount = secondLot.getByRole('spinbutton', { name: 'Proposed credit amount' });
  await expect(first).toBeEnabled();
  await first.uncheck({ timeout: 5000 });
  await quantity.fill('2');
  await quantity.press('Tab');
  await expect(amount).toHaveValue('200');
  await amount.fill('175.50');
  const reason = page.getByRole('textbox', { name: 'Reason', exact: true });
  await reason.fill('Return only the second lot');
  await reason.press('Tab');
  await expect
    .poll(async () => (await storedSupplierReturnDraft(page, 'shipment:group-one'))?.draft.payload)
    .toMatchObject({
      reason: 'Return only the second lot',
      lines: [
        expect.objectContaining({
          receiptValueId: 'receipt-two',
          quantity: 2,
          proposedCreditAmount: 175.5,
        }),
      ],
    });

  const refreshAuthority = async () => {
    const response = page.waitForResponse((res) => res.url().endsWith('/trading-authority'));
    await page.evaluate(() => window.dispatchEvent(new Event('focus')));
    await response;
  };
  const restricted = page.getByTestId('shop-data-restricted');

  suspended = true;
  await page.reload();
  // P04-15 hides standalone shop-data routes until local authority confirms release.
  await expect(restricted).toHaveText('Security suspension is enforced for this shop.');
  await expect(second).toHaveCount(0);
  suspended = false;
  await refreshAuthority();
  await expect(restricted).toHaveCount(0);
  await expect(second).toBeChecked();
  await expect(first).not.toBeChecked();
  await expect(quantity).toHaveValue('2');
  await expect(amount).toHaveValue(/175\.5/);
  await page.screenshot({ path: info.outputPath('modify-1-restored-second-lot.png') });

  suspended = true;
  await page.goto(`${server.origin}/supplier-returns/${RETURN}`);
  await expect(restricted).toHaveText('Security suspension is enforced for this shop.');
  suspended = false;
  await refreshAuthority();
  await expect(restricted).toHaveCount(0);
  await expect(page.getByRole('spinbutton', { name: 'Frozen rice', exact: true })).toHaveValue(
    '300',
  );
});

test('fresh source-ineligible refusal explains recorded chronology and retains fields', async ({
  page,
}, info) => {
  await setup(page);
  let posts = 0;
  await page.route('**/api/purchase-receipts/**', (route) => json(route, receipt()));
  await page.route('**/api/supplier-returns', (route) => {
    posts++;
    return json(route, { code: 'SupplierReturn.SourceIneligible' }, 409);
  });
  await page.goto(`${server.origin}/purchase-receipts/${RECEIPT}`);
  const quantity = page.getByRole('spinbutton', { name: /Quantity/ });
  const reason = page.getByRole('textbox', { name: 'Reason', exact: true });
  await quantity.fill('3');
  await reason.fill('Chronology correction');
  await page.getByRole('button', { name: 'Record supplier shipment', exact: true }).last().click();
  await expect(
    page.getByRole('alert').filter({ hasText: /recorded receipt or shipment dates/i }),
  ).toBeVisible();
  await expect(quantity).toHaveValue('3');
  await expect(reason).toHaveValue('Chronology correction');
  expect(posts).toBe(1);
  await expect(page.getByRole('button', { name: 'Check / retry saved operation' })).toHaveCount(0);
  await page.screenshot({ path: info.outputPath('modify-1-source-refusal.png') });
});
