import { expect, test, type Browser, type Locator, type Page, type Route, type TestInfo } from '@playwright/test';
import { SUPPORTED_LANGUAGES } from '../../../src/app/core/i18n/language.constants';
import type { DiscountRuleDto } from '../../../src/app/features/discounts/services/discount.service';
import {
  createShellScenario,
  filterDeclaredShellFailures,
  installShellFixture,
  type ShellScenario,
} from '../fixtures/shell.fixture';
import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  waitForStablePage,
} from '../support/audit-page';

const API = 'http://localhost:5277/api';
const LONG = `International seasonal promotion ${'with-extra-long-content-'.repeat(7)}`;
interface State {
  rules: DiscountRuleDto[];
  queries: URLSearchParams[];
  previewCalls: number;
  batchCalls: number;
  listDelay: number;
  listStatus: number;
  failPreview: number;
  failCreate: number;
  failReplace: number;
  failDisable: number;
  failBatch: number;
}

test.describe('discounts', () => {
  test('guards management access and renders list response states', async ({ browser }, testInfo) => {
    for (const role of ['Owner', 'Manager', 'Staff'] as const) {
      const page = await isolatedPage(browser, testInfo);
      await open(page, createShellScenario({ role }), state());
      if (role === 'Staff') await expect(page).toHaveURL(/\/(dashboard|sales)$/);
      else await expect(page.locator('[data-testid="discounts-row-rule-active"]')).toBeVisible();
      await page.context().close();
    }
    const loading = await isolatedPage(browser, testInfo);
    await install(loading, createShellScenario(), state({ listDelay: 400 }));
    await loading.goto('/discounts');
    await expect(loading.locator('[aria-busy="true"]')).toBeVisible();
    await expect(loading.locator('[data-testid="discounts-table"]')).toBeVisible();
    await loading.context().close();

    const empty = await isolatedPage(browser, testInfo);
    await open(empty, createShellScenario(), state({ rules: [] }));
    await expect(empty.locator('[data-testid="discounts-empty"]')).toBeVisible();
    await empty.context().close();

    const failed = await isolatedPage(browser, testInfo);
    await open(failed, createShellScenario(), state({ listStatus: 503 }));
    await expect(failed.locator('.error')).toContainText('Could not load discount rules');
    await failed.context().close();
  });

  test('filters, pages, and selects discount rules without stale detail', async ({ page }) => {
    const rules = [
      ...baseRules(),
      ...Array.from({ length: 22 }, (_, index) =>
        rule({ id: `paged-${index}`, name: `Paged promotion ${String(index).padStart(2, '0')}` })),
    ];
    const scenario = createShellScenario();
    const data = state({ rules });
    const clean = auditFailures(page, scenario);
    await open(page, scenario, data);
    await select(page, 'discounts-status-filter', 'All');
    await expect(page.getByText('Expired', { exact: true })).toBeVisible();
    await expect(page.getByText('Disabled', { exact: true })).toBeVisible();
    await page.locator('[data-testid="discounts-row-rule-expired"]').click();
    await expect(page.locator('.detail-panel h3')).toHaveText('Expired threshold');
    await page.locator('[data-testid="discounts-search"]').fill('Active batch');
    await expect(page.locator('.detail-panel h3')).toHaveText('Active batch');
    await expect(page.locator('.detail-panel')).not.toContainText('Expired threshold');
    await page.locator('[data-testid="discounts-search"]').fill('');
    await select(page, 'discounts-status-filter', 'Active');
    await select(page, 'discounts-type-filter', 'Batch Discount');
    await select(page, 'discounts-type-filter', 'All types');
    await page.getByRole('button', { name: 'Next page' }).click();
    await expect(page.locator('.page-info')).toContainText('Page 2');
    expect(data.queries.some((query) => query.get('page') === '2')).toBe(true);
    expect(data.queries.some((query) => query.get('ruleType') === 'BatchPercentage')).toBe(true);
    clean();
  });

  test('validates dynamic rule conditions and target batch search', async ({ page }) => {
    const scenario = createShellScenario();
    const data = state({ failBatch: 1 });
    const clean = auditFailures(page, scenario, [503]);
    await open(page, scenario, data);
    await page.locator('[data-testid="discounts-create-rule"]').click();
    const dialog = editor(page);
    await dialog.getByRole('button', { name: 'Create', exact: true }).click();
    await expect(dialog.locator('#discount-rule-name')).toHaveAttribute('aria-invalid', 'true');
    expect(data.previewCalls).toBe(0);
    await chooseType(dialog, page, 'Threshold discount');
    await expect(dialog.locator('#discount-rule-threshold')).toBeVisible();
    await expect(dialog.locator('#discount-rule-batch-search')).toHaveCount(0);
    await dialog.locator('#discount-rule-name').fill('Threshold audit');
    await dialog.getByRole('button', { name: 'Preview', exact: true }).click();
    expect(data.previewCalls).toBe(0);
    await dialog.locator('#discount-rule-threshold').fill('500');
    await dialog.getByRole('button', { name: 'Preview', exact: true }).click();
    await expect(dialog.getByText('Affected sample')).toBeVisible();
    await chooseType(dialog, page, 'Batch discount');
    const search = dialog.locator('#discount-rule-batch-search');
    await search.fill('ri');
    await page.waitForTimeout(350);
    expect(data.batchCalls).toBe(0);
    await search.fill('rice');
    await expect(dialog.getByRole('alert')).toContainText('Could not load batch search results');
    await search.fill('rice flour');
    await expect(dialog.locator('.batch-result')).toHaveCount(2);
    clean();
  });

  test('previews, creates, and replaces rules with retryable feedback', async ({ page }) => {
    const scenario = createShellScenario();
    const data = state({ failPreview: 1, failCreate: 1, failReplace: 1 });
    const clean = auditFailures(page, scenario, [503]);
    await open(page, scenario, data);
    await page.locator('[data-testid="discounts-create-rule"]').click();
    let dialog = editor(page);
    await fillBatch(dialog, 'Created audit rule');
    await dialog.getByRole('button', { name: 'Preview', exact: true }).click();
    await expect(dialog.getByRole('alert')).toContainText('Could not load discount preview');
    await dialog.getByRole('button', { name: 'Preview', exact: true }).click();
    await expect(dialog.getByText('Affected sample')).toBeVisible();
    await dialog.locator('#discount-rule-name').fill('Created audit rule v2');
    await expect(dialog.getByText('Affected sample')).toHaveCount(0);
    await dialog.getByRole('button', { name: 'Create', exact: true }).click();
    await expect(dialog.getByRole('alert')).toContainText('Could not create discount rule');
    await dialog.getByRole('button', { name: 'Create', exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(page.locator('.detail-panel h3')).toHaveText('Created audit rule v2');
    await page.locator('[data-testid="discounts-edit-rule"]').click();
    dialog = editor(page);
    await dialog.locator('#discount-rule-name').fill('Replacement audit rule');
    await dialog.getByRole('button', { name: 'Save replacement' }).click();
    await expect(dialog.getByRole('alert')).toContainText('Could not replace discount rule');
    await dialog.getByRole('button', { name: 'Save replacement' }).click();
    await expect(dialog).toBeHidden();
    await expect(page.locator('.detail-panel h3')).toHaveText('Replacement audit rule');
    clean();
  });

  test('confirms, retries, and renders a disabled rule', async ({ page }) => {
    const scenario = createShellScenario();
    const data = state({ failDisable: 1 });
    const clean = auditFailures(page, scenario, [503]);
    await open(page, scenario, data);
    await page.locator('[data-testid="discounts-disable"]').click();
    await disableDialog(page).getByRole('button', { name: 'Cancel' }).click();
    await page.locator('[data-testid="discounts-disable"]').click();
    const dialog = disableDialog(page);
    await dialog.locator('[data-testid="discounts-disable-reason"]').fill('Campaign complete');
    await dialog.locator('[data-testid="discounts-disable-confirm"]').click();
    await expect(dialog.getByRole('alert')).toContainText('Could not disable rule');
    await dialog.locator('[data-testid="discounts-disable-confirm"]').click();
    await expect(dialog).toBeHidden();
    await expect(page.locator('.detail-panel')).toContainText('Disabled');
    await expect(page.locator('[data-testid="discounts-edit-rule"]')).toHaveCount(0);
    await expect(page.locator('[data-testid="discounts-disable"]')).toHaveCount(0);
    clean();
  });

  test('fits localized long states and open dialogs across the audit matrix', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-mobile', 'manual locale and viewport matrix');
    test.setTimeout(180_000);
    await install(page, createShellScenario({ longLabels: true }),
      state({ rules: [rule({ name: LONG, description: LONG })] }));
    for (const locale of SUPPORTED_LANGUAGES) {
      await page.addInitScript((value) =>
        localStorage.setItem('inventory.preferences.language', value), locale);
      for (const viewport of [{ width: 360, height: 640 }, { width: 1440, height: 900 }]) {
        await page.setViewportSize(viewport);
        await page.goto('/discounts');
        await waitForStablePage(page);
        await assertNoOverflow(page);
        await expect(page.locator('body')).not.toContainText(/discounts\.[A-Za-z]/);
        await page.locator('[data-testid="discounts-create-rule"]').click();
        await assertDialogFits(editor(page));
        await editor(page).locator('.p-dialog-footer button').first().click();
        await page.locator('[data-testid="discounts-edit-rule"]').click();
        await assertDialogFits(editor(page));
        await editor(page).locator('.p-dialog-header button').click();
        await page.locator('[data-testid="discounts-disable"]').click();
        await assertDialogFits(disableDialog(page));
        await disableDialog(page).locator('.p-dialog-header button').click();
      }
    }
  });
});

function state(overrides: Partial<State> = {}): State {
  return {
    rules: baseRules(), queries: [], previewCalls: 0, batchCalls: 0, listDelay: 0,
    listStatus: 200, failPreview: 0, failCreate: 0, failReplace: 0, failDisable: 0,
    failBatch: 0, ...overrides,
  };
}

function baseRules(): DiscountRuleDto[] {
  return [
    rule(),
    rule({ id: 'rule-expired', name: 'Expired threshold', ruleType: 'SaleThresholdPercentage',
      inventoryBatchId: null, thresholdAmount: 500, endsAt: '2020-01-01T00:00:00Z' }),
    rule({ id: 'rule-disabled', name: 'Disabled sale', ruleType: 'SalePercentage',
      inventoryBatchId: null, isActive: false }),
  ];
}

function rule(overrides: Partial<DiscountRuleDto> = {}): DiscountRuleDto {
  return {
    id: 'rule-active', ruleType: 'BatchPercentage', name: 'Active batch', description: null,
    inventoryBatchId: 'batch-1', percentage: 10, thresholdAmount: null, startsAt: null,
    endsAt: null, isActive: true, disabledAt: null, disabledReason: null,
    belowCostConfirmed: false, belowCostConfirmationReason: null, replacesRuleId: null,
    replacedByRuleId: null, createdAt: '2026-01-01T00:00:00Z', updatedAt: null, ...overrides,
  };
}

async function open(page: Page, scenario: ShellScenario, data: State): Promise<void> {
  await install(page, scenario, data);
  await page.goto('/discounts');
  await waitForStablePage(page);
}

async function install(page: Page, scenario: ShellScenario, data: State): Promise<void> {
  await installShellFixture(page, scenario);
  await page.route(`${API}/inventory/batches/available**`, (route) => batches(route, data));
  await page.route(`${API}/discounts**`, (route) => discounts(route, data));
}

async function discounts(route: Route, data: State): Promise<void> {
  const request = route.request();
  const url = new URL(request.url());
  const path = url.pathname;
  const method = request.method();
  if (path === '/api/discounts' && method === 'GET') return list(route, data, url);
  if (path === '/api/discounts/preview') {
    data.previewCalls += 1;
    if (data.failPreview-- > 0) return json(route, {}, 503);
    return json(route, preview());
  }
  if (path === '/api/discounts' && method === 'POST') {
    if (data.failCreate-- > 0) return json(route, {}, 503);
    const created = rule({ ...request.postDataJSON(), id: 'rule-created' });
    data.rules.unshift(created);
    return json(route, created);
  }
  const id = path.split('/')[3] ?? '';
  if (path.endsWith('/disable')) return disable(route, data, id);
  if (method === 'PUT') return replace(route, data, id);
  if (method === 'GET') return json(route, data.rules.find((item) => item.id === id) ?? {});
  return route.fallback();
}

async function list(route: Route, data: State, url: URL): Promise<void> {
  data.queries.push(new URLSearchParams(url.searchParams));
  if (data.listDelay) await new Promise((resolve) => setTimeout(resolve, data.listDelay));
  if (data.listStatus !== 200) return json(route, {}, data.listStatus);
  const params = url.searchParams;
  const status = params.get('status');
  const type = params.get('ruleType');
  const search = params.get('search')?.toLowerCase();
  const page = Number(params.get('page') ?? 1);
  const pageSize = Number(params.get('pageSize') ?? 20);
  let rules = data.rules.filter((item) => statusMatch(item, status));
  if (type) rules = rules.filter((item) => item.ruleType === type);
  if (search) rules = rules.filter((item) => item.name.toLowerCase().includes(search));
  const items = rules.slice((page - 1) * pageSize, page * pageSize).map((item) => ({
    id: item.id, ruleType: item.ruleType, name: item.name, isActive: item.isActive,
    startsAt: item.startsAt, endsAt: item.endsAt, createdAt: item.createdAt,
  }));
  return json(route, { items, totalCount: rules.length, pageNumber: page, pageSize });
}

function statusMatch(item: DiscountRuleDto, status: string | null): boolean {
  const expired = Boolean(item.endsAt && Date.parse(item.endsAt) < Date.now());
  if (status === 'active') return item.isActive && !expired;
  if (status === 'disabled') return !item.isActive;
  if (status === 'expired') return item.isActive && expired;
  return true;
}

async function replace(route: Route, data: State, id: string): Promise<void> {
  if (data.failReplace-- > 0) return json(route, {}, 503);
  const replacement = rule({ ...route.request().postDataJSON(), id: `${id}-replacement`, replacesRuleId: id });
  data.rules = data.rules.map((item) =>
    item.id === id ? { ...item, isActive: false, replacedByRuleId: replacement.id } : item);
  data.rules.unshift(replacement);
  return json(route, replacement);
}

async function disable(route: Route, data: State, id: string): Promise<void> {
  if (data.failDisable-- > 0) return json(route, {}, 503);
  const current = data.rules.find((item) => item.id === id) ?? rule({ id });
  const reason = (route.request().postDataJSON() as { reason: string | null }).reason;
  const disabled = { ...current, isActive: false, disabledAt: '2026-07-29T00:00:00Z', disabledReason: reason };
  data.rules = data.rules.map((item) => item.id === id ? disabled : item);
  return json(route, disabled);
}

async function batches(route: Route, data: State): Promise<void> {
  data.batchCalls += 1;
  if (data.failBatch-- > 0) return json(route, {}, 503);
  const makeBatch = (id: string, name: string) => ({
    inventoryBatchId: id, itemName: name, batchNumber: id, barcode: id, quantity: 20,
    salesPrice: 100, mrp: 120, taxRatePercent: 5, taxIncluded: false, expiryDate: '2027-01-01',
  });
  return json(route, [makeBatch('batch-1', 'Rice Flour'), makeBatch('batch-2', LONG)]);
}

function preview(): Record<string, unknown> {
  const sample = { batchId: 'batch-1', itemName: LONG, batchNumber: LONG,
    salesPrice: 100, costPrice: 80, discountedPrice: 90 };
  return { affectedCount: 1, affectedSample: [sample], belowCostSample: [],
    safeMaxPercentage: 20, errors: [], infos: [] };
}

async function fillBatch(dialog: Locator, name: string): Promise<void> {
  await dialog.locator('#discount-rule-name').fill(name);
  await dialog.locator('#discount-rule-batch-search').fill('rice');
  await expect(dialog.locator('.batch-result')).toHaveCount(2);
  await dialog.locator('.batch-result').first().click();
}

async function chooseType(dialog: Locator, page: Page, name: string): Promise<void> {
  await dialog.locator('p-select[formcontrolname="ruleType"]').click();
  await page.getByRole('option', { name, exact: true }).click();
}

async function select(page: Page, inputId: string, name: string): Promise<void> {
  await page.locator(`p-select[inputid="${inputId}"]`).click();
  await page.getByRole('option', { name, exact: true }).click();
}

const editor = (page: Page): Locator => page.locator('.discount-rule-editor-dialog');
const disableDialog = (page: Page): Locator => page.locator('.discounts-disable-dialog');

function auditFailures(page: Page, scenario: ShellScenario, statuses: readonly number[] = []): () => void {
  const collector = collectBrowserFailures(page, {
    ignoreConsole: (message) => message.includes('Failed to load resource') &&
      statuses.some((status) => message.includes(`status of ${status}`)),
    ignoreResponse: (response) => statuses.includes(response.status()) &&
      (response.url().includes('/discounts') || response.url().includes('/batches/available')),
  });
  return () => {
    assertNoUnexpectedBrowserFailures(filterDeclaredShellFailures(collector.failures, scenario));
    collector.dispose();
  };
}

async function isolatedPage(browser: Browser, testInfo: TestInfo): Promise<Page> {
  const context = await browser.newContext({
    baseURL: testInfo.project.use.baseURL as string, viewport: testInfo.project.use.viewport,
  });
  return context.newPage();
}

async function assertNoOverflow(page: Page): Promise<void> {
  const widths = await page.evaluate(() => ({ document: document.documentElement.scrollWidth, viewport: innerWidth }));
  expect(widths.document).toBeLessThanOrEqual(widths.viewport + 1);
}

async function assertDialogFits(dialog: Locator): Promise<void> {
  await expect(dialog).toBeVisible();
  await expect(dialog).toHaveAccessibleName(/\S/);
  expect(await dialog.evaluate((element) => {
    const box = element.getBoundingClientRect();
    return box.left >= 0 && box.top >= 0 && box.right <= innerWidth && box.bottom <= innerHeight;
  })).toBe(true);
}

function json(route: Route, body: unknown, status = 200): Promise<void> {
  return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}
