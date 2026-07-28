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

  test('renders a successful empty response without loading or error UI', async ({
    page,
  }, testInfo) => {
    const collector = collectBrowserFailures(page);
    try {
      await openBatches(page, 'ready', []);
      const isMobile = testInfo.project.name === 'chromium-mobile';
      const emptyState = page.locator(isMobile ? '.mobile-empty-state' : '.empty-state');

      await expect(emptyState).toBeVisible();
      await expect(emptyState).toContainText('No products added yet');
      await expect(
        page.locator(
          isMobile ? '.batch-card-item' : '.desktop-table tbody tr:has(td:not([colspan]))',
        ),
      ).toHaveCount(0);
      await expect(page.locator('.batches-table-loading, .batches-error')).toBeHidden();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
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
      await search.fill('');

      await selectBatchStatus(page, 'Voided');
      await expect(rows).toHaveCount(1);
      await expect(rows).toContainText('Voided inventory batch');

      await selectBatchStatus(page, 'All');
      await selectBatchDate(page, 'batch-from-date', '2026-6-28');
      await expect(rows).toHaveCount(2);
      await selectBatchDate(page, 'batch-to-date', '2026-6-28');
      await expect(rows).toHaveCount(1);
      await expect(rows).toContainText('Voided inventory batch');

      await page.getByRole('button', { name: 'Clear' }).click();
      await expect(search).toHaveValue('');
      await expect(rows).toHaveCount(BATCHES.length);

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
      await page.setViewportSize({ width: 1024, height: 900 });
      await openBatches(page, 'ready', DENSE_BATCHES);
      await expect(page.locator('.desktop-table')).toBeVisible();
      await expect(page.locator('.mobile-grid-container')).toBeHidden();
      await expect(page.locator('.p-paginator')).toBeVisible();
      await expect(page.locator('.p-datatable-table-container')).toHaveCount(1);
      await assertDesktopHorizontalScroll(page);
      await page.locator('.p-paginator-next').click();
      await expect(page.locator('.desktop-table tbody')).toContainText('Dense batch item 9');

      await page.setViewportSize({ width: 375, height: 667 });
      await page.reload();
      await expect(page.locator('.mobile-grid-container')).toBeVisible();
      await expect(page.locator('.desktop-table')).toBeHidden();
      await expect(page.locator('.batch-card-item')).toHaveCount(DENSE_BATCHES.length);
      const longCard = page.locator('.batch-card-item').filter({ hasText: LONG_BATCH.itemName });
      await expect(longCard).toHaveCount(1);
      await assertBounds(page, '.batches-filter-bar, .batches-header');
      await assertCardContentsBounded(longCard);

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
  createBatch('2', {
    itemName: 'Voided inventory batch',
    isVoided: true,
    createdAt: '2026-07-28T00:00:00.000Z',
  }),
  createBatch('3', { itemName: 'Active stock batch', createdAt: '2026-07-27T00:00:00.000Z' }),
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

async function assertDesktopHorizontalScroll(page: Page): Promise<void> {
  const tableContainer = page.locator('.p-datatable-table-container');
  const styles = await tableContainer.evaluate((element) => getComputedStyle(element).overflowX);

  expect(styles).toMatch(/auto|scroll/);
  await expect
    .poll(() => tableContainer.evaluate((element) => element.scrollWidth > element.clientWidth))
    .toBe(true);
}

async function assertCardContentsBounded(card: ReturnType<Page['locator']>): Promise<void> {
  const geometry = await card.evaluate((element) => {
    const cardBounds = element.getBoundingClientRect();
    const details = element.querySelector<HTMLElement>('.batch-card-details');
    const children = Array.from(element.querySelectorAll<HTMLElement>('.batch-card-content > *'));

    return {
      cardHeight: cardBounds.height,
      detailsWidth: details?.getBoundingClientRect().width ?? 0,
      hasOverflowingChild: children.some((child) => {
        const bounds = child.getBoundingClientRect();
        return bounds.left < cardBounds.left - 2 || bounds.right > cardBounds.right + 2;
      }),
    };
  });

  expect(geometry.detailsWidth).toBeGreaterThanOrEqual(160);
  expect(geometry.cardHeight).toBeLessThan(500);
  expect(geometry.hasOverflowingChild).toBe(false);
}

async function selectBatchStatus(page: Page, label: string): Promise<void> {
  await page.locator('p-select[name="batchFilterStatus"]').click();
  await page.getByRole('option', { name: label, exact: true }).click();
}

async function selectBatchDate(page: Page, inputId: string, dateKey: string): Promise<void> {
  const input = page.locator(`#${inputId}`);
  const calendar = page.locator('.p-datepicker-panel:visible');

  await input.click();
  await expect(calendar).toBeVisible();
  await calendar.locator(`[data-date="${dateKey}"]`).click();
  await expect(calendar).toBeHidden();
}

function filterBatchRequestFailures(failures: readonly BrowserFailure[]): BrowserFailure[] {
  return failures.filter(
    (failure) => !(failure.kind === 'response' && failure.url === BATCHES_URL),
  );
}
