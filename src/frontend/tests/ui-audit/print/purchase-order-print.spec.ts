import { expect, test, type Page } from '@playwright/test';

import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  waitForStablePage,
} from '../support/audit-page';
import {
  PURCHASE_ORDER_STATUSES,
  DENSE_PURCHASE_ORDERS,
  LONG_SHOP_ADDRESS,
  createPurchaseOrdersScenario,
  installPurchaseOrdersFixture,
  type PurchaseOrdersScenario,
} from '../fixtures/purchase-orders.fixture';

const ROUTE = '/inventory/purchase-orders';

test.describe('purchase-order-print (A4 media audit)', () => {
  test('renders normal document sections, totals, deterministic fonts, and no controls', async ({
    page,
  }) => {
    const order = PURCHASE_ORDER_STATUSES[1]!;
    await withPrintDocument(
      page,
      createPurchaseOrdersScenario({
        orders: [order],
      }),
      order.purchaseOrderId,
      async () => {
        await assertDocumentSections(page);
        await assertScreenControlsHidden(page);
        await assertExactPrintFonts(page);
        await assertA4Output(page, 1);
      },
    );
  });

  test('keeps long supplier/address on unclipped A4 pages', async ({ page }) => {
    const order = { ...PURCHASE_ORDER_STATUSES[1]!, purchaseOrderNumber: 'PO-2026-LONG-SUPPLIER' };
    await withPrintDocument(
      page,
      createPurchaseOrdersScenario({
        orders: [order],
        withLongSupplierDetails: true,
        withLongShopAddress: true,
      }),
      order.purchaseOrderId,
      async () => {
        await assertDocumentSections(page);
        await assertLongSupplierDetailsVisible(page);
        await assertLongShopAddressVisible(page);
        await assertA4Output(page, 1);
        await assertNoClippingOrOverlap(page);
      },
    );
  });

  test('keeps long notes on at least two unclipped A4 pages', async ({ page }) => {
    const order = { ...PURCHASE_ORDER_STATUSES[1]!, purchaseOrderNumber: 'PO-2026-LONG-NOTES' };
    await withPrintDocument(
      page,
      createPurchaseOrdersScenario({
        orders: [order],
        withLongNotes: true,
      }),
      order.purchaseOrderId,
      async () => {
        await assertDocumentSections(page);
        await assertA4Output(page, 2);
        await assertNoClippingOrOverlap(page);
      },
    );
  });

  test('keeps dense line items and totals apart across multiple pages', async ({ page }) => {
    const order = DENSE_PURCHASE_ORDERS[0]!;
    await withPrintDocument(
      page,
      createPurchaseOrdersScenario({
        orders: [order],
        withDenseLines: true,
      }),
      order.purchaseOrderId,
      async () => {
        await assertDocumentSections(page);
        await assertDenseLineItemsPresent(page);
        await assertDenseGrandTotalVisible(page);
        await assertTablePagination(page);
        await assertA4Output(page, 2);
        await assertNoClippingOrOverlap(page);
      },
    );
  });

  test('renders receipt/status information on purchase order with receipt history', async ({ page }) => {
    const partiallyReceivedOrder = PURCHASE_ORDER_STATUSES[2]!;
    await withPrintDocument(
      page,
      createPurchaseOrdersScenario({
        orders: [partiallyReceivedOrder],
        withReceiptHistory: true,
      }),
      partiallyReceivedOrder.purchaseOrderId,
      async () => {
        await assertDocumentSections(page);
        await assertReceiptStatusVisible(page);
        await assertA4Output(page, 1);
        await assertNoClippingOrOverlap(page);
      },
    );
  });

  test('renders large currency values without clipping totals', async ({ page }) => {
    const largeValueOrder = {
      ...PURCHASE_ORDER_STATUSES[1]!,
      purchaseOrderId: 'po-large-value',
      purchaseOrderNumber: 'PO-2026-LARGE-VALUE',
      expectedTotal: 99999.99,
    };
    await withPrintDocument(
      page,
      createPurchaseOrdersScenario({
        orders: [largeValueOrder],
      }),
      largeValueOrder.purchaseOrderId,
      async () => {
        await expect(page.locator('table tfoot th.numeric').last()).toContainText('99,999.99');
        await assertA4Output(page, 1);
        await assertNoClippingOrOverlap(page);
      },
    );
  });

  test('renders the loading print document without document sections or controls', async ({
    page,
  }) => {
    const order = PURCHASE_ORDER_STATUSES[1]!;
    await withPrintState(
      page,
      createPurchaseOrdersScenario({ orders: [order], apiState: 'loading' }),
      order.purchaseOrderId,
      async () => {
        await expect(page.locator('.print-state')).toBeVisible();
        await expect(page.locator('article.po-document')).toHaveCount(0);
        await assertScreenControlsHidden(page);
      },
    );
  });

  test('renders the declared error print document without document sections or controls', async ({
    page,
  }) => {
    const order = PURCHASE_ORDER_STATUSES[1]!;
    const declaredError = {
      url: `http://localhost:5277/api/purchase-orders/${order.purchaseOrderId}`,
      status: 503,
    };
    await withPrintState(
      page,
      createPurchaseOrdersScenario({ orders: [order], apiState: 'error' }),
      order.purchaseOrderId,
      async () => {
        await expect(page.locator('.print-state--error[role="alert"]')).toBeVisible();
        await expect(page.locator('article.po-document')).toHaveCount(0);
        await assertScreenControlsHidden(page);
      },
      declaredError,
    );
  });
});

