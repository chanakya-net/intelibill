import { expect, test, type Page, type Route } from '@playwright/test';

import { SUPPORTED_LANGUAGES } from '../../../src/app/core/i18n/language.constants';
import type { SaleListItemDto } from '../../../src/app/features/sales/services/sale.models';
import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  mockExternalRequests,
  waitForStablePage,
} from '../support/audit-page';

type SalesState = 'ready' | 'loading' | 'empty' | 'error';

const LONG_SALES: readonly SaleListItemDto[] = [
  sale({
    saleId: 'sale-paid',
    invoiceNumber: 'INV-2026-VERY-LONG-REFERENCE-0000000000001',
    customerName: 'Alexandria Cassandra Long Customer Name For Responsive Sales History Audits',
    customerPhone: '+919999999999999999999',
    paymentMethod: 2,
    status: 'paid',
  }),
  sale({
    saleId: 'sale-partial',
    invoiceNumber: 'INV-2026-0002',
    paymentMethod: 4,
    status: 'partiallyPaid',
    dueAmount: 125.5,
  }),
  sale({
    saleId: 'sale-refund',
    invoiceNumber: 'INV-2026-0003',
    paymentMethod: 3,
    status: 'refunded',
    refundAmount: 40,
  }),
];

const PAGE_TWO_SALES: readonly SaleListItemDto[] = [
  sale({
    saleId: 'sale-paid',
    invoiceNumber: 'INV-PAGE-TWO-ONLY',
    customerName: 'Page Two Customer',
    paymentMethod: 1,
    status: 'paid',
  }),
];

const FILTERED_SALES: readonly SaleListItemDto[] = [
  sale({
    saleId: 'sale-filtered',
    invoiceNumber: 'INV-FILTERED-PAID',
    customerName: 'Filtered Paid Customer',
    paymentMethod: 1,
    status: 'paid',
  }),
];

const SEARCH_SALES: readonly SaleListItemDto[] = [
  sale({
    saleId: 'sale-searched',
    invoiceNumber: 'INV-SEARCHED-PAID',
    customerName: 'Alexandria Search Result',
    paymentMethod: 1,
    status: 'paid',
  }),
];

