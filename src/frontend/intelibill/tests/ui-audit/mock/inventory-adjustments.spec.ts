import { chromium, expect, firefox, test, webkit, type BrowserType, type Locator, type Page, type Route } from '@playwright/test';
import { SUPPORTED_LANGUAGES } from '../../../src/app/core/i18n/language.constants';
import type { InventoryAdjustmentHistoryItem, InventoryBatchDto } from '../../../src/app/features/inventory/services/inventory.models';
import { SHELL_ENGLISH_VIEWPORTS, SHELL_LOCALE_VIEWPORTS, createShellScenario, installShellFixture } from '../fixtures/shell.fixture';
import { assertNoUnexpectedBrowserFailures, collectBrowserFailures, type BrowserFailure, type FailureCollector, waitForStablePage } from '../support/audit-page';

const API_BASE = 'http://localhost:5277/api';
type RequestState = 'ready' | 'empty' | 'loading' | 'error' | 'pending';
interface PendingMutation { readonly route: Route; readonly body: Record<string, unknown> }
interface Scenario {
  history: RequestState; batches: RequestState; adjust: RequestState; void: RequestState;
  rows: InventoryAdjustmentHistoryItem[]; inventory: InventoryBatchDto[];
  pendingAdjust?: PendingMutation; pendingVoid?: PendingMutation;
  observedErrors: { readonly url: string; readonly status: number }[];
}
const collectors = new WeakMap<Page, FailureCollector>();
const scenarios = new WeakMap<Page, Scenario>();
test.describe('inventory-adjustments', () => {
  test.beforeEach(({ page }) => collectors.set(page, collectBrowserFailures(page)));
  test.afterEach(({ page }) => {
    const collector = collectors.get(page);
    if (!collector) return;
    try {
      assertNoUnexpectedBrowserFailures(filterDeclaredFailures(collector.failures, scenarios.get(page)));
    } finally { collector.dispose(); }
  });
  test('renders loading, empty, and independent API failure states', async ({ page }) => {
    const scenario = createScenario({ history: 'loading' });
    await openAdjustments(page, scenario);
    await expect(page.getByTestId('adjustment-summary')).toHaveAttribute('aria-busy', 'true');
    await expect(page.getByTestId('adjustment-ledger')).toHaveAttribute('aria-busy', 'true');

    scenario.history = 'empty';
    await visitAdjustments(page);
    await expect(page.locator('.empty-state:visible')).toContainText('No adjustments found');
    await expect(page.getByTestId('summary-total')).toHaveText('0');

    scenario.history = 'error';
    await visitAdjustments(page);
    await expect(page.locator('.p-toast-message:visible')).toContainText('Failed to load adjustment history');
    await expect(page.getByTestId('new-adjustment-action')).toBeEnabled();

    scenario.history = 'ready';
    scenario.batches = 'error';
    await visitAdjustments(page);
    await expect(page.locator('.p-toast-message:visible')).toContainText('Failed to load inventory batches');
    await expect(visibleRows(page)).toHaveCount(scenario.rows.length);
  });

  test('keeps dense adjustment results and summary within responsive bounds', async ({ page }) => {
    const scenario = createScenario();
    await openAdjustments(page, scenario);

    await expect(page.getByTestId('summary-total')).toHaveText('3');
    await expect(page.getByTestId('summary-increase')).toContainText('99,999,999.99');
    await expect(page.getByTestId('summary-decrease')).toContainText('87,654,321.98');
    await expect(page.getByTestId('summary-net')).toContainText('+12345678.01');
    await assertResponsiveLedger(page);
  });

  test('edits a row and keeps pickers and validation feedback bounded', async ({ page }) => {
    await openAdjustments(page, createScenario());
    await page.getByTestId('new-adjustment-action').click();

    const dialog = page.locator('.product-form-dialog');
    await expect(dialog).toBeVisible();
    await assertOverlayFits(dialog);

    await dialog.locator('.p-autocomplete-dropdown').click();
    const autocomplete = page.locator('.p-autocomplete-overlay:visible');
    await expect(autocomplete).toBeVisible();
    await assertOverlayFits(autocomplete);
    await autocomplete.getByText('BATCH-EMPTY-LONG', { exact: false }).click();

    await expect(dialog.getByTestId('adjustment-validation')).toBeVisible();
    await expect(dialog.getByTestId('submit-adjustment-action')).toBeDisabled();
    await dialog.locator('p-select').first().click();
    await assertOverlayFits(page.locator('.p-select-overlay:visible'));
    await page.keyboard.press('Escape');
    await dialog.locator('p-select').nth(1).click();
    await assertOverlayFits(page.locator('.p-select-overlay:visible'));
    await page.keyboard.press('Escape');
    await dialog.getByRole('combobox', { name: /performed at/i }).click();
    await assertOverlayFits(page.locator('.p-datepicker-panel:visible'));
    await page.keyboard.press('Escape');
    await assertNoDocumentOverflow(page);
  });

  test('renders saving, success, void, and retryable mutation errors', async ({ page }) => {
    const scenario = createScenario({ adjust: 'pending' });
    await openAdjustments(page, scenario);
    await submitAdjustment(page, scenario.inventory[0].itemName);

    const adjustmentDialog = page.locator('.product-form-dialog');
    await expect(adjustmentDialog.getByTestId('submit-adjustment-action')).toBeDisabled();
    await expect(adjustmentDialog.getByTestId('cancel-adjustment-action')).toBeDisabled();
    await completePendingAdjustment(scenario);
    await expect(adjustmentDialog).toBeHidden();
    await expect(toast(page, 'Inventory batch adjusted successfully')).toContainText('Inventory batch adjusted successfully');
    await expect(page.getByText(/ADJ-AUDIT-CREATED/).filter({ visible: true }).first()).toBeVisible();

    scenario.adjust = 'error';
    await submitAdjustment(page, scenario.inventory[0].itemName);
    await expect(adjustmentDialog).toBeVisible();
    await expect(toast(page, 'Adjustment rejected by deterministic fixture.')).toContainText('Adjustment rejected by deterministic fixture.');
    await expect(adjustmentDialog.getByTestId('submit-adjustment-action')).toBeEnabled();
    await adjustmentDialog.getByTestId('cancel-adjustment-action').click();

    scenario.void = 'pending';
    await openVoidDialog(page);
    const voidDialog = page.getByTestId('void-adjustment-dialog');
    await assertOverlayFits(page.locator('.void-adjustment-dialog'));
    await assertNoDocumentOverflow(page);
    await voidDialog.locator('textarea').fill('   ');
    await voidDialog.locator('textarea').blur();
    await expect(voidDialog.getByTestId('void-validation')).toBeVisible();
    await voidDialog.locator('textarea').fill('Duplicate audit adjustment');
    await voidDialog.getByTestId('confirm-void-action').click();
    await expect(voidDialog.getByTestId('confirm-void-action')).toBeDisabled();
    await expect(voidDialog.getByTestId('cancel-void-action')).toBeDisabled();
    await completePendingVoid(scenario);
    await expect(voidDialog).toBeHidden();
    await expect(toast(page, 'Adjustment voided successfully')).toContainText('Adjustment voided successfully');

    scenario.void = 'error';
    await openVoidDialog(page);
    await voidDialog.locator('textarea').fill('Retry audit void');
    await voidDialog.getByTestId('confirm-void-action').click();
    await expect(voidDialog).toBeVisible();
    await expect(toast(page, 'Void rejected by deterministic fixture.')).toContainText('Void rejected by deterministic fixture.');
    await expect(voidDialog.getByTestId('confirm-void-action')).toBeEnabled();
  });

  test('fits dense results and open dialogs across the approved rendering matrix', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-mobile', 'manual browser and locale matrix');
    test.setTimeout(240_000);
    await page.close();

    for (const browserType of [chromium, firefox, webkit]) {
      for (const viewport of SHELL_ENGLISH_VIEWPORTS) await withMatrixPage(browserType, viewport, 'en-IN');
    }
    for (const locale of SUPPORTED_LANGUAGES) {
      for (const viewport of SHELL_LOCALE_VIEWPORTS) await withMatrixPage(chromium, viewport, locale);
    }
  });
});