async function withPrintDocument(
  page: Page,
  scenario: PurchaseOrdersScenario,
  purchaseOrderId: string,
  assertion: () => Promise<void>,
  declaredError?: DeclaredError,
): Promise<void> {
  await withPrintState(page, scenario, purchaseOrderId, assertion, declaredError);
}

async function withPrintState(
  page: Page,
  scenario: PurchaseOrdersScenario,
  purchaseOrderId: string,
  assertion: () => Promise<void>,
  declaredError?: DeclaredError,
): Promise<void> {
  const collector = collectBrowserFailures(page);
  try {
    await installPurchaseOrdersFixture(page, scenario);
    await page.goto(`${ROUTE}/${purchaseOrderId}/print`);
    await page.emulateMedia({ media: 'print', colorScheme: 'light' });
    await waitForStablePage(page);
    await assertion();
    assertOnlyDeclaredFailures(collector.failures, declaredError);
  } finally {
    collector.dispose();
  }
}

async function assertDocumentSections(page: Page): Promise<void> {
  await expect(page.locator('article.po-document')).toBeVisible();
  await expect(page.locator('.po-document__header')).toBeVisible();
  await expect(page.locator('.po-document__grid')).toBeVisible();
  await expect(page.locator('table')).toBeVisible();
  await expect(page.locator('tfoot')).toBeVisible();
}

async function assertScreenControlsHidden(page: Page): Promise<void> {
  await expect(page.locator('.screen-controls')).toBeHidden();
}

async function assertLongSupplierDetailsVisible(page: Page): Promise<void> {
  const supplierName = page.locator('.po-document__supplier--name');
  await expect(supplierName).toContainText('Very Long International Supplier Name With Deterministic Audit Value');
  const supplierRef = page.locator('.po-document__supplier--reference');
  await expect(supplierRef).toContainText('SUPPLIER-REFERENCE-WITH-A-LONG-DETERMINISTIC-VALUE-2026-000001');
}

async function assertLongShopAddressVisible(page: Page): Promise<void> {
  await expect(page.locator('.po-document__shop')).toContainText(LONG_SHOP_ADDRESS);
}

async function assertDenseLineItemsPresent(page: Page): Promise<void> {
  const rows = page.locator('table tbody tr');
  const count = await rows.count();
  expect(count).toBeGreaterThanOrEqual(30);
}

async function assertDenseGrandTotalVisible(page: Page): Promise<void> {
  await expect(page.locator('table tfoot th.numeric').last()).toContainText('39,025.00');
}

async function assertTablePagination(page: Page): Promise<void> {
  const table = page.locator('table');
  const tableComputedStyle = await table.evaluate((el) => getComputedStyle(el).breakInside);
  expect(tableComputedStyle).toBe('avoid');
}

