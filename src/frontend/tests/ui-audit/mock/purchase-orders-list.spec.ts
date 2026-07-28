import {
  chromium,
  expect,
  firefox,
  test,
  webkit,
  type BrowserType,
  type Page,
} from '@playwright/test';

import { SUPPORTED_LANGUAGES } from '../../../src/app/core/i18n/language.constants';
import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  waitForStablePage,
} from '../support/audit-page';
import {
  DENSE_PURCHASE_ORDERS,
  LONG_PURCHASE_ORDER,
  PURCHASE_ORDER_STATUSES,
  createPurchaseOrdersScenario,
  installPurchaseOrdersFixture,
  type PurchaseOrdersScenario,
} from '../fixtures/purchase-orders.fixture';

const ROUTE = '/inventory/purchase-orders';
const VIEWPORTS = [
  { width: 360, height: 800, summaryColumns: 1 },
  { width: 719, height: 900, summaryColumns: 1 },
  { width: 721, height: 900, summaryColumns: 2 },
  { width: 1099, height: 900, summaryColumns: 2 },
  { width: 1101, height: 900, summaryColumns: 4 },
  { width: 1440, height: 900, summaryColumns: 4 },
] as const;

test.describe('purchase-orders-list', () => {
  test('renders statuses, dense and long values, filters, pagination, and detail navigation', async ({
    page,
  }) => {
    const scenario = createPurchaseOrdersScenario({
      orders: [...DENSE_PURCHASE_ORDERS, LONG_PURCHASE_ORDER],
    });
    const collector = collectBrowserFailures(page);

    try {
      await installPurchaseOrdersFixture(page, scenario);
      await page.goto(ROUTE);
      await waitForStablePage(page);

      await expect(page.locator('.po-status-pill')).toHaveCount(20);
      for (const status of [
        'Draft',
        'Placed',
        'Partially Received',
        'Received',
        'Closed',
        'Cancelled',
      ]) {
        await expect(page.locator('.po-status-pill').filter({ hasText: status })).not.toHaveCount(
          0,
        );
      }
      await expect(page.locator('.po-table-supplier').first()).toHaveText('Audit Supplier');

      const search = page.getByPlaceholder(/Search PO number/i);
      const filtered = waitForListRequest(
        page,
        (url) => url.searchParams.get('search') === 'long-reference',
      );
      await search.fill('long-reference');
      await filtered;
      await expect(page.locator('.po-table-row')).toHaveCount(1);

      const noResults = waitForListRequest(
        page,
        (url) => url.searchParams.get('search') === 'no-match',
      );
      await search.fill('no-match');
      await noResults;
      await expect(page.locator('.empty-state')).toBeVisible();

      const cleared = waitForListRequest(page, (url) => !url.searchParams.has('search'));
      await page.getByRole('button', { name: 'Clear' }).click();
      await cleared;
      const paged = waitForListRequest(page, (url) => url.searchParams.get('page') === '2');
      await page.locator('.p-paginator-next').click();
      await paged;

      await page.locator('.po-table-row').first().click();
      await expect(page).toHaveURL(/\/inventory\/purchase-orders\/po-dense-21$/);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('wires filter controls and permitted popup actions deterministically', async ({ page }) => {
    const scenario = createPurchaseOrdersScenario({ orders: PURCHASE_ORDER_STATUSES });
    const collector = collectBrowserFailures(page);

    try {
      await installPurchaseOrdersFixture(page, scenario);
      await page.goto(ROUTE);
      await waitForStablePage(page);
      await assertActionControls(page, true);

      const statusRequest = waitForListRequest(
        page,
        'status filter',
        (url) => url.searchParams.get('status') === 'Placed',
      );
      await page.locator('.po-filter-bar__select').click();
      await page.locator('.p-select-overlay').getByText('Placed', { exact: true }).click();
      await statusRequest;
      await expect(page.locator('.po-table-row')).toHaveCount(1);
      await expect(page.locator('.po-status-pill')).toHaveText('Placed');

      const cleared = waitForListRequest(
        page,
        'clear status',
        (url) => !url.searchParams.has('status'),
      );
      await page.getByRole('button', { name: 'Clear' }).click();
      await cleared;

      const fromRequest = waitForListRequest(
        page,
        'from-date filter',
        (url) => url.searchParams.get('order_date_from') === '2026-06-15',
      );
      await selectOrderDate(page, 0);
      await fromRequest;
      await expect(page.locator('.po-table-row')).toHaveCount(5);

      const toRequest = waitForListRequest(
        page,
        'to-date filter',
        (url) =>
          url.searchParams.get('order_date_from') === '2026-06-15' &&
          url.searchParams.get('order_date_to') === '2026-06-15',
      );
      await selectOrderDate(page, 1);
      await toRequest;
      await expect(page.locator('.po-table-row')).toHaveCount(1);

      const resetForActions = waitForListRequest(
        page,
        'clear dates',
        (url) => !url.searchParams.has('order_date_from'),
      );
      await page.getByRole('button', { name: 'Clear' }).click();
      await resetForActions;
      const draftRow = rowForStatus(page, 'Draft');
      await draftRow.locator('.po-row-action-menu-trigger').click();
      const actionMenu = page.locator('.po-row-action-menu');
      await expect(actionMenu).toContainText('Edit Purchase Order Draft');
      await expect(actionMenu).toContainText('Place Order');
      const placed = page.waitForRequest((request) => {
        const url = new URL(request.url());
        return (
          request.method() === 'POST' && url.pathname === '/api/purchase-orders/po-draft/place'
        );
      });
      await actionMenu.locator('.p-menu-item').nth(1).click();
      await placed;
      await expect(rowForStatus(page, 'Draft')).toHaveCount(0);
      await expect(rowForStatus(page, 'Placed')).toHaveCount(2);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('hides all row actions from Staff', async ({ page }) => {
    const scenario = createPurchaseOrdersScenario({
      role: 'Staff',
      orders: PURCHASE_ORDER_STATUSES,
    });
    const collector = collectBrowserFailures(page);

    try {
      await installPurchaseOrdersFixture(page, scenario);
      await page.goto(ROUTE);
      await waitForStablePage(page);
      await assertActionControls(page, false);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('shows explicit loading, empty, and non-contradictory error states', async ({ page }) => {
    for (const scenario of [
      createPurchaseOrdersScenario({ apiState: 'loading' }),
      createPurchaseOrdersScenario({ orders: [] }),
      createPurchaseOrdersScenario({ apiState: 'error' }),
    ]) {
      await installPurchaseOrdersFixture(page, scenario);
      await page.goto(ROUTE);
      if (scenario.apiState === 'loading') {
        await expect(page.locator('.directory-panel--loading[aria-busy="true"]')).toBeVisible();
      } else if (scenario.apiState === 'error') {
        await expect(page.getByRole('alert')).toBeVisible();
        await expect(page.locator('.empty-state')).toHaveCount(0);
      } else {
        await expect(page.locator('.empty-state')).toBeVisible();
      }
      await page.unrouteAll({ behavior: 'ignoreErrors' });
    }
  });

  test('keeps responsive values, bounds, and localized controls usable across the matrix', async ({}, testInfo) => {
    test.skip(
      testInfo.project.name !== 'chromium-mobile',
      'scoped browser, viewport, and locale matrix',
    );
    test.setTimeout(180_000);

    for (const browserType of [chromium, firefox, webkit]) {
      for (const viewport of VIEWPORTS) {
        await withPurchaseOrdersPage(
          browserType,
          viewport,
          createPurchaseOrdersScenario({
            orders: [...PURCHASE_ORDER_STATUSES, LONG_PURCHASE_ORDER],
          }),
          (page) => assertResponsivePurchaseOrders(page, viewport),
        );
      }
    }

    for (const locale of SUPPORTED_LANGUAGES) {
      for (const viewport of [VIEWPORTS[0], VIEWPORTS.at(-1)!]) {
        await withPurchaseOrdersPage(
          chromium,
          viewport,
          createPurchaseOrdersScenario({ locale, orders: [LONG_PURCHASE_ORDER] }),
          async (page) => {
            await assertLocalizedLabels(page, locale);
            await assertResponsivePurchaseOrders(page, viewport);
          },
        );
      }
    }
  });
});

function rowForStatus(page: Page, status: string) {
  const tone = status === 'Partially Received' ? 'partial' : status.toLowerCase();
  return page.locator(`.po-table-row:has(.po-status-pill--${tone})`);
}

async function assertActionControls(page: Page, canManage: boolean): Promise<void> {
  for (const status of [
    'Draft',
    'Placed',
    'Partially Received',
    'Received',
    'Closed',
    'Cancelled',
  ]) {
    const trigger = rowForStatus(page, status).locator('.po-row-action-menu-trigger');
    await expect(trigger).toHaveCount(
      canManage && !['Received', 'Closed', 'Cancelled'].includes(status) ? 1 : 0,
    );
  }
}

function waitForListRequest(
  page: Page,
  labelOrMatches: string | ((url: URL) => boolean),
  optionalMatches?: (url: URL) => boolean,
) {
  const label = typeof labelOrMatches === 'string' ? labelOrMatches : 'list';
  const matches = typeof labelOrMatches === 'function' ? labelOrMatches : optionalMatches!;
  return page
    .waitForRequest(
      (request) => {
        if (request.method() !== 'GET') return false;
        const url = new URL(request.url());
        return url.pathname === '/api/purchase-orders' && matches(url);
      },
      { timeout: 4_000 },
    )
    .catch(() => Promise.reject(new Error(`Missing ${label} request.`)));
}

async function selectOrderDate(page: Page, index: number): Promise<void> {
  await page.locator('input.po-filter-bar__date-input').nth(index).click();
  const panel = page.locator('.p-datepicker-panel:visible');
  await panel.locator('.p-datepicker-prev-button').click();
  await panel.locator('.p-datepicker-day').getByText('15', { exact: true }).click();
}

async function withPurchaseOrdersPage(
  browserType: BrowserType,
  viewport: (typeof VIEWPORTS)[number],
  scenario: PurchaseOrdersScenario,
  assertion: (page: Page) => Promise<void>,
): Promise<void> {
  const browser = await browserType.launch();
  const context = await browser.newContext({
    viewport,
    locale: scenario.shell.locale,
    timezoneId: 'Asia/Kolkata',
    colorScheme: 'light',
    reducedMotion: 'reduce',
    serviceWorkers: 'block',
    baseURL: 'http://127.0.0.1:4300',
  });
  const page = await context.newPage();
  const collector = collectBrowserFailures(page);

  try {
    await installPurchaseOrdersFixture(page, scenario);
    await page.goto(ROUTE);
    await waitForStablePage(page);
    await assertion(page);
    assertNoUnexpectedBrowserFailures(collector.failures);
  } finally {
    collector.dispose();
    await context.close();
    await browser.close();
  }
}

async function assertResponsivePurchaseOrders(
  page: Page,
  viewport: (typeof VIEWPORTS)[number],
): Promise<void> {
  const surface = page.locator('.directory-panel__surface');
  const supplier = page.locator('.po-table-supplier').last();
  const reference = page.locator('.po-table-reference').last();
  const menuButton = page.locator('.po-row-action-menu-trigger').last();

  await expect(surface).toBeVisible();
  await expect(supplier).toHaveAttribute('title', LONG_PURCHASE_ORDER.supplierName!);
  await expect(reference).toHaveAttribute('title', LONG_PURCHASE_ORDER.supplierReference!);
  await expect(menuButton).toBeVisible();

  const metrics = await page.evaluate(() => {
    const surface = document.querySelector<HTMLElement>('.directory-panel__surface');
    const tableContainer = surface?.querySelector<HTMLElement>('.p-datatable-table-container');
    const supplier = Array.from(document.querySelectorAll<HTMLElement>('.po-table-supplier')).at(
      -1,
    );
    const reference = Array.from(document.querySelectorAll<HTMLElement>('.po-table-reference')).at(
      -1,
    );
    const rows = Array.from(document.querySelectorAll<HTMLElement>('.po-table-row'));
    const cards = Array.from(document.querySelectorAll<HTMLElement>('.summary-card'));
    const directActions = Array.from(document.querySelectorAll<HTMLElement>('.po-row-actions'));
    const surfaceBounds = surface?.getBoundingClientRect();
    const grid = document.querySelector<HTMLElement>('.summary-grid');

    return {
      documentWidth: document.documentElement.scrollWidth,
      surfaceScrolls: [surface, tableContainer].some(
        (element) => !!element && element.scrollWidth > element.clientWidth,
      ),
      supplierTruncates: !!supplier && supplier.scrollWidth > supplier.clientWidth,
      referenceTruncates: !!reference && reference.scrollWidth > reference.clientWidth,
      rowsInsideSurface:
        !!surfaceBounds &&
        rows.every((row) => {
          const bounds = row.getBoundingClientRect();
          return bounds.top >= surfaceBounds.top && bounds.bottom <= surfaceBounds.bottom;
        }),
      cardsInsideViewport: cards.every((card) => {
        const bounds = card.getBoundingClientRect();
        return bounds.left >= 0 && bounds.right <= window.innerWidth;
      }),
      summaryColumns: grid ? getComputedStyle(grid).gridTemplateColumns.split(' ').length : 0,
      directActionsVisible: directActions.some(
        (actions) => getComputedStyle(actions).display !== 'none',
      ),
    };
  });

  expect(metrics.documentWidth).toBeLessThanOrEqual(viewport.width);
  expect(metrics.supplierTruncates).toBe(true);
  expect(metrics.referenceTruncates).toBe(true);
  expect(metrics.surfaceScrolls).toBe(true);
  expect(metrics.rowsInsideSurface).toBe(true);
  expect(metrics.cardsInsideViewport).toBe(true);
  expect(metrics.summaryColumns).toBe(viewport.summaryColumns);
  expect(metrics.directActionsVisible).toBe(viewport.width > 720);
}

async function assertLocalizedLabels(
  page: Page,
  locale: (typeof SUPPORTED_LANGUAGES)[number],
): Promise<void> {
  const labels = await page.evaluate(async (activeLocale) => {
    const response = await fetch(`/assets/i18n/${activeLocale}.json`);
    const translations = (await response.json()) as {
      purchaseOrders: {
        searchPlaceholder: string;
        actionsLabel: string;
        statusLabel: string;
        list: { supplier: string; supplierReference: string };
      };
    };
    return translations.purchaseOrders;
  }, locale);

  await expect(page.locator('.po-filter-bar__search')).toHaveAttribute(
    'placeholder',
    labels.searchPlaceholder,
  );
  const tableHeader = page.locator('.directory-panel__surface thead');
  await expect(tableHeader).toContainText(labels.statusLabel);
  await expect(tableHeader).toContainText(labels.list.supplier);
  await expect(tableHeader).toContainText(labels.list.supplierReference);
  await expect(page.getByRole('button', { name: labels.actionsLabel }).first()).toBeVisible();
}
