import { expect, test, type Page } from '@playwright/test';

import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  mockExternalRequests,
  type BrowserFailure,
} from '../support/audit-page';

const BATCHES_URL = 'http://localhost:5277/api/inventory/batches';

test.describe('inventory-batches-list', () => {
  test('keeps loading separate from an API error state', async ({ page }) => {
    const loadingCollector = collectBrowserFailures(page);
    try {
      await openBatches(page, 'loading');
      await expect(page.locator('.batches-table-loading')).toBeVisible();
      await expect(page.locator('.mobile-empty-state')).toBeHidden();
      await expect(page.locator('.desktop-table')).toBeHidden();
      assertNoUnexpectedBrowserFailures(loadingCollector.failures);
    } finally {
      loadingCollector.dispose();
    }

    const errorCollector = collectBrowserFailures(page, {
      ignoreConsole: (message) => message.includes('503') && message.includes('Failed to load'),
      ignoreResponse: (response) => response.url() === BATCHES_URL && response.status() === 503,
    });
    try {
      await openBatches(page, 'error');
      await expect(page.locator('.batches-error')).toBeVisible();
      await expect(page.locator('.batches-error')).toContainText(
        'Failed to load inventory batches',
      );
      await expect(page.locator('.mobile-empty-state')).toBeHidden();
      await expect(page.locator('.desktop-table')).toBeHidden();
      assertNoUnexpectedBrowserFailures(filterBatchRequestFailures(errorCollector.failures));
    } finally {
      errorCollector.dispose();
    }
  });

  test('filters populated data, exposes no-results, and opens row actions', async ({
    page,
  }, testInfo) => {
    const collector = collectBrowserFailures(page);
    try {
      await openBatches(page, 'ready', BATCHES);
      const isMobile = testInfo.project.name === 'chromium-mobile';
      const rows = page.locator(isMobile ? '.batch-card-item' : '.desktop-table tbody tr');
      await expect(rows).toHaveCount(BATCHES.length);

      const search = page.locator('input[name="batchFilterSearch"]');
      await search.fill('voided');
      await expect(rows).toHaveCount(1);
      await expect(rows).toContainText('Voided inventory batch');

      await search.fill('does-not-exist');
      await expect(page.locator(isMobile ? '.mobile-empty-state' : '.empty-state')).toBeVisible();
      await search.fill('active');

      const actions = rows.first().getByRole('button', { name: 'Actions' });
      await actions.click();
      const menu = page.locator('.p-menu:visible');
      await expect(menu).toBeVisible();
      await expect(menu.getByText('Adjust Batch', { exact: true })).toBeVisible();
      await expect(menu).toHaveCSS('z-index', /\d+/);
      await menu.getByText('Adjust Batch', { exact: true }).click();
      await expect(page.locator('.p-dialog:visible')).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('preserves table scrolling, pagination, and bounded mobile cards for dense long values', async ({
    page,
  }) => {
    const collector = collectBrowserFailures(page);
    try {
      await page.setViewportSize({ width: 1440, height: 900 });
      await openBatches(page, 'ready', DENSE_BATCHES);
      await expect(page.locator('.desktop-table')).toBeVisible();
      await expect(page.locator('.mobile-grid-container')).toBeHidden();
      await expect(page.locator('.p-paginator')).toBeVisible();
      await expect(page.locator('.p-datatable-table-container')).toHaveCount(1);
      await expect
        .poll(() =>
          page
            .locator('.p-datatable-table-container')
            .evaluate((element) => element.scrollWidth >= element.clientWidth),
        )
        .toBe(true);
      await page.locator('.p-paginator-next').click();
      await expect(page.locator('.desktop-table tbody')).toContainText('Dense batch item 9');

      await page.setViewportSize({ width: 375, height: 667 });
      await page.reload();
      await expect(page.locator('.mobile-grid-container')).toBeVisible();
      await expect(page.locator('.desktop-table')).toBeHidden();
      await expect(page.locator('.batch-card-item')).toHaveCount(DENSE_BATCHES.length);
      const longCard = page.locator('.batch-card-item').filter({ hasText: LONG_BATCH.itemName });
      await expect(longCard).toHaveCount(1);
      await assertBounds(page, '.batch-card-item, .batches-filter-bar, .batches-header');

      const actions = longCard.getByRole('button', {
        name: 'Actions',
      });
      await actions.click();
      await expect(page.locator('.p-menu:visible')).toContainText('Print labels');
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('renders translated batches text in Hindi', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await openBatches(page, 'ready', BATCHES, 'hi-IN');
      await expect(page.locator('.batches-page h1')).toHaveText('इन्वेंटरी बैचेस');
      await expect(page.locator('.batches-filter-bar')).toContainText('स्थिति');
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });
});

type BatchState = 'loading' | 'error' | 'ready';

interface MockBatch {
  readonly id: string;
  readonly shopId: string;
  readonly itemId: string;
  readonly itemName: string;
  readonly barcode: string;
  readonly batchNumber: string;
  readonly quantity: number;
  readonly originalQuantity: number;
  readonly costPrice: number;
  readonly mrp: number;
  readonly salesPrice: number;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
  readonly expiryDate: string | null;
  readonly manufacturingDate: string | null;
  readonly supplierId: string | null;
  readonly supplierName: string | null;
  readonly isVoided: boolean;
  readonly createdAt: string;
  readonly updatedAt: string | null;
}

const LONG_BATCH = createBatch('1', {
  itemName:
    'Extraordinarily long inventory item name that must remain inside a compact mobile batch card',
  barcode: 'LONG-BARCODE-1234567890123456789012345678901234567890',
  batchNumber: 'LONG-BATCH-NUMBER-1234567890123456789012345678901234567890',
});
const BATCHES = [
  LONG_BATCH,
  createBatch('2', { itemName: 'Voided inventory batch', isVoided: true }),
  createBatch('3', { itemName: 'Active stock batch' }),
];
const DENSE_BATCHES = Array.from({ length: 12 }, (_, index) =>
  index === 0
    ? LONG_BATCH
    : createBatch(String(index + 1), { itemName: `Dense batch item ${index + 1}` }),
);

async function openBatches(
  page: Page,
  state: BatchState,
  batches: readonly MockBatch[] = [],
  locale = 'en-IN',
): Promise<void> {
  await mockExternalRequests(page, { authenticated: true, locale });
  await page.route('http://localhost:5277/api/suppliers', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
  });
  await page.route(BATCHES_URL, async (route) => {
    if (state === 'loading') {
      await new Promise<void>(() => undefined);
      return;
    }

    if (state === 'ready') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(batches),
      });
      return;
    }

    await route.fulfill({
      status: 503,
      contentType: 'application/json',
      body: JSON.stringify({ title: 'Inventory.LoadFailed' }),
    });
  });
  await page.goto('/inventory/batches');
  await expect(page.locator('.batches-page')).toBeVisible();
}

function createBatch(id: string, patch: Partial<MockBatch> = {}): MockBatch {
  return {
    id: `batch-${id}`,
    shopId: 'ui-audit-shop',
    itemId: `item-${id}`,
    itemName: `Batch item ${id}`,
    barcode: `BARCODE-${id}`,
    batchNumber: `BATCH-${id}`,
    quantity: 10,
    originalQuantity: 10,
    costPrice: 90,
    mrp: 120,
    salesPrice: 110,
    taxRatePercent: 5,
    taxIncluded: false,
    expiryDate: null,
    manufacturingDate: null,
    supplierId: null,
    supplierName: null,
    isVoided: false,
    createdAt: '2026-07-29T00:00:00.000Z',
    updatedAt: null,
    ...patch,
  };
}

async function assertBounds(page: Page, selector: string): Promise<void> {
  const outsideViewport = await page.locator(selector).evaluateAll((elements) =>
    elements.some((element) => {
      const box = element.getBoundingClientRect();
      return box.left < 0 || box.right > innerWidth;
    }),
  );

  expect(outsideViewport).toBe(false);
}

function filterBatchRequestFailures(failures: readonly BrowserFailure[]): BrowserFailure[] {
  return failures.filter(
    (failure) => !(failure.kind === 'response' && failure.url === BATCHES_URL),
  );
}