async function assertReceiptStatusVisible(page: Page): Promise<void> {
  const statusField = page.locator('.po-document__status');
  await expect(statusField).toHaveText('Partially Received');
  const receipts = page.locator('.po-document__receipts');
  await expect(receipts).toBeVisible();
  await expect(receipts.locator('h2')).toHaveText('Receipts');
  const receiptItems = page.locator('.receipt-status');
  await expect(receiptItems).toHaveCount(2);
  await expect(receiptItems.nth(0)).toContainText('REC-2026-001');
  await expect(receiptItems.nth(0)).toContainText('7/15/26');
  await expect(receiptItems.nth(0)).toContainText('John Doe');
}

async function assertExactPrintFonts(page: Page): Promise<void> {
  const fonts = await page.locator('.po-document').evaluate((article) => ({
    document: getComputedStyle(article).fontFamily,
    heading: getComputedStyle(article.querySelector('h1')!).fontFamily,
  }));
  expect(fonts.document).toBe('Arial, sans-serif');
  expect(fonts.heading).toBe('Georgia, "Times New Roman", serif');
}

async function assertA4Output(page: Page, minimumPages: number): Promise<void> {
  const printStyles = await page.locator('article.po-document').evaluate((article) => {
    const targets = [
      article.querySelector('.po-document__header'),
      article.querySelector('.po-document__grid'),
      article.querySelector('table'),
    ];
    const pageRule = Array.from(document.styleSheets)
      .flatMap((sheet) => {
        try {
          return Array.from(sheet.cssRules);
        } catch {
          return [];
        }
      })
      .find((rule) => rule.cssText.includes('@page'));
    return {
      breaks: targets.map((target) => getComputedStyle(target!).breakInside),
      pageRule: pageRule?.cssText ?? '',
    };
  });
  expect(printStyles.breaks).toEqual(['avoid', 'avoid', 'avoid']);
  expect(printStyles.pageRule.toLowerCase()).toContain('size: a4');

  const pdf = await page.pdf({
    format: 'A4',
    margin: { top: '12mm', right: '12mm', bottom: '12mm', left: '12mm' },
    preferCSSPageSize: true,
    printBackground: true,
  });
  const pageCount = (pdf.toString('latin1').match(/\/Type\s*\/Page\b/g) ?? []).length;
  expect(pageCount).toBeGreaterThanOrEqual(minimumPages);
}

async function assertNoClippingOrOverlap(page: Page): Promise<void> {
  const result = await page.locator('article.po-document').evaluate((article) => {
    const elements = Array.from(
      article.querySelectorAll<HTMLElement>(
        '.po-document__header, .po-document__grid, table',
      ),
    );
    const boxes = elements.map((element) => ({
      name: element.className,
      rect: element.getBoundingClientRect(),
      clipped:
        element.scrollHeight > element.clientHeight || element.scrollWidth > element.clientWidth,
    }));
    const overlap = boxes.some((box, index) =>
      boxes
        .slice(index + 1)
        .some(
          (other) =>
            box.rect.bottom > other.rect.top &&
            box.rect.top < other.rect.bottom &&
            box.rect.right > other.rect.left &&
            box.rect.left < other.rect.right,
        ),
    );
    return { clipped: boxes.filter((box) => box.clipped).map((box) => box.name), overlap };
  });
  expect(result.clipped).toEqual([]);
  expect(result.overlap).toBe(false);
}

interface DeclaredError {
  readonly url: string;
  readonly status: number;
}

function assertOnlyDeclaredFailures(
  failures: readonly { kind: string; message: string; url?: string }[],
  declaredError?: DeclaredError,
): void {
  if (!declaredError) {
    assertNoUnexpectedBrowserFailures(failures);
    return;
  }
  expect(failures).toContainEqual({
    kind: 'response',
    message: `HTTP ${declaredError.status}`,
    url: declaredError.url,
  });
  const unexpected = failures.filter((failure) => !isDeclaredFailure(failure, declaredError));
  assertNoUnexpectedBrowserFailures(unexpected);
}

function isDeclaredFailure(
  failure: { kind: string; message: string; url?: string },
  declaredError: DeclaredError,
): boolean {
  if (failure.kind === 'response')
    return failure.url === declaredError.url && failure.message === `HTTP ${declaredError.status}`;
  if (failure.kind === 'console')
    return failure.message.includes(`status of ${declaredError.status}`);
  return (
    failure.kind === 'request' &&
    failure.message === 'net::ERR_ABORTED' &&
    failure.url?.includes('/api/shops/')
  );
}
