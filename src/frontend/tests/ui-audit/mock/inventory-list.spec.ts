import {
  chromium,
  expect,
  firefox,
  test,
  webkit,
  type BrowserType,
  type Locator,
  type Page,
} from '@playwright/test';

import { SUPPORTED_LANGUAGES } from '../../../src/app/core/i18n/language.constants';
import { SHELL_ENGLISH_VIEWPORTS, SHELL_LOCALE_VIEWPORTS } from '../fixtures/shell.fixture';
import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  waitForStablePage,
} from '../support/audit-page';
import {
  createInventoryScenario,
  DENSE_INVENTORY_ITEMS,
  INVENTORY_ITEMS,
  installInventoryFixture,
  LONG_INVENTORY_ITEM,
  type InventoryScenario,
} from '../fixtures/inventory.fixture';

const ROUTE = '/inventory';
const ITEMS_URL = 'http://localhost:5277/api/items';

test.describe('inventory-list', () => {
  test('separates loading, empty, populated, and catalog API error states', async ({
    page,
  }, testInfo) => {
    const loading = createInventoryScenario({ apiState: 'loading' });
    await openInventory(page, loading);
    await expect(page.locator('.inventory-page')).toBeVisible();
    await expect(representationFor(page, testInfo.project.name)).toBeVisible();
    await expect(page.locator('app-inventory-filter-bar')).toHaveCount(0);

    await openInventory(page, createInventoryScenario({ items: [] }));
    await expect(emptyStateFor(page, testInfo.project.name)).toBeVisible();
    await expect(page.locator('app-inventory-filter-bar')).toBeVisible();

    await openInventory(page, createInventoryScenario({ items: INVENTORY_ITEMS }));
    await expect(rowsFor(page, testInfo.project.name)).toHaveCount(INVENTORY_ITEMS.length);

    const collector = collectBrowserFailures(page, {
      ignoreConsole: (message) => message.includes('503'),
      ignoreResponse: (response) =>
        response.url().startsWith(ITEMS_URL) && response.status() === 503,
    });
    try {
      await openInventory(page, createInventoryScenario({ apiState: 'error' }));
      await expect(page.locator('.inventory-page .error')).toBeVisible();
      await expect(page.locator('app-inventory-filter-bar')).toHaveCount(0);
      await expect(emptyStateFor(page, testInfo.project.name)).toHaveCount(0);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('filters by search and status, resets paging, and bounds dense values', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await page.setViewportSize({ width: 1440, height: 900 });
      await openInventory(page, createInventoryScenario({ items: INVENTORY_ITEMS }));
      await selectStatus(page, 'Active', 'active');
      await expect(page.locator('.desktop-table tbody tr')).toHaveCount(3);
      await selectStatus(page, 'Inactive', 'inactive');
      await expect(page.locator('.desktop-table tbody')).toContainText('Inactive product');
      await selectStatus(page, 'Out of Stock', 'critical');
      await expect(page.locator('.desktop-table tbody')).toContainText('Critical stock product');

      await openInventory(page, createInventoryScenario({ items: DENSE_INVENTORY_ITEMS }));
      const search = page.getByPlaceholder(/Search by product name or barcode/i);
      await search.fill('long-barcode');
      await expect(page.locator('.desktop-table tbody tr')).toHaveCount(1);
      await expect(page.locator('.desktop-table')).toContainText(LONG_INVENTORY_ITEM.name);
      await search.fill('');
      await expect(page.locator('.desktop-table tbody tr')).toHaveCount(20);
      await page.locator('.desktop-table .p-paginator-next').click();
      await expect(page.locator('.desktop-table tbody')).toContainText(
        'Dense inventory product 21',
      );
      await selectStatus(page, 'Active', 'active');
      await expect(page.locator('.desktop-table tbody')).toContainText('Dense inventory product 1');
      await expect(page.locator('.desktop-table tbody')).not.toContainText(
        'Dense inventory product 21',
      );
      await assertDesktopActionsAndLongValues(page);

      await page.setViewportSize({ width: 360, height: 800 });
      await page.reload();
      await waitForStablePage(page);
      await expect(page.locator('.mobile-grid-container')).toBeVisible();
      await selectStatus(page, 'Active', 'active');
      const card = page.locator('.product-card-item').filter({ hasText: LONG_INVENTORY_ITEM.name });
      await expect(card).toBeVisible();
      await assertNoPageOverflow(page);
      await assertWithinViewport(card);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('keeps add and edit overlays usable through barcode validation, scanners, and mutation errors', async ({
    page,
  }) => {
    const collector = collectBrowserFailures(page, {
      ignoreConsole: (message) => message.includes('409'),
      ignoreResponse: (response) =>
        response.url().startsWith(ITEMS_URL) && response.status() === 409,
    });
    try {
      await page.setViewportSize({ width: 360, height: 800 });
      await openInventory(
        page,
        createInventoryScenario({ items: INVENTORY_ITEMS, scannerState: 'detected' }),
      );
      await expect(page.getByRole('button', { name: /Add New Product/i })).toBeVisible();
      await expect(page.getByRole('button', { name: /Print label/i }).first()).toBeVisible();
      await expect(page.getByRole('button', { name: 'Edit' }).first()).toBeVisible();

      await openAddDialog(page);
      await submitDialog(page, /Add New Product/i);
      await expect(page.locator('.field:has(#add-product-name) .validation-message')).toBeVisible();
      await expect(
        page.locator('.field:has(app-inventory-barcode-field) .validation-message'),
      ).toBeVisible();
      await scanBarcode(page);

      const addBarcode = page.locator('app-inventory-barcode-field input');
      await addBarcode.fill('EXISTING-BARCODE');
      await page.getByRole('button', { name: /Generate/i }).click();
      await expect(page.locator('.barcode-replace-confirm')).toBeVisible();
      await page.getByRole('button', { name: /Keep current/i }).click();
      await expect(addBarcode).toHaveValue('EXISTING-BARCODE');
      await assertDialogFits(page);
      await page.locator('.dialog-actions button').first().click();

      await openInventory(
        page,
        createInventoryScenario({ items: INVENTORY_ITEMS, mutationState: 'error' }),
      );
      await openAddDialog(page);
      await fillAddDialog(page);
      await submitDialog(page, /Add New Product/i);
      await expect(page.getByRole('alert')).toBeVisible();
      await expect(page.locator('.mobile-grid-container')).toBeVisible();
      await expect(page.locator('.dialog-actions button').last()).toBeEnabled();
      await page.locator('.dialog-actions button').first().click();
      await expect(page.locator('.mobile-grid-container')).toBeVisible();

      await page.setViewportSize({ width: 1440, height: 900 });
      await page.reload();
      await waitForStablePage(page);
      await page.getByRole('button', { name: 'Edit' }).first().click();
      await page.locator('#edit-product-name').fill('');
      await page.locator('app-inventory-barcode-field input').fill('');
      await submitDialog(page, /Save/i);
      await expect(
        page.locator('.field:has(#edit-product-name) .validation-message'),
      ).toBeVisible();
      await expect(
        page.locator('.field:has(app-inventory-barcode-field) .validation-message'),
      ).toBeVisible();
      await page.locator('#edit-product-name').fill('Fresh milk retry');
      await page.locator('app-inventory-barcode-field input').fill('DUPLICATE-001');
      await submitDialog(page, /Save/i);
      await expect(page.getByRole('alert')).toBeVisible();
      await expect(page.locator('#edit-product-name')).toHaveValue('Fresh milk retry');
      await expect(page.locator('.dialog-actions button').last()).toBeEnabled();
      await expect(page.locator('.dialog-actions button').first()).toBeEnabled();
      await assertDialogFits(page);
      await expect(page.locator('.desktop-table tbody')).toContainText('Fresh milk');
      await page.locator('.dialog-actions button').first().click();
      await expect(page.locator('.desktop-table')).toBeVisible();

      await openInventory(
        page,
        createInventoryScenario({ items: INVENTORY_ITEMS, scannerState: 'detected' }),
      );
      await page.getByRole('button', { name: 'Edit' }).first().click();
      await scanBarcode(page);
      await page.locator('.dialog-actions button').first().click();

      await openInventory(
        page,
        createInventoryScenario({ items: INVENTORY_ITEMS, scannerState: 'error' }),
      );
      await openAddDialog(page);
      await page.getByRole('button', { name: /Open barcode scanner/i }).click();
      await expect(page.locator('.scanner-error')).toBeVisible();
      await assertDialogFits(page);
      await page.keyboard.press('Escape');

      await openInventory(page, createInventoryScenario({ role: 'Staff', items: INVENTORY_ITEMS }));
      await expect(page.getByRole('button', { name: /Add New Product/i })).toHaveCount(0);
      await expect(page.getByRole('button', { name: /Edit|Print label/i })).toHaveCount(0);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('keeps every supported locale and browser viewport usable with item overlays', async ({
    page,
  }, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-mobile', 'manual browser and locale matrix');
    test.setTimeout(240_000);
    await page.close();

    for (const browserType of [chromium, firefox, webkit]) {
      for (const viewport of SHELL_ENGLISH_VIEWPORTS) {
        await withInventoryMatrixPage(browserType, viewport, 'en-IN');
      }
    }
    for (const locale of SUPPORTED_LANGUAGES) {
      for (const viewport of SHELL_LOCALE_VIEWPORTS) {
        await withInventoryMatrixPage(chromium, viewport, locale);
      }
    }
  });
});

async function openInventory(page: Page, scenario: InventoryScenario): Promise<void> {
  await page.unrouteAll({ behavior: 'ignoreErrors' });
  await installInventoryFixture(page, scenario);
  await page.goto(ROUTE);
  await waitForStablePage(page);
}

function rowsFor(page: Page, projectName: string): Locator {
  return page.locator(
    projectName === 'chromium-mobile' ? '.product-card-item' : '.desktop-table tbody tr',
  );
}

function representationFor(page: Page, projectName: string): Locator {
  return page.locator(
    projectName === 'chromium-mobile' ? '.mobile-grid-container' : '.desktop-table',
  );
}

function emptyStateFor(page: Page, projectName: string): Locator {
  return page.locator(projectName === 'chromium-mobile' ? '.mobile-empty-state' : '.empty-state');
}

async function selectStatus(page: Page, label: string, value: string): Promise<void> {
  const requests: string[] = [];
  const onRequest = (candidate: { url(): string }) => requests.push(candidate.url());
  page.on('request', onRequest);
  await page.locator('app-inventory-filter-bar').getByRole('combobox').click();
  await page.locator('.p-select-option').filter({ hasText: label }).click();
  await expect(page.locator('[data-status-filter-value]')).toHaveText(value);
  await expect
    .poll(() =>
      requests.some((url) => {
        const request = new URL(url);
        return (
          request.pathname === '/api/items' &&
          request.searchParams.get('status') === value &&
          request.searchParams.get('pageNumber') === '1'
        );
      }),
    )
    .toBeTruthy();
  page.off('request', onRequest);
}

async function openAddDialog(page: Page): Promise<void> {
  await page.locator('.inventory-header > button').click();
  await expect(page.locator('.p-dialog:visible')).toBeVisible();
}

async function fillAddDialog(page: Page): Promise<void> {
  await page.locator('#add-product-name').fill('Duplicate barcode product');
  await page.locator('#add-product-uom').fill('PCS');
  await page.locator('app-inventory-barcode-field input').fill('DUPLICATE-001');
}

async function submitDialog(page: Page, name: RegExp): Promise<void> {
  await page.getByRole('button', { name }).last().click();
}

async function scanBarcode(page: Page): Promise<void> {
  await page.getByRole('button', { name: /Open barcode scanner/i }).click();
  await expect(page.locator('.scanner-preview-shell')).toBeVisible();
  await assertDialogFits(page);
  await page.keyboard.press('Escape');
  await expect(page.locator('.p-dialog:visible')).toHaveCount(1);
}

async function assertDesktopActionsAndLongValues(page: Page): Promise<void> {
  const scroll = page.locator('.desktop-table .p-datatable-table-container');
  await expect
    .poll(() => scroll.evaluate((node) => node.scrollWidth > node.clientWidth))
    .toBeTruthy();
  await scroll.evaluate((node) => node.scrollTo({ left: node.scrollWidth }));
  await expect
    .poll(() =>
      scroll.evaluate((node) => node.scrollLeft + node.clientWidth >= node.scrollWidth - 1),
    )
    .toBeTruthy();
  await expect(page.getByRole('button', { name: 'Edit' }).last()).toBeVisible();
  await expect(page.getByRole('button', { name: /Print label/i }).last()).toBeVisible();
  const longName = page
    .locator('.desktop-table tbody tr')
    .filter({ hasText: LONG_INVENTORY_ITEM.name })
    .locator('.truncate')
    .first();
  await expect(longName).toBeVisible();
  await expect
    .poll(() =>
      longName.evaluate((node) => {
        const style = getComputedStyle(node);
        return node.scrollWidth <= node.clientWidth || style.textOverflow === 'ellipsis';
      }),
    )
    .toBeTruthy();
  await assertNoPageOverflow(page);
}

async function withInventoryMatrixPage(
  browserType: BrowserType,
  viewport: { readonly width: number; readonly height: number },
  locale: (typeof SUPPORTED_LANGUAGES)[number],
): Promise<void> {
  const browser = await browserType.launch();
  const context = await browser.newContext({
    viewport,
    locale,
    timezoneId: 'Asia/Kolkata',
    reducedMotion: 'reduce',
    serviceWorkers: 'block',
    baseURL: 'http://127.0.0.1:4300',
  });
  const page = await context.newPage();
  const collector = collectBrowserFailures(page);
  try {
    await openInventory(page, createInventoryScenario({ locale, items: [LONG_INVENTORY_ITEM] }));
    await expect(page.locator('.inventory-page h1')).toBeVisible();
    await expect(
      viewport.width < 1024
        ? page.locator('.product-card-item')
        : page.locator('.desktop-table tbody tr'),
    ).toBeVisible();
    await openAddDialog(page);
    await assertDialogFits(page);
    await page.locator('.dialog-actions button').first().click();
    if (viewport.width < 1024) {
      await page.locator('.product-card-item').first().click();
    } else {
      await page.locator('.desktop-table .action-buttons button').first().click();
    }
    await assertDialogFits(page);
    await page.locator('.dialog-actions button').first().click();
    await assertNoPageOverflow(page);
    assertNoUnexpectedBrowserFailures(collector.failures);
  } finally {
    collector.dispose();
    await context.close();
    await browser.close();
  }
}

async function assertNoPageOverflow(page: Page): Promise<void> {
  await expect
    .poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth))
    .toBeTruthy();
}

async function assertWithinViewport(locator: Locator): Promise<void> {
  const box = await locator.boundingBox();
  expect(box).not.toBeNull();
  expect(box!.x).toBeGreaterThanOrEqual(0);
  expect(box!.x + box!.width).toBeLessThanOrEqual(360);
}

async function assertDialogFits(page: Page): Promise<void> {
  const dialog = page.locator('.p-dialog:visible').first();
  const box = await dialog.boundingBox();
  const viewport = page.viewportSize();
  expect(box).not.toBeNull();
  expect(viewport).not.toBeNull();
  expect(box!.height).toBeLessThanOrEqual(viewport!.height);
  expect(box!.width).toBeLessThanOrEqual(viewport!.width);
}