test.describe('sales-history', () => {
  test('filters, clears, paginates, and opens a dense sales row', async ({
    page,
  }) => {
    const collector = collectBrowserFailures(page);
    const salesRequests: string[] = [];
    try {
      await installSalesScenario(page, 'ready', [], salesRequests);
      await visitSales(page);

      await expect(page.locator('tbody')).toContainText(LONG_SALES[0].invoiceNumber);
      await expect(page.locator('tbody')).toContainText('Partially paid');
      await expect(page.locator('tbody')).toContainText('UPI');
      await expect(page.locator('tbody')).toContainText('Credit');
      await expect(page.locator('.status-filter')).toContainText('Partially paid');
      await expect(page.locator('.page-status')).toContainText('Page 1 of 3');

      const statusResponse = waitForSalesResponse(page, { status: 'paid', page: '1' });
      await page.locator('.status-filter').getByRole('button', { name: 'Paid', exact: true }).click();
      await statusResponse;
      await expect(page.locator('tbody')).toContainText(FILTERED_SALES[0].invoiceNumber);

      const searchResponse = waitForSalesResponse(page, {
        status: 'paid',
        search: 'Alexandria',
        page: '1',
      });
      await page.locator('.search-field input').fill('Alexandria');
      await searchResponse;
      await expect(page.locator('tbody')).toContainText(SEARCH_SALES[0].invoiceNumber);

      const clearResponse = waitForSalesResponse(page, { page: '1', status: null, search: null });
      await page.locator('.clear-filters-btn').click();
      await clearResponse;
      await expect(page.locator('.search-field input')).toHaveValue('');
      await expect(page.locator('tbody')).toContainText(LONG_SALES[0].invoiceNumber);

      const requestCountBeforePageTwo = salesRequests.length;
      const pageTwoResponse = page.waitForResponse((response) => {
        const url = new URL(response.url());
        return url.pathname === '/api/sales' && url.searchParams.get('page') === '2';
      });
      await page.getByRole('button', { name: /next page/i }).click();
      await pageTwoResponse;
      await expect(page.locator('.page-status')).toContainText('Page 2 of 3');
      await expect(page.locator('.showing')).toContainText(/21.*40.*60/);
      await expect(page.locator('tbody')).toContainText(PAGE_TWO_SALES[0].invoiceNumber);
      expect(salesRequests.slice(requestCountBeforePageTwo)).toHaveLength(1);

      const saleDetailResponse = page.waitForResponse(
        (response) => new URL(response.url()).pathname === '/api/sales/sale-paid',
      );
      await page
        .getByRole('button', { name: /view receipt/i })
        .first()
        .click();
      await saleDetailResponse;
      await expect(page.locator('.p-dialog')).toBeVisible();

      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('keeps sales controls and dense data usable in every locale and audit viewport', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-mobile', 'manual locale and viewport matrix');
    test.setTimeout(120_000);
    const collector = collectBrowserFailures(page);
    try {
      for (const locale of SUPPORTED_LANGUAGES) {
        for (const viewport of [
          { width: 1440, height: 900 },
          { width: 360, height: 800 },
        ]) {
          await installSalesScenario(page, 'ready', [], [], locale);
          await page.setViewportSize(viewport);
          await visitSales(page);
          await expect(page.locator('tbody')).toContainText(LONG_SALES[0].customerName!);
          await expect(page.locator('.table-wrap')).toHaveCSS('overflow-x', 'auto');
          const tableDimensions = await page.locator('.table-wrap').evaluate((element) => ({
            clientWidth: element.clientWidth,
            scrollWidth: element.scrollWidth,
          }));
          expect(tableDimensions.scrollWidth).toBeGreaterThanOrEqual(tableDimensions.clientWidth);
          await assertSalesControlsFit(page);
          await expect(page.locator('.sales-ledger-page')).not.toContainText('sales.history.');
          await assertNoPageOverflow(page);
        }
      }
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('presents loading, empty, and error feedback without active list controls', async ({
    page,
  }) => {
    const loadingCollector = collectBrowserFailures(page);
    try {
      await installSalesScenario(page, 'loading');
      await page.goto('/sales');
      await expect(page.locator('p-skeleton').first()).toBeVisible();
      await expect(page.locator('.clear-filters-btn')).toBeDisabled();
      await expect(page.locator('.search-field input')).toBeDisabled();
      assertNoUnexpectedBrowserFailures(loadingCollector.failures);
    } finally {
      loadingCollector.dispose();
    }

    const emptyCollector = collectBrowserFailures(page);
    try {
      await openSales(page, 'empty');
      await expect(page.locator('.empty-state')).toBeVisible();
      await expect(page.locator('.empty-state')).toContainText('No sales found');
      assertNoUnexpectedBrowserFailures(emptyCollector.failures);
    } finally {
      emptyCollector.dispose();
    }

    const errorCollector = collectBrowserFailures(page, {
      ignoreConsole: (message) =>
        message.includes('503') && message.includes('Failed to load resource'),
      ignoreResponse: (response) =>
        response.url().includes('/api/sales') && response.status() === 503,
    });
    try {
      await openSales(page, 'error');
      await expect(page.locator('.error[role="alert"]')).toContainText('Unable to load sales.');
      await expect(page.locator('.ledger-panel')).toBeVisible();
      assertNoUnexpectedBrowserFailures(errorCollector.failures);
    } finally {
      errorCollector.dispose();
    }
  });

  test('opens export format choices and triggers the chosen export request', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    const exportRequests: string[] = [];
    try {
      await installSalesScenario(page, 'ready', exportRequests);
      await visitSales(page);

      await page.locator('[data-export-trigger]').click();
      await expect(page.getByRole('menuitem', { name: 'PDF' })).toBeVisible();
      await page.getByRole('menuitem', { name: 'PDF' }).click();
      await expect.poll(() => exportRequests).toHaveLength(1);
      expect(exportRequests[0]).toContain('format=pdf');

      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });
});

async function openSales(page: Page, state: SalesState): Promise<void> {
  await installSalesScenario(page, state);
  await visitSales(page);
}

async function installSalesScenario(
  page: Page,
  state: SalesState,
  exportRequests: string[] = [],
  salesRequests: string[] = [],
  locale = 'en-IN',
): Promise<void> {
  await mockExternalRequests(page, { authenticated: true, locale });
  await page.route('**/api/sales?**', async (route) => {
    salesRequests.push(route.request().url());
    await fulfillSales(route, state);
  });
  await page.route('**/api/sales/sale-paid', async (route) => fulfillSaleDetail(route));
  await page.route('**/api/exports/sales?**', async (route) => {
    exportRequests.push(route.request().url());
    await route.fulfill({
      status: 200,
      contentType: 'application/pdf',
      headers: { 'content-disposition': 'attachment; filename="sales.pdf"' },
      body: 'audit export',
    });
  });
}

async function visitSales(page: Page): Promise<void> {
  await page.goto('/sales');
  await waitForStablePage(page);
  await expect(page.locator('.sales-ledger-page')).toBeVisible();
}

async function fulfillSales(route: Route, state: SalesState): Promise<void> {
  if (state === 'loading') {
    await new Promise((resolve) => setTimeout(resolve, 1_000));
  }

  if (state === 'error') {
    await route.fulfill({
      status: 503,
      contentType: 'application/json',
      body: JSON.stringify({ detail: 'Unable to load sales.' }),
    });
    return;
  }

  const requestUrl = new URL(route.request().url());
  const pageNumber = Number(requestUrl.searchParams.get('page') ?? '1');
  const pageSize = Number(requestUrl.searchParams.get('pageSize') ?? '20');
  const items = getSalesItems(state, requestUrl, pageNumber);
  await route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      items,
      totalCount: state === 'empty' ? 0 : 60,
      pageNumber,
      pageSize,
      summary: { periodSales: 123456.78, invoiceCount: items.length, refundAmount: 40 },
    }),
  });
}

function getSalesItems(
  state: SalesState,
  requestUrl: URL,
  pageNumber: number,
): readonly SaleListItemDto[] {
  if (state === 'empty') return [];
  if (requestUrl.searchParams.get('search') === 'Alexandria') return SEARCH_SALES;
  if (requestUrl.searchParams.get('status') === 'paid') return FILTERED_SALES;
  return pageNumber === 2 ? PAGE_TWO_SALES : LONG_SALES;
}

async function fulfillSaleDetail(route: Route): Promise<void> {
  await route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      ...LONG_SALES[0],
      creditNoteAppliedAmount: 0,
      items: [],
      returns: [],
      warnings: [],
    }),
  });
}

