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
  { width: 360, height: 800 },
  { width: 768, height: 1024 },
  { width: 1024, height: 768 },
  { width: 1440, height: 900 },
] as const;

test.describe('purchase-orders-list', () => {
  test('renders statuses, dense and long values, filters, pagination, menus, and detail navigation', async ({
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
      await expect(page.locator('.po-row-action-menu-trigger').first()).toBeVisible();
      await page.locator('.po-row-action-menu-trigger').first().click();
      await expect(page.locator('.po-row-action-menu')).toContainText('Edit Purchase Order Draft');

      await page.locator('.po-filter-bar__select').click();
      await expect(page.locator('.p-select-overlay')).toBeVisible();
      await page.keyboard.press('Escape');

      const search = page.getByPlaceholder(/Search PO number/i);
      const filtered = page.waitForRequest((request) =>
        request.url().includes('search=long-reference'),
      );
      await search.fill('long-reference');
      await filtered;
      await expect(page.locator('.po-table-row')).toHaveCount(1);

      const noResults = page.waitForRequest((request) => request.url().includes('search=no-match'));
      await search.fill('no-match');
      await noResults;
      await expect(page.locator('.empty-state')).toBeVisible();

      const cleared = page.waitForRequest((request) => request.url().includes('page_size=20'));
      await page.getByRole('button', { name: 'Clear' }).click();
      await cleared;
      const paged = page.waitForRequest((request) => request.url().includes('page=2'));
      await page.locator('.p-paginator-next').click();
      await paged;

      await page.locator('.po-table-row').first().click();
      await expect(page).toHaveURL(/\/inventory\/purchase-orders\/po-dense-21$/);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('shows explicit loading, empty, and error states', async ({ page }) => {
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
      } else {
        await expect(page.locator('.empty-state')).toBeVisible();
      }
      await page.unrouteAll({ behavior: 'ignoreErrors' });
    }
  });

  test('keeps supplier values, table scrolling, action menus, and labels usable across the supported matrix', async ({}, testInfo) => {
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
          (page) => assertResponsivePurchaseOrders(page, viewport.width),
        );
      }
    }

    for (const locale of SUPPORTED_LANGUAGES) {
      for (const viewport of [VIEWPORTS[0], VIEWPORTS.at(-1)!]) {
        await withPurchaseOrdersPage(
          chromium,
          viewport,
          createPurchaseOrdersScenario({
            locale,
            orders: [LONG_PURCHASE_ORDER],
          }),
          async (page) => {
            await expect(page.locator('.po-page h1')).not.toBeEmpty();
            await assertResponsivePurchaseOrders(page, viewport.width);
          },
        );
      }
    }
  });
});

async function withPurchaseOrdersPage(
  browserType: BrowserType,
  viewport: { readonly width: number; readonly height: number },
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

async function assertResponsivePurchaseOrders(page: Page, viewportWidth: number): Promise<void> {
  const surface = page.locator('.directory-panel__surface');
  const longCell = page.locator('.po-table-supplier').last();
  const menuButton = page.locator('.po-row-action-menu-trigger').last();

  await expect(surface).toBeVisible();
  await expect(longCell).toHaveAttribute('title', LONG_PURCHASE_ORDER.supplierName!);
  await expect(menuButton).toBeVisible();
  await menuButton.click();
  await expect(page.locator('.po-row-action-menu')).toBeVisible();

  const metrics = await page.evaluate(() => {
    const surface = document.querySelector<HTMLElement>('.directory-panel__surface');
    const tableContainer = surface?.querySelector<HTMLElement>('.p-datatable-table-container');
    const supplier = Array.from(document.querySelectorAll<HTMLElement>('.po-table-supplier')).at(
      -1,
    );
    return {
      documentWidth: document.documentElement.scrollWidth,
      surfaceScrolls: [surface, tableContainer].some(
        (element) => !!element && element.scrollWidth > element.clientWidth,
      ),
      supplierTruncates: !!supplier && supplier.scrollWidth > supplier.clientWidth,
    };
  });

  expect(metrics.documentWidth).toBeLessThanOrEqual(viewportWidth);
  expect(metrics.supplierTruncates).toBe(true);
  expect(metrics.surfaceScrolls).toBe(true);
}
