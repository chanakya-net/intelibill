import { expect, test, type Locator, type Page, type Route } from '@playwright/test';

import { SUPPORTED_LANGUAGES } from '../../../src/app/core/i18n/language.constants';
import type {
  SaleDto,
  SaleItemDto,
  SaleListItemDto,
  SaleReturnPreviewDto,
} from '../../../src/app/features/sales/services/sale.models';
import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  mockExternalRequests,
  waitForStablePage,
} from '../support/audit-page';

type DetailMode = 'ready' | 'delayed' | 'error';
type PreviewMode = 'ready' | 'delayed' | 'error';

interface Scenario {
  detailMode: DetailMode;
  previewMode: PreviewMode;
}

const scenario = (): Scenario => ({ detailMode: 'ready', previewMode: 'ready' });

test.describe('sale-detail', () => {
  test('renders dense line items and authoritative totals', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await installScenario(page, scenario());
      const detail = await openDetail(page);

      await expect(detail.locator('.sale-lines-section')).toHaveCount(2);
      await expect(detail.locator('.sale-lines-row:not(.sale-lines-row-head)')).toHaveCount(10);
      await expect(detail).toContainText('₹205.00');
      await expect(detail).toContainText('₹3,050.00');
      await expect(detail).not.toContainText('₹320.00');
      await expect(detail).toContainText('Total Savings');
      await expect(detail).toContainText('Payment Mode');
      await expect(detail).toContainText('Paid Amount');
      await expect(detail).toContainText('Due Amount');
      await expect(detail).toContainText('Partially Paid');
      await expect(detail).toContainText('CN-LONG-SETTLEMENT-0000000001');
      await expect(detail.locator('.sale-lines-section').first()).toHaveCSS('overflow-x', 'auto');

      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('presents detail loading, error, return history, and eligibility states', async ({
    page,
  }) => {
    const state = scenario();
    state.detailMode = 'delayed';
    const collector = collectBrowserFailures(page, {
      ignoreConsole: (message) => message.includes('503'),
      ignoreResponse: (response) =>
        new URL(response.url()).pathname === '/api/sales/sale-audit' && response.status() === 503,
    });
    try {
      await installScenario(page, state);
      await visitSales(page);
      await page.locator('.receipt-btn').click();
      const detail = page.locator('.sale-detail-dialog:visible');
      await expect(detail.getByRole('status')).toContainText('Loading');
      await expect(detail).toContainText('RET-LONG-0000000001');
      await expect(detail.getByRole('button', { name: 'Return items' })).toBeVisible();
      await detail.locator('.sale-detail-close').click();

      state.detailMode = 'error';
      await page.getByRole('button', { name: /view receipt/i }).click();
      await expect(detail.getByRole('alert')).toContainText('Detail unavailable for audit.');
      await expect(detail.getByRole('button', { name: 'Close' })).toBeVisible();
      await detail.getByRole('button', { name: 'Close' }).click();
      await expect(detail).toBeHidden();

      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('exercises return preview loading, success, warning, and error feedback', async ({
    page,
  }) => {
    const state = scenario();
    state.previewMode = 'delayed';
    const collector = collectBrowserFailures(page, {
      ignoreConsole: (message) => message.includes('503'),
      ignoreResponse: (response) =>
        new URL(response.url()).pathname.endsWith('/returns/preview') && response.status() === 503,
    });
    try {
      await installScenario(page, state);
      const detail = await openDetail(page);
      await detail.getByRole('button', { name: 'Return items' }).click();
      const preview = page.locator('.return-preview-dialog:visible');
      await expect(preview).toBeVisible();

      const firstLine = preview.locator('.return-preview-line').first();
      await firstLine.getByRole('checkbox').click();
      await firstLine.locator('p-select').click();
      await page.getByRole('option', { name: 'Restockable' }).click();
      await preview.getByRole('button', { name: 'Preview refund' }).click();
      await expect(preview.locator('.return-preview-shell')).toHaveAttribute('aria-busy', 'true');
      await expect(preview.getByRole('status')).toContainText('Calculated effects');
      await expect(preview.getByRole('alert')).toContainText('Manager approval audit warning');
      await expect(preview).toContainText('Refund payout destination');

      state.previewMode = 'error';
      await preview.getByRole('button', { name: 'Preview refund' }).click();
      await expect(preview.getByRole('alert')).toContainText('Preview unavailable for audit.');

      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('keeps detail and return preview bounded, scrollable, and localized', async ({
    page,
  }, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-mobile', 'manual locale and viewport matrix');
    test.setTimeout(180_000);
    const state = scenario();

    for (const locale of SUPPORTED_LANGUAGES) {
      for (const viewport of [
        { width: 1440, height: 900 },
        { width: 360, height: 800 },
      ]) {
        await installScenario(page, state, locale);
        await page.setViewportSize(viewport);
        const detail = await openDetail(page);
        await assertOverlayFits(detail, viewport);
        await expect(detail.locator('.p-dialog-content')).toHaveCSS('overflow-y', 'auto');
        await expect(detail.locator('.sale-lines-section').first()).toHaveCSS('overflow-x', 'auto');
        await expect(detail).not.toContainText(/sales\.(detail|invoice|returns)\./);

        await detail.locator('.sale-detail-actions .p-button').first().click();
        const preview = page.locator('.return-preview-dialog:visible');
        await assertOverlayFits(preview, viewport);
        await expect(preview.locator('.p-dialog-content')).toHaveCSS('overflow-y', 'auto');
        await expect(preview.locator('.return-preview-lines-card')).toHaveCSS('overflow-x', 'auto');
        await expect(preview).not.toContainText(/sales\.returns\.preview\./);
        await assertNoDocumentOverflow(page, viewport.width);
      }
    }
  });
});

async function installScenario(page: Page, state: Scenario, locale = 'en-IN'): Promise<void> {
  await mockExternalRequests(page, { authenticated: true, locale });
  await page.route('**/api/sales?**', (route) => fulfillSales(route));
  await page.route('**/api/sales/sale-audit', (route) => fulfillDetail(route, state));
  await page.route('**/api/sales/sale-audit/returns/preview', (route) =>
    fulfillPreview(route, state),
  );
}

async function visitSales(page: Page): Promise<void> {
  await page.goto('/sales');
  await waitForStablePage(page);
  await expect(page.locator('.sales-ledger-page')).toBeVisible();
}

async function openDetail(page: Page): Promise<Locator> {
  await visitSales(page);
  await page.locator('.receipt-btn').click();
  const detail = page.locator('.sale-detail-dialog:visible');
  await expect(detail.locator('.sale-detail-shell')).toBeVisible();
  return detail;
}

async function fulfillSales(route: Route): Promise<void> {
  const item = listItem();
  await json(route, {
    items: [item],
    totalCount: 1,
    pageNumber: 1,
    pageSize: 20,
    summary: { periodSales: item.totalAmount, invoiceCount: 1, refundAmount: 45 },
  });
}

async function fulfillDetail(route: Route, state: Scenario): Promise<void> {
  if (route.request().method() !== 'GET') {
    await route.fallback();
    return;
  }
  if (state.detailMode === 'delayed') await delay(700);
  if (state.detailMode === 'error') {
    await route.fulfill({
      status: 503,
      contentType: 'application/json',
      body: JSON.stringify({ detail: 'Detail unavailable for audit.' }),
    });
    return;
  }
  await json(route, detailSale());
}

async function fulfillPreview(route: Route, state: Scenario): Promise<void> {
  if (state.previewMode === 'delayed') await delay(700);
  if (state.previewMode === 'error') {
    await route.fulfill({
      status: 503,
      contentType: 'application/json',
      body: JSON.stringify({ detail: 'Preview unavailable for audit.' }),
    });
    return;
  }
  await json(route, previewResult());
}

function listItem(): SaleListItemDto {
  return {
    saleId: 'sale-audit',
    invoiceNumber: 'INV-LONG-2026-0000000000000001',
    customerId: 'customer-audit',
    customerName: 'Alexandria Cassandra Long Customer Name Without Convenient Breaks',
    customerPhone: '+919999999999999999999999',
    paymentMethod: 4,
    soldAt: '2026-07-20T10:00:00Z',
    paidAmount: 9876543.21,
    dueAmount: 12345.67,
    totalBeforeDiscount: 9900000,
    totalDiscountAmount: 111.12,
    totalAmount: 9888888.88,
    totalTaxAmount: 152542.37,
    itemCount: 10,
    returnNumbers: ['RET-LONG-0000000001'],
    status: 'partiallyPaid',
    refundAmount: 45,
    dueReductionAmount: 0,
    creditNoteAppliedAmount: 25,
  };
}

function detailSale(): SaleDto {
  const items = Array.from({ length: 10 }, (_, index) =>
    saleLine(index, index < 7 ? 'Goods' : 'Service'),
  );
  return {
    ...listItem(),
    creditNoteRedemptionSummaries: [
      {
        creditNoteId: 'credit-audit',
        code: 'CN-LONG-SETTLEMENT-0000000001',
        amount: 25,
      },
    ],
    items,
    returns: [
      {
        saleReturnId: 'return-audit',
        returnNumber: 'RET-LONG-0000000001',
        returnedAt: '2026-07-21T10:00:00Z',
        totalRefundAmount: 45,
        dueReductionAmount: 0,
        payoutAmount: 45,
        isVoided: false,
        voidedAt: null,
        voidReason: null,
        items: [],
      },
    ],
    warnings: [],
  };
}

function saleLine(index: number, lineType: 'Goods' | 'Service'): SaleItemDto {
  const authoritativeTotals = [205, 95, 330, 440, 550, 660, 770, 880, 990, 1100];
  return {
    saleItemId: `line-${index}`,
    lineType,
    itemId: lineType === 'Goods' ? `item-${index}` : null,
    serviceId: lineType === 'Service' ? `service-${index}` : null,
    itemName: `Extremely long ${lineType} line ${index} XXXXXXXXXXXXXXXXXXXXXXXXXXXXX`,
    lineCode: `LINE-CODE-WITHOUT-BREAK-${index}-XXXXXXXXXXXXXXXX`,
    inventoryBatchId: lineType === 'Goods' ? `batch-${index}` : null,
    quantity: index === 0 ? 2 : 1,
    salesPrice: index === 0 ? 110 : 100 + index * 10,
    originalSalesPrice: 150 + index * 10,
    finalSalesPrice: 100 + index * 10,
    preTaxAmountBeforeDiscount: 200 + index * 10,
    itemDiscountAmount: index === 0 ? 15 : 5,
    saleDiscountAmount: 0,
    taxableAmount: 180 + index * 10,
    taxAmount: 20 + index,
    totalAmount: authoritativeTotals[index],
    savingsAmount: 5 + index,
    taxRatePercent: 10,
    isPriceIncludingTax: false,
    hasPriceMismatch: false,
    returnedQuantity: 0,
    returnableQuantity: index < 2 ? 1 : 0,
    returnStatus: index < 2 ? 'Returnable' : 'FullyReturned',
    hsnCode: `HSN-SAC-${index}-XXXXXXXXXXXXXXXX`,
  };
}

function previewResult(): SaleReturnPreviewDto {
  return {
    saleId: 'sale-audit',
    hasFinancialAccess: true,
    lines: [
      {
        saleItemId: 'line-0',
        itemId: 'item-0',
        inventoryBatchId: 'batch-0',
        requestedQuantity: 1,
        returnedQuantity: 0,
        returnableQuantity: 0,
        condition: 1,
        willRestock: true,
        financial: {
          originalCostPrice: 90,
          originalSalesPrice: 110,
          originalTaxRatePercent: 10,
          originalIsPriceIncludingTax: false,
          maxRefundAmount: 121,
          approvedRefundAmount: 121,
          taxableAmount: 110,
          taxAmount: 11,
        },
      },
    ],
    financial: {
      totalRefundAmount: 121,
      dueReductionAmount: 0,
      payoutAmount: 121,
      totalTaxableAmount: 110,
      totalTaxAmount: 11,
      customerBalanceBefore: 0,
      customerBalanceAfter: 0,
    },
    warnings: [
      {
        code: 'MANAGER_APPROVAL',
        message: 'Manager approval audit warning',
        severity: 'Warning',
      },
    ],
  };
}

async function assertOverlayFits(
  overlay: Locator,
  viewport: { width: number; height: number },
): Promise<void> {
  await expect(overlay).toBeVisible();
  const bounds = await overlay.boundingBox();
  expect(bounds).not.toBeNull();
  expect(bounds!.x).toBeGreaterThanOrEqual(-1);
  expect(bounds!.y).toBeGreaterThanOrEqual(-1);
  expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(viewport.width + 1);
  expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(viewport.height + 1);
}

async function assertNoDocumentOverflow(page: Page, viewportWidth: number): Promise<void> {
  const width = await page.evaluate(() =>
    Math.max(document.body.scrollWidth, document.documentElement.scrollWidth),
  );
  expect(width).toBeLessThanOrEqual(viewportWidth + 5);
}

async function json(route: Route, body: unknown): Promise<void> {
  await route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(body),
  });
}

async function delay(milliseconds: number): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, milliseconds));
}