async function assertNoPageOverflow(page: Page): Promise<void> {
  const dimensions = await page.evaluate(() => ({
    body: document.body.scrollWidth,
    document: document.documentElement.scrollWidth,
    viewport: window.innerWidth,
  }));
  expect(dimensions.body).toBeLessThanOrEqual(dimensions.viewport + 5);
  expect(dimensions.document).toBeLessThanOrEqual(dimensions.viewport + 5);
}

async function assertSalesControlsFit(page: Page): Promise<void> {
  const clippedControls = await page
    .locator('.status-filter, .search-actions, .inline-export, .pagination-bar')
    .evaluateAll((elements) =>
      elements
        .map((element) => element.getBoundingClientRect())
        .filter((bounds) => bounds.left < 0 || bounds.right > window.innerWidth),
    );
  expect(clippedControls).toHaveLength(0);
}

function waitForSalesResponse(
  page: Page,
  expected: Record<string, string | null>,
): Promise<import('@playwright/test').Response> {
  return page.waitForResponse((response) => {
    const url = new URL(response.url());
    return (
      url.pathname === '/api/sales' &&
      Object.entries(expected).every(([key, value]) => url.searchParams.get(key) === value)
    );
  });
}

function sale(
  overrides: Partial<SaleListItemDto> & Pick<SaleListItemDto, 'saleId' | 'invoiceNumber'>,
): SaleListItemDto {
  const { saleId, invoiceNumber, ...saleOverrides } = overrides;
  return {
    saleId,
    invoiceNumber,
    customerId: null,
    paymentMethod: 1,
    soldAt: '2026-05-27T10:00:00.000Z',
    paidAmount: 1000,
    dueAmount: 0,
    totalBeforeDiscount: 1000,
    totalDiscountAmount: 0,
    totalAmount: 1000,
    totalTaxAmount: 152.54,
    customerName: 'Walk-in customer',
    customerPhone: null,
    itemCount: 3,
    returnNumbers: ['RETURN-00000000000001'],
    status: 'paid',
    refundAmount: 0,
    dueReductionAmount: 0,
    creditNoteAppliedAmount: 0,
    ...saleOverrides,
  };
}
