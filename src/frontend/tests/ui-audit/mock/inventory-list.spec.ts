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
const BROWSER_TYPES = [chromium, firefox, webkit] as const;

test.describe('inventory-list', () => {
  test('separates loading, empty, populated, and catalog API error states', async ({
    page,
  }, testInfo) => {
    const loading = createInventoryScenario({ apiState: 'loading' });
    await openInventory(page, loading);
    await expect(page.locator('.inventory-page')).toBeVisible();
    await expect(representationFor(page, testInfo.project.name)).toBeVisible();
    await expect(page.locator('app-inventory-filter-bar')).toBeHidden();

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
      await expect(page.locator('app-inventory-filter-bar')).toBeHidden();
      await expect(emptyStateFor(page, testInfo.project.name)).toHaveCount(0);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('filters, paginates, scans, and renders desktop/mobile dense content correctly', async ({
    page,
  }, testInfo) => {
    const collector = collectBrowserFailures(page);
    try {
      await page.setViewportSize({ width: 1440, height: 900 });
      await openInventory(page, createInventoryScenario({ items: INVENTORY_ITEMS }));

      await selectStatus(page, 'Active', 'active');
      await expect(rowsFor(page, testInfo.project.name)).toHaveCount(3);

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
      const densePageRequest = page.waitForRequest((request) => {
        if (request.method() !== 'GET') return false;
        const requestUrl = new URL(request.url());
        return requestUrl.pathname === '/api/items' && requestUrl.searchParams.get('pageNumber') === '2';
      });
      await page.locator('.desktop-table .p-paginator-next').click();
      await densePageRequest;
      await expect(page.locator('.desktop-table tbody')).toContainText('Dense inventory product 21');

      await selectStatus(page, 'Active', 'active');
      await expect(page.locator('.desktop-table tbody')).toContainText('Dense inventory product 1');
      await expect(page.locator('.desktop-table tbody')).not.toContainText('Dense inventory product 21');
      await assertDesktopActionsAndLongValues(page);

      await openInventory(page, createInventoryScenario({ items: INVENTORY_ITEMS }));
      await page.setViewportSize({ width: 360, height: 800 });
      await openInventory(page, createInventoryScenario({ items: INVENTORY_ITEMS }));
      await expect(page.locator('.mobile-grid-container')).toBeVisible();
      await expect(page.locator('.product-card-item')).toHaveCount(4);

      await selectStatus(page, 'Inactive', 'inactive');
      await expect(page.locator('.product-card-item')).toHaveCount(1);
      await expect(page.locator('.product-card-item')).toContainText('Inactive product');
      await selectStatus(page, 'Active', 'active');
      await expect(page.locator('.product-card-item')).toHaveCount(3);

      const card = page
        .locator('.product-card-item')
        .filter({ hasText: LONG_INVENTORY_ITEM.name })
        .first();
      await expect(card).toBeVisible();
      await assertNoPageOverflow(page);
      await assertWithinViewport(card, page);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('keeps add and edit overlays usable through validation, scanner, and mutation errors', async ({
    page,
  }, testInfo) => {
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

      const addDialog = await openAddDialog(page);
      await submitDialog(addDialog, /Add New Product/i);
      await expect(addDialog.locator('.field:has(#add-product-name) .validation-message')).toBeVisible();
      await expect(
        addDialog.locator('.field:has(app-inventory-barcode-field) .validation-message'),
      ).toBeVisible();
      const addScannerButton = barcodeScannerButton(addDialog);
      await expect(addScannerButton).toBeVisible();
      await addScannerButton.click();
      await expect(page.locator('.scanner-preview-shell')).toBeVisible();
      await expect(page.locator('.scanner-error')).toBeHidden();
      await assertDialogFits(page);
      await page.keyboard.press('Escape');
      await expect(page.locator('app-inventory-barcode-field input')).toHaveValue('SCANNED-AUDIT-001');
      await expect(page.locator('app-barcode-scanner-dialog .scanner-preview-shell')).toHaveCount(0);

      await fillAddDialog(page);
      await page.locator('app-inventory-barcode-field input').fill('EXISTING-BARCODE');
      await page.getByRole('button', { name: /Generate/i }).click();
      await expect(page.locator('.barcode-replace-confirm')).toBeVisible();
      await page.getByRole('button', { name: /Keep current/i }).click();
      await expect(page.locator('app-inventory-barcode-field input')).toHaveValue('EXISTING-BARCODE');
      await assertDialogFits(page);
      await page.locator('.dialog-actions button').first().click();

      await openInventory(
        page,
        createInventoryScenario({ items: INVENTORY_ITEMS, mutationState: 'error' }),
      );
      const failingAddDialog = await openAddDialog(page);
      await fillAddDialog(page);
      await submitDialog(failingAddDialog, /Add New Product/i);
      await expect(page.getByRole('alert')).toBeVisible();
      await expect(
        failingAddDialog.locator('.field:has(app-inventory-barcode-field) .validation-message'),
      ).toBeHidden();
      await expect(listingFor(page)).toBeVisible();
      await expect(page.locator('app-inventory-filter-bar')).toBeVisible();
      await expect(page.locator('.desktop-table tbody')).toContainText('Fresh milk');

      await page.locator('.dialog-actions button').first().click();
      await expect(page.locator('.p-dialog:visible')).toHaveCount(0);

      const closingAddDialog = await openAddDialog(page);
      await fillAddDialog(page);
      await submitDialog(closingAddDialog, /Add New Product/i);
      await page.keyboard.press('Escape');
      await expect(page.locator('app-barcode-scanner-dialog .scanner-preview-shell')).toHaveCount(0);
      await expect(page.locator('.inventory-page .error')).toHaveCount(0);

      await page.setViewportSize({ width: 1440, height: 900 });
      await page.reload();
      await waitForStablePage(page);
      const editDialog = await openEditDialog(page);
      await editDialog.locator('#edit-product-name').fill('');
      await editDialog.locator('app-inventory-barcode-field input').fill('');
      await submitDialog(editDialog, /Save/i);
      await expect(
        editDialog.locator('.field:has(#edit-product-name) .validation-message'),
      ).toBeVisible();
      await expect(
        editDialog.locator('.field:has(app-inventory-barcode-field) .validation-message'),
      ).toBeVisible();
      await expect(barcodeScannerButton(editDialog)).toBeVisible();

      await editDialog.locator('#edit-product-name').fill('Fresh milk retry');
      await editDialog.locator('app-inventory-barcode-field input').fill('DUPLICATE-001');
      await submitDialog(editDialog, /Save/i);
      await expect(page.getByRole('alert')).toBeVisible();
      await expect(page.locator('#edit-product-name')).toHaveValue('Fresh milk retry');
      await expect(page.locator('.inventory-page')).toBeVisible();
      await expect(page.locator('.dialog-actions button').first()).toBeEnabled();
      await expect(page.locator('.dialog-actions button').last()).toBeEnabled();
      await assertDialogFits(page);

      await page.keyboard.press('Escape');
      await expect(listingFor(page)).toBeVisible();

      await openInventory(
        page,
        createInventoryScenario({ items: INVENTORY_ITEMS, scannerState: 'detected' }),
      );
      const detectedEditDialog = await openEditDialog(page);
      await expect(barcodeScannerButton(detectedEditDialog)).toBeVisible();
      await barcodeScannerButton(detectedEditDialog).click();
      await expect(page.locator('.scanner-preview-shell')).toBeVisible();
      await page.keyboard.press('Escape');
      await expect(page.locator('app-inventory-barcode-field input')).toHaveValue('SCANNED-AUDIT-001');
      await expect(page.locator('.dialog-actions button').last()).toBeEnabled();
      await expect(page.locator('.dialog-actions button').first()).toBeEnabled();
      await page.keyboard.press('Escape');

      await openInventory(
        page,
        createInventoryScenario({ items: INVENTORY_ITEMS, scannerState: 'error' }),
      );
      const errorScannerAddDialog = await openAddDialog(page);
      await barcodeScannerButton(errorScannerAddDialog).click();
      const scannerError = page.locator('.scanner-error:visible');
      await expect(scannerError).toBeVisible();
      await expect(scannerError).toHaveText(/\S+/);
      await assertDialogFits(page);
      await page.keyboard.press('Escape');
      await expect(scannerError).toHaveCount(0);
      await errorScannerAddDialog.locator('.dialog-actions button').first().click();
      await expect(errorScannerAddDialog).toBeHidden();

      await openInventory(page, createInventoryScenario({ role: 'Staff', items: INVENTORY_ITEMS }));
      await expect(page.locator('.inventory-header .p-button')).toHaveCount(0);
      await expect(page.locator('.desktop-table .action-buttons button')).toHaveCount(0);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('keeps English and locale matrix paths usable with overlays', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-mobile', 'manual locale/browser matrix');
    test.setTimeout(720_000);

    for (const browserType of BROWSER_TYPES) {
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
  const loadRequest = scenario.apiState !== 'loading'
    ? page.waitForRequest((request) => {
      if (request.method() !== 'GET') return false;
      const requestUrl = new URL(request.url());
      return requestUrl.pathname === '/api/items';
    }, { timeout: 2_000 })
    : null;
  await page.goto(ROUTE);
  await waitForStablePage(page);
  if (loadRequest) {
    await loadRequest.catch(() => null);
  }
}

function rowsFor(page: Page, projectName: string): Locator {
  return page.locator(projectName === 'chromium-mobile' ? '.product-card-item' : '.desktop-table tbody tr');
}

function representationFor(page: Page, projectName: string): Locator {
  return page.locator(projectName === 'chromium-mobile' ? '.mobile-grid-container' : '.desktop-table');
}

function emptyStateFor(page: Page, projectName: string): Locator {
  return page.locator(projectName === 'chromium-mobile' ? '.mobile-empty-state' : '.empty-state');
}

async function selectStatus(page: Page, label: string, value: string): Promise<void> {
  const currentValue = await page.locator('[data-status-filter-value]').textContent();
  const normalizedCurrentValue = (currentValue ?? '').trim();
  const shouldAwaitRequest = normalizedCurrentValue !== value;
  if (!shouldAwaitRequest) {
    return;
  }
  const statusRequest = page.waitForRequest((request) => {
    if (request.method() !== 'GET') {
      return false;
    }
    const requestUrl = new URL(request.url());
    const status = requestUrl.searchParams.get('status');
    if (requestUrl.pathname !== '/api/items') {
      return false;
    }
    if (value === 'active') {
      return status === null || status === 'active';
    }
    return status === value;
  });
  await page.locator('app-inventory-filter-bar p-select').getByRole('combobox').click();
  const overlay = page.locator('.p-select-overlay:visible');
  await expect(overlay).toBeVisible();
  const legacyOption = overlay.locator('.p-select-option, .p-select-item');
  const exactVisibleLegacyOption = legacyOption
    .filter({
      visible: true,
      hasText: new RegExp(`^${escapeForRegexp(label)}$`, 'i'),
    })
    .first();
  if ((await exactVisibleLegacyOption.count()) === 1) {
    await exactVisibleLegacyOption.click();
  } else {
    await legacyOption.filter({ visible: true }).filter({ hasText: label }).first().click();
  }
  await expect(page.locator('[data-status-filter-value]')).toHaveText(value);
  const request = await statusRequest;
  const requestUrl = new URL(request.url());
  if (value === 'active') {
    expect([null, 'active']).toContain(requestUrl.searchParams.get('status'));
  } else {
    expect(requestUrl.searchParams.get('status')).toBe(value);
  }
  expect(requestUrl.searchParams.get('pageNumber')).toBe('1');
}

async function openAddDialog(page: Page): Promise<Locator> {
  await page.locator('.inventory-header .p-button').first().click();
  const dialog = page.locator('app-add-product-overlay .p-dialog:visible');
  await expect(dialog).toBeVisible();
  return dialog;
}

async function fillAddDialog(page: Page): Promise<void> {
  await page.locator('#add-product-name').fill('Duplicate barcode product');
  await page.locator('#add-product-uom').fill('PCS');
  await page.locator('app-inventory-barcode-field input').fill('DUPLICATE-001');
}

function barcodeScannerButton(scope: Locator): Locator {
  return scope.locator('.camera-button').first();
}

function listingFor(page: Page): Locator {
  const width = page.viewportSize()?.width ?? 1024;
  if (width < 1024) {
    return page.locator('.product-card-item').first();
  }
  return page.locator('.desktop-table');
}

async function openEditDialog(page: Page): Promise<Locator> {
  const width = page.viewportSize()?.width ?? 1024;
  if (width < 1024) {
    const firstCard = page.locator('.product-card-item').first();
    await expect(firstCard).toBeVisible();
    const editAction = firstCard.locator('.p-button:has(.pi-pencil)').first();
    await expect(editAction).toBeVisible();
    await editAction.click();
  } else {
    const firstEditAction = page
      .locator('.desktop-table .action-buttons .p-button:has(.pi-pencil)')
      .first();
    await expect(firstEditAction).toBeVisible();
    await firstEditAction.click();
  }
  const dialog = page.locator('app-edit-item-overlay .p-dialog:visible');
  await expect(dialog).toBeVisible();
  return dialog;
}

async function submitDialog(dialog: Locator, name: RegExp): Promise<void> {
  await expect(dialog).toBeVisible();
  const submitButton = dialog.locator('.dialog-actions button').filter({ hasText: name });
  await expect(submitButton).toHaveCount(1);
  await submitButton.click();
}

function escapeForRegexp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

async function assertDesktopActionsAndLongValues(page: Page): Promise<void> {
  const scrollContainer = page.locator(
    '.desktop-table .p-datatable-table-container, .desktop-table .p-datatable-wrapper',
  ).first();
  await expect(scrollContainer).toBeVisible();
  await expect
    .poll(async () => {
      const metrics = await scrollContainer.evaluate((node) => {
        return {
          scrollable: node.scrollWidth > node.clientWidth,
        };
      });
      return metrics.scrollable;
    })
    .toBeTruthy();
  await scrollContainer.evaluate((node) => node.scrollTo(node.scrollWidth, 0));
  await expect
    .poll(async () => {
      return scrollContainer.evaluate((node) => node.scrollLeft + node.clientWidth >= node.scrollWidth - 1);
    })
    .toBeTruthy();

  const longRow = page.locator('.desktop-table tbody tr').filter({ hasText: LONG_INVENTORY_ITEM.name }).first();
  const actionButtons = longRow.locator('.action-buttons');
  const actionButton = actionButtons.locator('.p-button').first();
  await expect(actionButton).toBeVisible();

  const placements = await actionButton.evaluate((node) => {
    const table = node.closest('.desktop-table');
    const tableRect = table?.getBoundingClientRect();
    const buttonRect = node.getBoundingClientRect();
    return {
      right: Math.round(buttonRect.right),
      tableRight: Math.round(tableRect?.right ?? 0),
      left: Math.round(buttonRect.left),
      tableLeft: Math.round(tableRect?.left ?? 0),
    };
  });
  expect(placements.right).toBeLessThanOrEqual(placements.tableRight + 1);
  expect(placements.left).toBeGreaterThanOrEqual(placements.tableLeft - 1);

  const longName = longRow.locator('td .truncate').first();
  const isTruncated = await longName.evaluate((node) => {
    const style = getComputedStyle(node);
    return node.scrollWidth > node.clientWidth || style.textOverflow === 'ellipsis';
  });
  expect(isTruncated).toBeTruthy();
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
    const collector = collectBrowserFailures(page, {
    ignoreConsole: (message) => message.includes('403'),
    ignoreResponse: (response) => response.url().startsWith(ITEMS_URL) && response.status() === 409,
  });
  try {
    await openInventory(
      page,
      createInventoryScenario({
        locale,
        items: viewport.width >= 1024 ? DENSE_INVENTORY_ITEMS : [LONG_INVENTORY_ITEM],
        scannerState: 'detected',
      }),
    );
    await expect(page.locator('.inventory-page h1')).toBeVisible();
    await expect(
      viewport.width < 1024
        ? page.locator('.product-card-item').first()
        : page.locator('.desktop-table tbody tr').first(),
    ).toBeVisible();
    await expect(page.locator('app-inventory-filter-bar')).toBeVisible();

    if (viewport.width >= 1024) {
      await selectStatus(page, 'Active', 'active');
      await page.locator('.desktop-table .p-paginator-next').click({ trial: true }).catch(() => null);
      await expect(
        page.locator('.desktop-table .p-paginator-page[aria-current="page"]'),
      ).toContainText('1');
    }

    const addDialog = await openAddDialog(page);
    await expect(addDialog.locator('.dialog-actions button')).toHaveCount(2);
    const addScannerButton = barcodeScannerButton(addDialog);
    await expect(addScannerButton).toBeVisible();
    await addDialog.locator('.dialog-actions button').first().click();
    await expect(addDialog).toBeHidden();

    const editDialog = await openEditDialog(page);
    await expect(barcodeScannerButton(editDialog)).toBeVisible();
    await page.keyboard.press('Escape');
    await assertDialogFits(page);
    await page.keyboard.press('Escape');
    await assertNoUnexpectedBrowserFailures(collector.failures);
  } finally {
    collector.dispose();
    await context.close();
    await browser.close();
  }
}

async function assertNoPageOverflow(page: Page): Promise<void> {
  await expect
    .poll(async () =>
      page.evaluate(() => {
        return Math.ceil(document.documentElement.scrollWidth) <= Math.ceil(window.innerWidth + 1);
      }),
    )
    .toBeTruthy();
}

async function assertWithinViewport(locator: Locator, page: Page): Promise<void> {
  const box = await locator.boundingBox();
  const viewport = page.viewportSize();
  expect(box).not.toBeNull();
  expect(viewport).not.toBeNull();
  expect(box!.x).toBeGreaterThanOrEqual(0);
  expect(box!.x + box!.width).toBeLessThanOrEqual(viewport!.width);
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