async function openAdjustments(page: Page, scenario: Scenario, locale = 'en-IN'): Promise<void> {
  scenarios.set(page, scenario);
  await installShellFixture(page, createShellScenario({ role: 'Owner', locale, longLabels: true }));
  await page.route(`${API_BASE}/inventory/**`, (route) => handleInventoryRoute(route, scenario));
  await visitAdjustments(page);
}
async function visitAdjustments(page: Page): Promise<void> {
  await page.goto('/inventory/adjustments');
  await waitForStablePage(page);
  await expect(page.locator('.adjustments-page')).toBeVisible();
}
async function handleInventoryRoute(route: Route, scenario: Scenario): Promise<void> {
  const request = route.request();
  const path = new URL(request.url()).pathname;
  if (request.method() === 'GET' && path === '/api/inventory/adjustments') {
    return respondToList(route, scenario, 'history');
  }
  if (request.method() === 'GET' && path === '/api/inventory/batches') {
    return respondToList(route, scenario, 'batches');
  }
  if (request.method() === 'POST' && /\/batches\/[^/]+\/adjust$/.test(path)) {
    return respondToMutation(route, scenario, 'adjust');
  }
  if (request.method() === 'POST' && /\/adjustments\/[^/]+\/void$/.test(path)) {
    return respondToMutation(route, scenario, 'void');
  }
  await route.abort('blockedbyclient');
}
async function respondToList(route: Route, scenario: Scenario, resource: 'history' | 'batches'): Promise<void> {
  const state = scenario[resource];
  if (state === 'loading') return new Promise<void>(() => undefined);
  if (state === 'error') {
    return declareError(route, scenario, 503, `${resource}.failed`);
  }
  if (resource === 'batches') return fulfillJson(route, scenario.inventory);
  const items = state === 'empty' ? [] : scenario.rows;
  await fulfillJson(route, { items, totalCount: items.length, pageNumber: 1, pageSize: 20 });
}
async function respondToMutation(route: Route, scenario: Scenario, mutation: 'adjust' | 'void'): Promise<void> {
  const body = (await route.request().postDataJSON()) as Record<string, unknown>;
  if (scenario[mutation] === 'pending') {
    scenario[mutation === 'adjust' ? 'pendingAdjust' : 'pendingVoid'] = { route, body };
    return;
  }
  if (scenario[mutation] === 'error') {
    const detail = `${mutation === 'adjust' ? 'Adjustment' : 'Void'} rejected by deterministic fixture.`;
    return declareError(route, scenario, mutation === 'adjust' ? 422 : 409, detail);
  }
  if (mutation === 'adjust') await fulfillAdjustment(route, scenario, body);
  else await fulfillVoid(route, scenario, body);
}
async function completePendingAdjustment(scenario: Scenario): Promise<void> {
  await expect.poll(() => scenario.pendingAdjust).toBeTruthy();
  const pending = scenario.pendingAdjust!;
  scenario.adjust = 'ready';
  await fulfillAdjustment(pending.route, scenario, pending.body);
}
async function completePendingVoid(scenario: Scenario): Promise<void> {
  await expect.poll(() => scenario.pendingVoid).toBeTruthy();
  const pending = scenario.pendingVoid!;
  scenario.void = 'ready';
  await fulfillVoid(pending.route, scenario, pending.body);
}

