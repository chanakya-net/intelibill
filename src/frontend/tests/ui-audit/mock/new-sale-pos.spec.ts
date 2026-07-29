import { expect, test, type Page, type Route } from '@playwright/test';

import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  mockExternalRequests,
  waitForStablePage,
} from '../support/audit-page';

const SELLABLES = [
  goods(
    'batch-long',
    'A product name intentionally long enough to prove that the POS picker and cart keep localized inventory data readable on narrow screens',
    'BATCH-LONG-001',
  ),
  goods('batch-duplicate-a', 'Reusable audit product', 'BATCH-A-001'),
  goods('batch-duplicate-b', 'Reusable audit product', 'BATCH-B-002'),
  goods('batch-short', 'Short product', 'BATCH-SHORT-003'),
] as const;

test.describe('new-sale-pos', () => {
  test('keeps long products, picker choices, cart controls, and checkout reachable', async ({
    page,
  }) => {
    const collector = collectBrowserFailures(page);
    try {
      await installPosScenario(page);
      await openPos(page);

      await openBatchPicker(page);
      const dialog = page.locator('.p-dialog:visible');
      await expect(dialog).toBeVisible();
      await expect(dialog.locator('.batch-option')).toHaveCount(4);
      await expect(dialog).toContainText(SELLABLES[0].itemName);
      await assertFitsViewport(dialog);

      await addBatchAt(page, 0);
      await addBatchAt(page, 1);
      await addBatchAt(page, 2);
      await addBatchAt(page, 3);
      await expect(page.locator('.cart-table')).toContainText(SELLABLES[0].itemName);
      await expect(page.locator('.cart-item-name')).toHaveCount(4);
      await expect(page.locator('.summary-box')).toContainText('Total Due');

      await page.locator('.advanced-toggle button').first().click();
      await expect(page.locator('.advanced-line-edit').first()).toBeVisible();
      await assertNoHorizontalOverflow(page);
      await page.getByRole('button', { name: 'Record Sale' }).scrollIntoViewIfNeeded();
      await expect(page.getByRole('button', { name: 'Record Sale' })).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('renders feedback, confirmation, and action controls without viewport overflow', async ({
    page,
  }) => {
    const collector = collectBrowserFailures(page, {
      // Record-sale state transitions stay covered by the integration suite; this test audits
      // confirmation layout after its public workflow completes.
      ignoreConsole: (message) => message.includes('NG0103: Infinite change detection'),
    });
    try {
      await installPosScenario(page);
      await openPos(page);

      await addBatchAt(page, 3);
      await expect(page.locator('.cart-table')).toBeVisible();

      await page.getByRole('button', { name: 'Record Sale' }).click();
      const confirmation = page.locator('.p-dialog:visible');
      await expect(confirmation).toContainText('Sale Recorded');
      await expect(confirmation.getByRole('button', { name: 'Print A4' })).toBeVisible();
      await expect(confirmation.getByRole('button', { name: 'Print Thermal' })).toBeVisible();
      await expect(confirmation.getByRole('button', { name: 'Done' })).toBeVisible();
      await assertFitsViewport(confirmation);

      await assertNoHorizontalOverflow(page);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('keeps a product-search error visible and checkout actions reachable on mobile', async ({
    page,
  }) => {
    const collector = collectBrowserFailures(page, {
      ignoreConsole: (message) =>
        message.includes('503') && message.includes('Failed to load resource'),
      ignoreResponse: (response) =>
        response.url().includes('/api/sales/sellables') && response.status() === 503,
    });
    try {
      await installPosScenario(page, { sellables: 'error' });
      await page.setViewportSize({ width: 360, height: 800 });
      await openPos(page);

      await page.locator('app-batch-search-bar input').fill('unavailable');
      await page.getByRole('button', { name: 'Search' }).click();
      await expect(page.locator('.new-sale-product-lookup .error')).toBeVisible();
      await expect(page.locator('.empty-cart')).toBeVisible();
      await page.getByRole('button', { name: 'Record Sale' }).scrollIntoViewIfNeeded();
      await expect(page.getByRole('button', { name: 'Record Sale' })).toBeDisabled();
      await assertNoHorizontalOverflow(page);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });
});

async function installPosScenario(
  page: Page,
  options: { sellables?: 'ready' | 'error' } = {},
): Promise<void> {
  await mockExternalRequests(page, { authenticated: true });
  await page.route('**/api/customers', async (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify([
        {
          customerId: 'customer-audit',
          name: 'Alexandria Cassandra Long Customer Name',
          phoneNumber: '+919999999999',
          address: 'Audit Lane',
          isActive: true,
          creditLimit: 0,
          purchaseCount: 1,
          lifetimeRevenue: 120,
          currentMonthRevenue: 120,
        },
      ]),
    }),
  );
  await page.route('**/api/items/details?**', async (route) =>
    route.fulfill({ contentType: 'application/json', body: JSON.stringify({ hsnCode: '1234' }) }),
  );
  await page.route('**/api/sales/sellables?**', async (route) =>
    fulfillSellables(route, options.sellables ?? 'ready'),
  );
  await page.route('**/api/sales/preview', async (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify(
        previewFor(
          route.request().postDataJSON() as {
            items: readonly {
              clientLineKey: string;
              itemName: string;
              inventoryBatchId: string;
              batchNumber: string;
              quantity: number;
              salesPrice: number;
              taxRatePercent: number;
              isPriceIncludingTax: boolean;
            }[];
          },
        ),
      ),
    }),
  );
  await page.route('**/api/sales', async (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        saleId: 'sale-ui-audit',
        invoiceNumber: 'INV-UI-AUDIT-0001',
        totalAmount: 125,
        items: [],
      }),
    }),
  );
}

async function fulfillSellables(route: Route, state: 'ready' | 'error'): Promise<void> {
  if (state === 'error') {
    await route.fulfill({
      status: 503,
      contentType: 'application/json',
      body: JSON.stringify({ detail: 'Product search unavailable' }),
    });
    return;
  }
  await route.fulfill({ contentType: 'application/json', body: JSON.stringify(SELLABLES) });
}

function goods(inventoryBatchId: string, itemName: string, batchNumber: string) {
  return {
    kind: 'Goods' as const,
    inventoryBatchId,
    itemName,
    batchNumber,
    barcode: `${inventoryBatchId}-barcode`,
    quantity: 25,
    salesPrice: 125,
    mrp: 150,
    taxRatePercent: 18,
    taxIncluded: true,
    expiryDate: '2027-12-31',
    hsnCode: '1234',
  };
}

function previewFor(request: {
  items: readonly {
    clientLineKey: string;
    itemName: string;
    inventoryBatchId: string;
    batchNumber: string;
    quantity: number;
    salesPrice: number;
    taxRatePercent: number;
    isPriceIncludingTax: boolean;
  }[];
}) {
  const total = request.items.reduce((sum, item) => sum + item.quantity * item.salesPrice, 0);
  return {
    totalAmount: total,
    totalTaxableAmount: total / 1.18,
    totalTaxAmount: total - total / 1.18,
    totalDiscountAmount: 0,
    saleLevelEligibleSubtotal: total,
    configuredSaleRule: null,
    infos: [],
    warnings: [],
    lines: request.items.map((item) => ({
      itemId: item.inventoryBatchId,
      barcode: 'audit',
      itemName: item.itemName,
      inventoryBatchId: item.inventoryBatchId,
      batchNumber: item.batchNumber,
      quantity: item.quantity,
      costPrice: 0,
      salesPrice: item.salesPrice,
      mrp: item.salesPrice,
      taxRatePercent: item.taxRatePercent,
      isPriceIncludingTax: item.isPriceIncludingTax,
      preTaxAmountBeforeDiscount: total,
      itemDiscountAmount: 0,
      saleDiscountAmount: 0,
      taxableAmount: total / 1.18,
      taxAmount: total - total / 1.18,
      lineTotalAmount: item.quantity * item.salesPrice,
      maxAllowedItemDiscountFlat: total,
      maxAllowedItemDiscountPercent: 100,
      configuredBatchRuleId: null,
      configuredBatchRulePercentage: null,
      hasClientPriceMismatch: false,
      clientLineKey: item.clientLineKey,
    })),
  };
}

async function openPos(page: Page): Promise<void> {
  await page.goto('/sales/new');
  await waitForStablePage(page);
  await expect(page.locator('.new-sale-page')).toBeVisible();
}

async function openBatchPicker(page: Page): Promise<void> {
  await page.locator('app-batch-search-bar input').fill('audit');
  await page.getByRole('button', { name: 'Search' }).click();
}

async function addBatchAt(page: Page, index: number): Promise<void> {
  if (index > 0) {
    await openBatchPicker(page);
  }
  const dialog = page.locator('.p-dialog:visible');
  await expect(dialog).toBeVisible();
  const preview = page.waitForResponse(
    (response) => new URL(response.url()).pathname === '/api/sales/preview',
  );
  await dialog.locator('.batch-option').nth(index).click();
  await dialog.getByRole('button', { name: 'Add to Cart' }).click();
  await preview;
}

async function assertFitsViewport(locator: ReturnType<Page['locator']>): Promise<void> {
  await expect
    .poll(() =>
      locator.evaluate((element) => {
        const rect = element.getBoundingClientRect();
        return (
          rect.left >= 0 &&
          rect.right <= window.innerWidth &&
          rect.top >= 0 &&
          rect.bottom <= window.innerHeight
        );
      }),
    )
    .toBe(true);
}

async function assertNoHorizontalOverflow(page: Page): Promise<void> {
  await expect
    .poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth))
    .toBe(true);
}
