import { expect, test, type Locator, type Page } from '@playwright/test';

import { SUPPORTED_LANGUAGES } from '../../../src/app/core/i18n/language.constants';
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
  test('separates loading, empty, populated, and API error states', async ({ page }, testInfo) => {
    const loading = createInventoryScenario({ apiState: 'loading' });
    await openInventory(page, loading);
    await expect(page.locator('.inventory-page')).toBeVisible();
    await expect(
      page.locator(
        testInfo.project.name === 'chromium-mobile' ? '.mobile-grid-container' : '.desktop-table',
      ),
    ).toBeVisible();
    await expect(page.locator('app-inventory-filter-bar')).toHaveCount(0);

    await openInventory(page, createInventoryScenario({ items: [] }));
    await expect(emptyStateFor(page, testInfo.project.name)).toBeVisible();
    await expect(page.locator('app-inventory-filter-bar')).toBeVisible();

    await openInventory(page, createInventoryScenario({ items: INVENTORY_ITEMS }));
    await expect(rowsFor(page, testInfo.project.name)).toHaveCount(INVENTORY_ITEMS.length);

    const errorCollector = collectBrowserFailures(page, {
      ignoreConsole: (message) => message.includes('503'),
      ignoreResponse: (response) =>
        response.url().startsWith(ITEMS_URL) && response.status() === 503,
    });
    try {
      await openInventory(page, createInventoryScenario({ apiState: 'error' }));
      await expect(page.locator('.inventory-page .error')).toBeVisible();
      await expect(page.locator('app-inventory-filter-bar')).toHaveCount(0);
      await expect(emptyStateFor(page, testInfo.project.name)).toHaveCount(0);
      assertNoUnexpectedBrowserFailures(errorCollector.failures);
    } finally {
      errorCollector.dispose();
    }
  });

  test('filters, paginates, and bounds dense values in desktop and mobile representations', async ({
    page,
  }) => {
    const collector = collectBrowserFailures(page);
    try {
      await page.setViewportSize({ width: 1440, height: 900 });
      await openInventory(page, createInventoryScenario({ items: DENSE_INVENTORY_ITEMS }));
      const search = page.getByPlaceholder(/Search by product name or barcode/i);
      await search.fill('long-barcode');
      await expect(page.locator('.desktop-table tbody tr')).toHaveCount(1);
      await expect(page.locator('.desktop-table')).toContainText(LONG_INVENTORY_ITEM.name);

      await search.fill('not-found');
      await expect(page.locator('.empty-state')).toBeVisible();
      await search.fill('');
      await expect(page.locator('.desktop-table tbody tr')).toHaveCount(20);
      const nextPage = page.waitForResponse(
        (response) => new URL(response.url()).searchParams.get('pageNumber') === '2',
      );
      await page.locator('.desktop-table .p-paginator-next').click();
      await nextPage;
      await expect(page.locator('.desktop-table tbody')).toContainText(
        'Dense inventory product 21',
      );
      await assertHorizontalContainer(page, '.p-datatable-table-container');

      await page.setViewportSize({ width: 360, height: 800 });
      await page.reload();
      await waitForStablePage(page);
      await expect(page.locator('.mobile-grid-container')).toBeVisible();
      const card = page.locator('.product-card-item').filter({ hasText: LONG_INVENTORY_ITEM.name });
      await expect(card).toBeVisible();
      await assertNoPageOverflow(page);
      await assertWithinViewport(card);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('respects role controls and keeps item overlays usable through validation, errors, and barcode generation', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 360, height: 800 });
    await openInventory(page, createInventoryScenario({ items: INVENTORY_ITEMS }));
    await expect(page.getByRole('button', { name: /Add New Product/i })).toBeVisible();
    await expect(page.getByRole('button', { name: /Print label/i }).first()).toBeVisible();
    await expect(page.getByRole('button', { name: 'Edit' }).first()).toBeVisible();
    await page.getByRole('button', { name: /Add New Product/i }).click();
    await expect(page.locator('.p-dialog:visible')).toBeVisible();
    await assertDialogFits(page);
    await page
      .getByRole('button', { name: /Add New Product/i })
      .last()
      .click();
    await expect(page.locator('.field:has(#add-product-name) .validation-message')).toBeVisible();

    const barcode = page.locator('app-inventory-barcode-field input');
    await barcode.fill('EXISTING-BARCODE');
    await page.getByRole('button', { name: /Generate/i }).click();
    await expect(page.locator('.barcode-replace-confirm')).toBeVisible();
    await page.getByRole('button', { name: /Keep current/i }).click();
    await expect(barcode).toHaveValue('EXISTING-BARCODE');
    await assertNoPageOverflow(page);

    await page.getByRole('button', { name: /Cancel/i }).click();
    await openInventory(
      page,
      createInventoryScenario({ items: INVENTORY_ITEMS, mutationState: 'error' }),
    );
    await page.getByRole('button', { name: /Add New Product/i }).click();
    await page.locator('#add-product-name').fill('Duplicate barcode product');
    await page.locator('#add-product-uom').fill('PCS');
    await page.locator('app-inventory-barcode-field input').fill('DUPLICATE-001');
    await page
      .getByRole('button', { name: /Add New Product/i })
      .last()
      .click();
    await expect(page.getByRole('alert')).toBeVisible();

    await openInventory(page, createInventoryScenario({ role: 'Staff', items: INVENTORY_ITEMS }));
    await expect(page.getByRole('button', { name: /Add New Product/i })).toHaveCount(0);
    await expect(page.getByRole('button', { name: /Edit|Print label/i })).toHaveCount(0);
  });

  test('opens edit overlay and keeps its barcode controls visible', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await openInventory(page, createInventoryScenario({ items: INVENTORY_ITEMS }));
    await page.getByRole('button', { name: 'Edit' }).first().click();
    await expect(page.locator('#edit-product-name')).toHaveValue('Fresh milk');
    await expect(page.locator('app-inventory-barcode-field input')).toBeVisible();
    await expect(page.getByRole('button', { name: /Generate/i })).toBeVisible();
    await assertDialogFits(page);
  });

  test('keeps every supported locale usable at mobile and desktop breakpoints', async ({
    page,
  }, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-mobile', 'locale matrix runs once');
    test.setTimeout(120_000);

    for (const locale of SUPPORTED_LANGUAGES) {
      for (const viewport of [
        { width: 360, height: 800 },
        { width: 1440, height: 900 },
      ]) {
        await page.setViewportSize(viewport);
        await openInventory(
          page,
          createInventoryScenario({ locale, items: [LONG_INVENTORY_ITEM] }),
        );
        await expect(page.locator('.inventory-page h1')).toBeVisible();
        await expect(
          viewport.width < 1024
            ? page.locator('.product-card-item')
            : page.locator('.desktop-table tbody tr'),
        ).toBeVisible();
        await assertNoPageOverflow(page);
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

function emptyStateFor(page: Page, projectName: string): Locator {
  return page.locator(projectName === 'chromium-mobile' ? '.mobile-empty-state' : '.empty-state');
}

async function assertHorizontalContainer(page: Page, selector: string): Promise<void> {
  await expect
    .poll(() =>
      page.locator(selector).evaluate((element) => element.scrollWidth > element.clientWidth),
    )
    .toBeTruthy();
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
  const dialog = page.locator('.p-dialog:visible');
  const box = await dialog.boundingBox();
  const viewport = page.viewportSize();
  expect(box).not.toBeNull();
  expect(viewport).not.toBeNull();
  expect(box!.height).toBeLessThanOrEqual(viewport!.height);
  await expect(dialog.locator('app-inventory-barcode-field')).toBeVisible();
}