async function fulfillAdjustment(route: Route, scenario: Scenario, body: Record<string, unknown>): Promise<void> {
  const batch = scenario.inventory.find((item) => route.request().url().includes(item.id))!;
  const quantity = Number(body['quantity']);
  const created = adjustment('created', 'Increase', quantity, false, batch);
  scenario.rows.unshift(created);
  await fulfillJson(route, {
    adjustmentId: created.adjustmentId, adjustmentNumber: created.adjustmentNumber,
    quantity, unitCost: batch.costPrice, costImpact: quantity * batch.costPrice,
    batchQuantityBefore: batch.quantity, batchQuantityAfter: batch.quantity + quantity,
    inventoryQuantityBefore: batch.quantity, inventoryQuantityAfter: batch.quantity + quantity,
    stockTransactionId: 'tx-created', performedAt: created.performedAt,
  });
}

async function fulfillVoid(route: Route, scenario: Scenario, body: Record<string, unknown>): Promise<void> {
  const row = scenario.rows.find((item) => route.request().url().includes(item.adjustmentId))!;
  Object.assign(row, {
    isVoided: true, voidedAt: '2026-07-29T12:00:00.000Z',
    voidedByUserId: 'user-owner', voidedByDisplayName: 'UI Audit Owner',
    voidReason: body['reason'], reversalStockTransactionId: 'tx-reversal-audit',
  });
  await fulfillJson(route, {
    adjustmentId: row.adjustmentId, reversalStockTransactionId: row.reversalStockTransactionId,
    batchQuantityBefore: row.batchQuantityAfter, batchQuantityAfter: row.batchQuantityBefore,
    inventoryQuantityBefore: row.inventoryQuantityAfter, inventoryQuantityAfter: row.inventoryQuantityBefore,
    voidedAt: row.voidedAt,
  });
}

