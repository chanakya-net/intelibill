import { expect, test, type Page, type Route } from '@playwright/test';

import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  mockExternalRequests,
  waitForStablePage,
} from '../support/audit-page';
import { seedOfflinePosSnapshot } from './new-sale-pos.offline-fixture';

const SELLABLES = [
  goods(
    'batch-long',
    'A product name intentionally long enough to prove that the POS picker and cart keep localized inventory data readable on narrow screens',
    'BATCH-LONG-001',
  ),
  goods('batch-duplicate-a', 'Reusable audit product', 'BATCH-A-001'),
  goods('batch-duplicate-b', 'Reusable audit product', 'BATCH-B-002'),
  goods('batch-short', 'Short product', 'BATCH-SHORT-003'),
  ...Array.from({ length: 6 }, (_, index) =>
    goods(
      `batch-dense-${index}`,
      `Dense cart product ${index + 1} with a deliberately long localized audit label`,
      `BATCH-DENSE-${index + 1}`,
    ),
  ),
] as const;

test.describe('new-sale-pos', () => {
  test('keeps empty-cart search, quick tiles, and batch actions reachable', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await installPosScenario(page);
      await openPos(page);

      await expect(page.locator('.empty-cart')).toBeVisible();
      await expect(page.locator('app-batch-search-bar input')).toBeVisible();
      await expect(page.getByRole('button', { name: 'Search' })).toBeVisible();
      await expect(page.locator('app-batch-search-bar .batch-search-actions button')).toHaveCount(3);
      await expect(page.getByRole('button', { name: 'Open Batch Picker' })).toBeVisible();

      await openBatchPicker(page);
      const dialog = page.locator('.p-dialog:visible');
      await expect(dialog.locator('.batch-option')).toHaveCount(SELLABLES.length);
      await assertFitsViewport(dialog);
      const duplicateTile = page
        .locator('.quick-product-tile')
        .filter({ hasText: 'Reusable audit product' });
      await expect(duplicateTile).toHaveCount(2);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

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
      await expect(dialog.locator('.batch-option')).toHaveCount(SELLABLES.length);
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
    const collector = collectBrowserFailures(page);
    try {
      await installPosScenario(page);
      await openPos(page);

      await addBatchAt(page, 3);
      await expect(page.locator('.cart-table')).toBeVisible();

      const customerName = page.locator('app-sale-customer-section input').first();
      await customerName.fill('Alexandria');
      await page.getByText('Alexandria Cassandra Long Customer Name', { exact: true }).click();
      await expect(page.locator('app-sale-payment-section .p-inputnumber-input')).toHaveCount(2);
      await page.locator('app-sale-payment-section .p-select').click();
      await page.getByRole('option', { name: 'UPI' }).click();
      await page.locator('app-sale-payment-section .p-inputnumber-input').first().fill('100');
      await page.locator('app-sale-payment-section .p-inputnumber-input').first().press('Tab');
      await expect(page.locator('.summary-box')).toContainText('Subtotal');
      await expect(page.locator('.summary-box')).toContainText('Tax');
      await expect(page.locator('.summary-box')).toContainText('Balance Due');

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

  test('keeps dense carts and validation feedback usable on narrow screens', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await installPosScenario(page);
      await page.setViewportSize({ width: 360, height: 800 });
      await openPos(page);

      await openBatchPicker(page);
      for (let index = 0; index < 8; index++) {
        await addBatchAt(page, index);
      }
      await expect(page.locator('.cart-item-name')).toHaveCount(8);
      await page.locator('.qty-controls').first().getByRole('button').nth(1).click();
      await expect(page.locator('.qty-value').first()).toHaveText('2');
      await page.locator('app-sale-customer-section input[type="tel"]').fill('bad-phone');
      await page.locator('app-sale-customer-section input[type="tel"]').blur();
      await expect(page.locator('app-sale-customer-section .error-hint')).toBeVisible();
      await page.getByRole('button', { name: 'Record Sale' }).scrollIntoViewIfNeeded();
      await assertNoHorizontalOverflow(page);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('uses IndexedDB-backed offline catalog, banner, and queued confirmation', async ({
    page,
  }) => {
    const collector = collectBrowserFailures(page, {
      ignoreConsole: (message) =>
        message.includes('503') && message.includes('Failed to load resource'),
      ignoreResponse: (response) =>
        response.url().includes('/api/ping') && response.status() === 503,
    });
    try {
      await installPosScenario(page, { offline: true });
      await seedOfflinePosSnapshot(page);
      await openPos(page);

      await expect(page.locator('.status-chip')).toBeVisible();
      await expect(page.locator('.offline-status-banner')).toBeVisible();
      await expect(page.locator('app-sale-customer-section')).toContainText('cached');
      await page.locator('app-batch-search-bar input').fill('offline');
      await page.getByRole('button', { name: 'Search' }).click();
      await expect(page.locator('.p-dialog:visible .batch-option')).toHaveCount(1);
      await addOfflineBatch(page);
      await page.getByRole('button', { name: 'Record Sale' }).click();
      const confirmation = page.locator('.p-dialog:visible');
      await expect(confirmation).toContainText('Sale Queued for Sync');
      await expect(confirmation.getByRole('button', { name: 'Print A4' })).toBeVisible();
      await expect(confirmation.getByRole('button', { name: 'Print Thermal' })).toBeVisible();
      await expect(confirmation.getByRole('button', { name: 'Done' })).toBeVisible();
      await assertFitsViewport(confirmation);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });
});

async function installPosScenario(
  page: Page,
  options: { sellables?: 'ready' | 'error'; offline?: boolean } = {},
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
  if (options.offline) {
    await page.route('**/api/ping**', async (route) =>
      route.fulfill({ status: 503, contentType: 'application/json', body: '{}' }),
    );
  }
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

async function addOfflineBatch(page: Page): Promise<void> {
  const dialog = page.locator('.p-dialog:visible');
  await dialog.locator('.batch-option').click();
  await dialog.getByRole('button', { name: 'Add to Cart' }).click();
  await expect(page.locator('.cart-item-name')).toHaveCount(1);
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
