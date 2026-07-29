import { expect, type Locator, type Page, type Route } from '@playwright/test';

import { mockExternalRequests, waitForStablePage } from '../support/audit-page';
import { offlinePosLocalStorageEntries } from './new-sale-pos.offline-fixture';

export const SELLABLES = [
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

interface PosScenarioOptions {
  readonly sellables?: 'ready' | 'error';
  readonly offline?: boolean;
}

interface PreviewItem {
  readonly clientLineKey: string;
  readonly itemName: string;
  readonly inventoryBatchId: string;
  readonly batchNumber: string;
  readonly quantity: number;
  readonly salesPrice: number;
  readonly taxRatePercent: number;
  readonly isPriceIncludingTax: boolean;
}

export async function installPosScenario(
  page: Page,
  options: PosScenarioOptions = {},
): Promise<void> {
  await mockExternalRequests(page, {
    authenticated: true,
    localStorageEntries: options.offline ? offlinePosLocalStorageEntries() : undefined,
  });
  await installPosRoutes(page, options);
}

async function installPosRoutes(page: Page, options: PosScenarioOptions): Promise<void> {
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
      body: JSON.stringify(previewFor(route.request().postDataJSON() as { items: PreviewItem[] })),
    }),
  );
  await page.route('**/api/sales', async (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        saleId: 'sale-ui-audit',
        invoiceNumber: 'INV-UI-AUDIT-0001',
        totalAmount: 337.5,
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

function previewFor(request: { items: readonly PreviewItem[] }) {
  const grossTotal = request.items.reduce((sum, item) => sum + item.quantity * item.salesPrice, 0);
  const totalDiscountAmount = round(grossTotal * 0.1);
  const totalAmount = round(grossTotal - totalDiscountAmount);
  const totalTaxableAmount = round(totalAmount / 1.18);
  const totalTaxAmount = round(totalAmount - totalTaxableAmount);
  return {
    totalAmount,
    totalTaxableAmount,
    totalTaxAmount,
    totalDiscountAmount,
    saleLevelEligibleSubtotal: grossTotal,
    configuredSaleRule: {
      ruleId: 'ui-audit-sale-discount',
      ruleType: 'SalePercentage',
      percentage: 10,
      thresholdAmount: null,
    },
    infos: [],
    warnings: [],
    lines: request.items.map(previewLine),
  };
}

function previewLine(item: PreviewItem) {
  const grossTotal = item.quantity * item.salesPrice;
  const saleDiscountAmount = round(grossTotal * 0.1);
  const lineTotalAmount = round(grossTotal - saleDiscountAmount);
  const taxableAmount = round(lineTotalAmount / 1.18);
  return {
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
    preTaxAmountBeforeDiscount: round(grossTotal / 1.18),
    itemDiscountAmount: 0,
    saleDiscountAmount,
    taxableAmount,
    taxAmount: round(lineTotalAmount - taxableAmount),
    lineTotalAmount,
    maxAllowedItemDiscountFlat: grossTotal,
    maxAllowedItemDiscountPercent: 100,
    configuredBatchRuleId: null,
    configuredBatchRulePercentage: null,
    hasClientPriceMismatch: false,
    clientLineKey: item.clientLineKey,
  };
}

function round(value: number): number {
  return Number(value.toFixed(2));
}

export async function openPos(page: Page): Promise<void> {
  await page.goto('/sales/new');
  await waitForStablePage(page);
  await expect(page.locator('.new-sale-page')).toBeVisible();
}

export async function openBatchPicker(page: Page): Promise<void> {
  await page.locator('app-batch-search-bar input').fill('audit');
  await page.getByRole('button', { name: 'Search' }).click();
}

export async function addBatchAt(page: Page, index: number, quantity = 1): Promise<void> {
  if (index > 0) {
    await openBatchPicker(page);
  }
  const dialog = page.locator('.p-dialog:visible');
  await expect(dialog).toBeVisible();
  await dialog.locator('.batch-option').nth(index).click();
  if (quantity !== 1) {
    const input = dialog.locator('.p-inputnumber-input');
    await input.fill(String(quantity));
    await input.press('Tab');
    await expect(input).toHaveValue(String(quantity));
  }
  const preview = page.waitForResponse(
    (response) => new URL(response.url()).pathname === '/api/sales/preview',
  );
  await dialog.getByRole('button', { name: 'Add to Cart' }).click();
  await preview;
}

export async function addOfflineBatch(page: Page): Promise<void> {
  const dialog = page.locator('.p-dialog:visible');
  await dialog.locator('.batch-option').click();
  await dialog.getByRole('button', { name: 'Add to Cart' }).click();
  await expect(page.locator('.cart-item-name')).toHaveCount(1);
}

export async function assertFitsViewport(locator: Locator): Promise<void> {
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

export async function assertNoHorizontalOverflow(page: Page): Promise<void> {
  await expect
    .poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth))
    .toBe(true);
}