async function submitAdjustment(page: Page, itemName: string): Promise<void> {
  await page.getByTestId('new-adjustment-action').click();
  const dialog = page.locator('.product-form-dialog');
  await dialog.locator('.p-autocomplete-dropdown').click();
  await page.locator('.p-autocomplete-overlay:visible').getByText(itemName, { exact: true }).click();
  await dialog.locator('p-select').first().click();
  await page.getByRole('option', { name: /increase/i }).click();
  await dialog.getByTestId('submit-adjustment-action').click();
}

async function openVoidDialog(page: Page): Promise<void> {
  await page.getByTestId('void-adjustment-action').filter({ visible: true }).first().click();
  await expect(page.getByTestId('void-adjustment-dialog')).toBeVisible();
}

function visibleRows(page: Page) {
  return page.locator('.desktop-table tbody tr:visible, .adjustment-card-item:visible');
}

function toast(page: Page, text: string) {
  return page.locator('.p-toast-message:visible').filter({ hasText: text });
}

async function withMatrixPage(
  browserType: BrowserType, viewport: { readonly width: number; readonly height: number },
  locale: (typeof SUPPORTED_LANGUAGES)[number],
): Promise<void> {
  const browser = await browserType.launch();
  const context = await browser.newContext({
    viewport, locale, timezoneId: 'Asia/Kolkata', reducedMotion: 'reduce',
    serviceWorkers: 'block', baseURL: 'http://127.0.0.1:4300',
  });
  const page = await context.newPage();
  const collector = collectBrowserFailures(page);
  const scenario = createScenario();
  try {
    await openAdjustments(page, scenario, locale);
    await assertResponsiveLedger(page);
    await page.getByTestId('new-adjustment-action').click();
    await assertOverlayFits(page.locator('.product-form-dialog'));
  } finally {
    assertNoUnexpectedBrowserFailures(filterDeclaredFailures(collector.failures, scenario));
    collector.dispose();
    await context.close();
    await browser.close();
  }
}

async function assertResponsiveLedger(page: Page): Promise<void> {
  await assertNoDocumentOverflow(page);
  const desktop = (await page.viewportSize())!.width >= 1024;
  await expect(page.locator(desktop ? '.desktop-table' : '.mobile-grid-container')).toBeVisible();
  if (!desktop) {
    await expect(page.locator('.adjustment-card-item')).toHaveCount(3);
    return;
  }
  const scroll = page.locator('.desktop-table .p-datatable-table-container');
  await expect.poll(() => scroll.evaluate((node) => node.scrollWidth > node.clientWidth)).toBe(true);
  await scroll.evaluate((node) => node.scrollTo({ left: node.scrollWidth }));
  await expect(page.getByTestId('void-adjustment-action').filter({ visible: true }).first()).toBeVisible();
}

async function assertNoDocumentOverflow(page: Page): Promise<void> {
  await expect
    .poll(() => page.evaluate(() => document.documentElement.scrollWidth))
    .toBeLessThanOrEqual(await page.evaluate(() => innerWidth));
}

async function assertOverlayFits(overlay: Locator): Promise<void> {
  const bounds = await overlay.evaluate((node) => {
    const box = node.getBoundingClientRect();
    return {
      left: box.left, right: box.right, top: box.top, bottom: box.bottom,
      viewportWidth: innerWidth, viewportHeight: innerHeight,
    };
  });
  expect(bounds.left).toBeGreaterThanOrEqual(0);
  expect(bounds.right).toBeLessThanOrEqual(bounds.viewportWidth);
  expect(bounds.top).toBeGreaterThanOrEqual(0);
  expect(bounds.bottom).toBeLessThanOrEqual(bounds.viewportHeight);
}

