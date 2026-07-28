import { expect, test, type Page, type Route } from '@playwright/test';

import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  mockExternalRequests,
  waitForStablePage,
} from '../support/audit-page';

type SalesState = 'ready' | 'loading' | 'empty' | 'error';

const LONG_SALES = [
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
] as const;

test.describe('sales-history', () => {
  test('renders dense sales values, filter controls, pagination, and row action', async ({
    page,
  }) => {
    const collector = collectBrowserFailures(page);
    try {
      await openSales(page, 'ready');

      await expect(page.locator('tbody')).toContainText(LONG_SALES[0].invoiceNumber);
      await expect(page.locator('tbody')).toContainText('Partially paid');
      await expect(page.locator('tbody')).toContainText('UPI');
      await expect(page.locator('tbody')).toContainText('Credit');
      await expect(page.locator('.status-filter')).toContainText('Partially paid');
      await expect(page.locator('.page-status')).toContainText('Page 1 of 3');

      const pageTwoResponse = page.waitForResponse((response) => {
        const url = new URL(response.url());
        return url.pathname === '/api/sales' && url.searchParams.get('page') === '2';
      });
      await page.getByRole('button', { name: /next page/i }).click();
      await pageTwoResponse;

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

  test('keeps the sales table scrollable without page overflow at audit viewports', async ({
    page,
  }) => {
    const collector = collectBrowserFailures(page);
    try {
      await installSalesScenario(page, 'ready');
      for (const viewport of [
        { width: 1440, height: 900 },
        { width: 768, height: 1024 },
        { width: 360, height: 800 },
      ]) {
        await page.setViewportSize(viewport);
        await visitSales(page);
        await expect(page.locator('tbody')).toContainText(LONG_SALES[0].customerName);
        await expect(page.locator('.table-wrap')).toHaveCSS('overflow-x', 'auto');
        const tableDimensions = await page.locator('.table-wrap').evaluate((element) => ({
          clientWidth: element.clientWidth,
          scrollWidth: element.scrollWidth,
        }));
        expect(tableDimensions.scrollWidth).toBeGreaterThanOrEqual(tableDimensions.clientWidth);
        await assertNoPageOverflow(page);
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
): Promise<void> {
  await mockExternalRequests(page, { authenticated: true });
  await page.route('**/api/sales?**', async (route) => fulfillSales(route, state));
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

  const items = state === 'empty' ? [] : LONG_SALES;
  const requestUrl = new URL(route.request().url());
  const pageNumber = Number(requestUrl.searchParams.get('page') ?? '1');
  const pageSize = Number(requestUrl.searchParams.get('pageSize') ?? '20');
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

function sale(
  overrides: Partial<(typeof LONG_SALES)[number]> & { saleId: string; invoiceNumber: string },
) {
  return {
    saleId: overrides.saleId,
    invoiceNumber: overrides.invoiceNumber,
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
    ...overrides,
  };
}
