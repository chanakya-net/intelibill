import { expect, test } from '@playwright/test';

import {
  cleanupTrackedRecords,
  createRecordTracker,
  createRunPrefix,
  installLiveApiGuard,
  loginViaApi,
  loadLiveEnv,
  pingApi,
  requireLiveConfig,
  seedSession,
} from '../support/live-api.fixture';
import { waitForStablePage } from '../support/audit-page';
import { writeRedactedArtifacts } from '../support/artifact-redaction';

const liveConfig = requireLiveConfig(loadLiveEnv());
const runPrefix = createRunPrefix();
const tracker = createRecordTracker(runPrefix);
let mutationAccessToken = '';

test.describe('live local API smoke', () => {
  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const report = await cleanupTrackedRecords(request, liveConfig, mutationAccessToken, tracker);
    await writeRedactedArtifacts({
      outputDir: '.ui-audit/reports/live',
      name: runPrefix,
      html: cleanupReportHtml(report),
      result: report,
    });
  });

  test('preflight and login', async ({ page, request }) => {
    await pingApi(request, liveConfig);
    await installLiveApiGuard(page, liveConfig);
    await page.goto('/login');
    await waitForStablePage(page);
    await page.locator('#identifier').fill(liveConfig.username);
    await page.locator('#password').fill(liveConfig.password);
    let loginStatus: number | null = null;
    let loginFailure: string | null = null;
    const requestPaths: string[] = [];
    page.on('request', (request) => {
      requestPaths.push(`${request.method()} ${new URL(request.url()).pathname}`);
    });
    page.on('response', (response) => {
      const url = new URL(response.url());
      if (response.request().method() === 'POST' && url.pathname === '/api/auth/login') {
        loginStatus = response.status();
      }
    });
    page.on('requestfailed', (request) => {
      if (new URL(request.url()).pathname === '/api/auth/login') {
        loginFailure = request.failure()?.errorText ?? 'request failed';
      }
    });
    expect(await hasInputValue(page.locator('#identifier'))).toBe(true);
    expect(await hasInputValue(page.locator('#password'))).toBe(true);
    await expect(page.locator('button[type="submit"]')).toBeEnabled();
    await page.locator('button[type="submit"]').click();

    await expect.poll(() => requestPaths).toContain('POST /api/auth/login');
    await expect
      .poll(() => ({ loginStatus, loginFailure }))
      .toEqual({ loginStatus: 200, loginFailure: null });
    await expect(page.locator('.app-shell')).toBeVisible();
    await expect(page).not.toHaveURL(/\/login(?:[/?#]|$)/);
  });

  test('shell renders and navigates', async ({ page, request }) => {
    const session = await loginViaApi(request, liveConfig);
    await installLiveApiGuard(page, liveConfig);
    await seedSession(page, session);

    for (const [route, selector] of [
      ['/dashboard', '.dashboard-page'],
      ['/inventory', '.inventory-page'],
      ['/sales', '.sales-ledger-page'],
    ] as const) {
      await page.goto(route);
      await waitForStablePage(page);
      await expect(page.locator('.app-shell')).toBeVisible();
      await expect(page.locator(selector)).toBeVisible();
    }
  });

  test('inventory create and edit', async ({ page, request }) => {
    const session = await loginViaApi(request, liveConfig);
    mutationAccessToken = session.accessToken;
    await installLiveApiGuard(page, liveConfig);
    await seedSession(page, session);
    await page.goto('/inventory');
    await waitForStablePage(page);

    const itemName = `${runPrefix}-item`;
    const editedName = `${itemName}-edited`;
    const addDialog = await openAddDialog(page);
    await addDialog.locator('#add-product-name').fill(itemName);
    await addDialog.locator('#add-product-uom').fill('PCS');
    await addDialog.locator('app-inventory-barcode-field input').fill(`${runPrefix}-sku`);

    const createResponse = page.waitForResponse(
      (response) =>
        response.request().method() === 'POST' && new URL(response.url()).pathname === '/api/items',
    );
    await addDialog
      .locator('.dialog-actions button')
      .filter({ hasText: /Add New Product/i })
      .click();
    const createdResponse = await createResponse;
    expect(createdResponse.ok()).toBe(true);
    const created = (await createdResponse.json()) as { id: string };
    tracker.track({ type: 'item', id: created.id, name: itemName });

    await expect(addDialog).toBeHidden();
    const printPrompt = page.locator('.p-confirmdialog:visible');
    await expect(printPrompt).toBeVisible();
    await printPrompt.locator('.p-confirmdialog-reject-button').click();
    await expect(printPrompt).toBeHidden();
    const search = page.getByPlaceholder(/Search by product name or barcode/i);
    await search.fill(itemName);
    const row = page.locator('.desktop-table tbody tr').filter({ hasText: itemName });
    await expect(row).toBeVisible();
    await row.locator('.p-button:has(.pi-pencil)').click();

    const editDialog = page.locator('app-edit-item-overlay .p-dialog:visible');
    await expect(editDialog).toBeVisible();
    await editDialog.locator('#edit-product-name').fill(editedName);
    const updateResponse = page.waitForResponse(
      (response) =>
        response.request().method() === 'PATCH' &&
        new URL(response.url()).pathname === `/api/items/${created.id}`,
    );
    await editDialog.locator('.dialog-actions button').filter({ hasText: /Save/i }).click();
    expect((await updateResponse).ok()).toBe(true);
    tracker.track({ type: 'item', id: created.id, name: editedName });

    await expect(editDialog).toBeHidden();
    await expect(page.locator('.desktop-table tbody')).toContainText(editedName);
  });

  test('sales history, detail and print', async ({ page, request }) => {
    const session = await loginViaApi(request, liveConfig);
    await installLiveApiGuard(page, liveConfig);
    await seedSession(page, session);
    const listResponse = page.waitForResponse(
      (response) =>
        response.request().method() === 'GET' && new URL(response.url()).pathname === '/api/sales',
    );
    await page.goto('/sales');
    await waitForStablePage(page);
    await expect(page.locator('.sales-ledger-page')).toBeVisible();

    const list = (await (await listResponse).json()) as {
      items: ReadonlyArray<{ saleId: string }>;
    };
    test.skip(
      list.items.length === 0,
      'No user-managed sales available for detail and print checks.',
    );
    const saleId = list.items[0]!.saleId;

    const detailResponse = page.waitForResponse(
      (response) =>
        response.request().method() === 'GET' &&
        new URL(response.url()).pathname === `/api/sales/${saleId}`,
    );
    await page.locator('.receipt-btn').first().click();
    expect((await detailResponse).ok()).toBe(true);
    await expect(page.locator('.sale-detail-shell')).toBeVisible();

    await page.emulateMedia({ media: 'print', colorScheme: 'light' });
    await page.goto(`/sales/${encodeURIComponent(saleId)}/print?template=a4`);
    await waitForStablePage(page);
    await expect(page.locator('app-sale-invoice-a4 > article.invoice')).toBeVisible();
    await expect(page.locator('.invoice__header')).toBeVisible();
    await expect(page.locator('.invoice__items')).toBeVisible();
    await expect(page.locator('.invoice__totals')).toBeVisible();
    await expect(page.locator('.screen-controls')).toBeHidden();
  });
});

async function hasInputValue(locator: import('@playwright/test').Locator): Promise<boolean> {
  return locator.evaluate((input) => (input as HTMLInputElement).value.length > 0);
}

async function openAddDialog(
  page: import('@playwright/test').Page,
): Promise<import('@playwright/test').Locator> {
  await page.locator('.inventory-header .p-button').first().click();
  const dialog = page.locator('app-add-product-overlay .p-dialog:visible');
  await expect(dialog).toBeVisible();
  return dialog;
}

function cleanupReportHtml(
  report: import('../support/live-api.fixture').LiveCleanupReport,
): string {
  const retained = report.retained
    .map(({ record, reason }) => `<li>${record.type}: ${record.name} — ${reason}</li>`)
    .join('');
  return `<h1>Live UI audit cleanup</h1><p>Deleted: ${report.deleted.length}</p><ul>${retained}</ul>`;
}