async function declareError(route: Route, scenario: Scenario, status: number, detail: string): Promise<void> {
  scenario.observedErrors.push({ url: route.request().url(), status });
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify({ title: detail, detail }) });
}

async function fulfillJson(route: Route, body: unknown): Promise<void> {
  await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
}

function filterDeclaredFailures(failures: readonly BrowserFailure[], scenario?: Scenario): BrowserFailure[] {
  if (!scenario) return [...failures];
  return failures.filter((failure) => {
    if (failure.kind === 'request' && failure.message === 'net::ERR_ABORTED' &&
      failure.url === `${API_BASE}/inventory/adjustments`) return false;
    if (
      failure.kind === 'console' &&
      scenario.observedErrors.some((error) => failure.message.includes(`status of ${error.status}`))
    ) {
      return false;
    }
    return !scenario.observedErrors.some((error) =>
      failure.kind === 'response' && failure.url === error.url &&
      failure.message === `HTTP ${error.status}`);
  });
}

function createScenario(overrides: Partial<Scenario> = {}): Scenario {
  return {
    history: 'ready', batches: 'ready', adjust: 'ready', void: 'ready',
    rows: [
      adjustment('1', 'Increase', 99_999_999.99, false, batch('history-long', 250)),
      adjustment('2', 'Decrease', 87_654_321.98, true),
      adjustment('3', 'Decrease', 0),
    ],
    inventory: [batch('available', 250), batch('empty-long', 0)],
    observedErrors: [],
    ...overrides,
  };
}

function adjustment(
  id: string, direction: 'Increase' | 'Decrease', quantity: number,
  voided = false, sourceBatch = batch(id, 250),
): InventoryAdjustmentHistoryItem {
  return {
    adjustmentId: `adjustment-${id}`, adjustmentNumber: id === 'created' ? 'ADJ-AUDIT-CREATED' : `ADJ-AUDIT-${id}`,
    itemId: sourceBatch.itemId, itemName: sourceBatch.itemName, barcode: sourceBatch.barcode,
    batchId: sourceBatch.id, batchNumber: sourceBatch.batchNumber, direction,
    reason: direction === 'Increase' ? 'FoundStock' : 'Damaged', quantity, unitCost: sourceBatch.costPrice,
    costImpact: direction === 'Increase' ? quantity * 12.34 : quantity * -12.34,
    batchQuantityBefore: 123_456_789.99, batchQuantityAfter: 123_456_790,
    inventoryQuantityBefore: 987_654_321.99, inventoryQuantityAfter: 987_654_322,
    performedAt: '2026-07-29T09:30:00.000Z', performedByUserId: 'user-owner',
    performedByDisplayName: 'Alexandria Cassandra Extremely Long Audit Owner Name',
    notes: 'Long deterministic adjustment note '.repeat(4),
    isVoided: voided, voidedAt: voided ? '2026-07-29T10:30:00.000Z' : null,
    voidedByUserId: voided ? 'user-owner' : null,
    voidedByDisplayName: voided ? 'Alexandria Cassandra Extremely Long Audit Owner Name' : null,
    voidReason: voided ? 'Long deterministic void explanation '.repeat(5) : null,
    reversalStockTransactionId: voided ? `reversal-${'9'.repeat(48)}` : null,
  };
}

function batch(id: string, quantity: number): InventoryBatchDto {
  const long = id.includes('long');
  return {
    id: `batch-${id}`, shopId: 'shop-primary', itemId: `item-${id}`,
    itemName: long
      ? 'Exceptionally Long International Inventory Item Name That Must Wrap Within Every Picker'
      : `Audit Inventory Item ${id}`,
    barcode: `AUDIT-${id}-${'8'.repeat(long ? 48 : 8)}`,
    batchNumber: long ? `BATCH-EMPTY-LONG-${'7'.repeat(36)}` : `BATCH-${id.toUpperCase()}`,
    quantity, originalQuantity: quantity, costPrice: 12.34, mrp: 18, salesPrice: 16,
    taxRatePercent: 5, taxIncluded: true, expiryDate: null, manufacturingDate: null,
    supplierId: null, supplierName: null, isVoided: false,
    createdAt: '2026-07-29T00:00:00.000Z', updatedAt: null,
  };
}
