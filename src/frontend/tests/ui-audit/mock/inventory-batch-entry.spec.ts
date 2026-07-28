import { expect, test, type Page } from '@playwright/test';

import { createShellScenario, installShellFixture } from '../fixtures/shell.fixture';
import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  waitForStablePage,
} from '../support/audit-page';

const API_BASE = 'http://localhost:5277/api';

test.describe('inventory-batch-entry', () => {
  test('marks pending results busy while a save is in progress', async ({ page }) => {
    await openBatchPage(page, { save: 'pending' });
    await seedDraft(page, [draftRow('pending-1', 'Pending save row')]);
    await page.reload();
    await waitForBatchPage(page);

    await page.getByRole('button', { name: 'Save' }).click();

    await expect(page.locator('.table-card')).toHaveAttribute('aria-busy', 'true');
    await expect(page.locator('.loading')).toHaveAttribute('role', 'status');
  });

  test('keeps empty and validation actions explicit', async ({ page }) => {
    await openBatchPage(page, { save: 'success' });

    await expect(page.getByRole('heading', { name: 'No pending rows' }).first()).toBeVisible();
    await expect(page.getByRole('button', { name: 'Save' })).toBeDisabled();
    await expect(page.getByRole('button', { name: 'Clear All' })).toBeDisabled();
    await expect(page.getByRole('button', { name: 'Add Row' })).toBeDisabled();
  });

  test('fits long draft rows and keeps actions reachable', async ({ page }, testInfo) => {
    await openBatchPage(page, { save: 'success' }, 'hi-IN');
    await seedDraft(page, [
      draftRow(
        'long-1',
        'असाधारण रूप से लंबा ऑडिट इन्वेंटरी आइटम जिसका नाम छोटे मोबाइल स्क्रीन पर भी पढ़ने योग्य रहना चाहिए',
      ),
      draftRow(
        'long-2',
        'Another exceptionally long inbound inventory item name for desktop table truncation',
      ),
    ]);
    await page.reload();
    await waitForBatchPage(page);

    await expect(page.getByText('लंबित पंक्तियाँ')).toBeVisible();
    await assertNoHorizontalOverflow(page);
    const rows = page.locator(
      testInfo.project.name === 'chromium-mobile'
        ? '.pending-row-card'
        : '.desktop-pending-table tbody tr',
    );
    await expect(rows).toHaveCount(2);
    await page.getByRole('button', { name: /सहेजें|Save/ }).scrollIntoViewIfNeeded();
    await expect(page.getByRole('button', { name: /सहेजें|Save/ })).toBeVisible();
  });

  test('supports barcode generation and HSN selection in a row', async ({ page }) => {
    await openBatchPage(page, { save: 'success' });

    await page.getByRole('button', { name: 'Expand optional details' }).click();
    const nameInput = page.locator('.item-name-autocomplete input');
    await nameInput.fill('Audit product');
    const hsnRequest = page.waitForRequest(`${API_BASE}/hsn/lookup`);
    await nameInput.press('Tab');
    await hsnRequest;
    await expect(page.getByText('HSN Suggestion')).toBeVisible();
    await page.getByRole('button', { name: 'Cancel' }).click();
    await expect(page.getByText('HSN Suggestion')).toHaveCount(0);

    await page.getByRole('button', { name: 'Collapse optional details' }).click();
    await page.getByRole('button', { name: 'Expand optional details' }).click();
    const retryHsnRequest = page.waitForRequest(`${API_BASE}/hsn/lookup`);
    await nameInput.click();
    await nameInput.press('Tab');
    await retryHsnRequest;
    await expect(page.getByText('HSN Suggestion')).toBeVisible();
    await page.getByRole('button', { name: 'Apply' }).click();
    await expect(page.locator('.hsn-chip')).toContainText('0401');

    await page.getByRole('button', { name: 'Generate' }).click();
    await expect(page.locator('.barcode-autocomplete input')).toHaveValue('AUDIT-GENERATED-001');
  });

  test('shows partial results and retains failed draft rows', async ({ page }) => {
    await openBatchPage(page, { save: 'partial' });
    await seedDraft(page, [
      draftRow('partial-1', 'Failed row'),
      draftRow('partial-2', 'Saved row'),
    ]);
    await page.reload();
    await waitForBatchPage(page);

    await page.getByRole('button', { name: 'Save' }).click();

    await expect(page.locator('.save-summary')).toContainText(
      'Some rows were saved. Fix failed rows and retry.',
    );
    await expect(page.locator('.row-error-text:visible')).toContainText('Audit row failure');
    await expect(visiblePendingRows(page)).toHaveCount(1);
  });

  test('shows full save results and clears persisted rows', async ({ page }) => {
    await openBatchPage(page, { save: 'success' });
    await seedDraft(page, [draftRow('saved-1', 'Saved row')]);
    await page.reload();
    await waitForBatchPage(page);

    await page.getByRole('button', { name: 'Save' }).click();

    await expect(page.locator('.save-summary')).toContainText('Saved rows successfully.');
    await expect(page.getByRole('heading', { name: 'No pending rows' }).first()).toBeVisible();
    await expect(
      page.getByRole('button', { name: 'Print labels for successful rows' }),
    ).toBeVisible();
  });

  test('keeps drafts available after a save error', async ({ page }) => {
    await openBatchPage(page, { save: 'error' });
    await seedDraft(page, [draftRow('error-1', 'Retry row')]);
    await page.reload();
    await waitForBatchPage(page);

    await page.getByRole('button', { name: 'Save' }).click();

    await expect(page.getByText('Unable to save rows right now. Please retry.')).toBeVisible();
    await expect(visiblePendingRows(page)).toContainText('Retry row');
    await expect(page.getByRole('button', { name: 'Save' })).toBeEnabled();
  });

  test('restores persisted drafts on a new page visit', async ({ page }) => {
    await openBatchPage(page, { save: 'success' });
    await seedDraft(page, [draftRow('restored-1', 'Restored draft')]);
    await page.reload();
    await waitForBatchPage(page);

    await expect(visiblePendingRows(page)).toContainText('Restored draft');
    await expect(page.getByText('Rows: 1')).toBeVisible();
  });
});

type BatchScenario = { readonly save: 'pending' | 'success' | 'partial' | 'error' };

async function openBatchPage(page: Page, scenario: BatchScenario, locale = 'en-IN'): Promise<void> {
  await installShellFixture(page, createShellScenario({ locale }));
  await installBatchRoutes(page, scenario);
  await page.goto('/login');
  await seedDraft(page, []);
  await page.goto('/inventory/batch');
  await waitForBatchPage(page);
}

async function installBatchRoutes(page: Page, scenario: BatchScenario): Promise<void> {
  await page.route(`${API_BASE}/hsn/lookup`, (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        hsnCodes: ['0401', '0402'],
        taxScenarios: [
          { condition: 'Audit standard', taxPercentage: '5%' },
          { condition: 'Audit alternative', taxPercentage: '12%' },
        ],
      }),
    }),
  );
  await page.route(`${API_BASE}/items/barcodes/generate`, (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({ barcode: 'AUDIT-GENERATED-001' }),
    }),
  );
  await page.route(`${API_BASE}/inventory/inbound/batch`, async (route) => {
    if (scenario.save === 'pending') {
      await new Promise<void>(() => undefined);
      return;
    }

    if (scenario.save === 'error') {
      await route.fulfill({
        status: 503,
        contentType: 'application/json',
        body: JSON.stringify({
          errors: [{ code: 'Inventory.SaveFailed', description: 'Audit save failure' }],
        }),
      });
      return;
    }

    const { items } = route.request().postDataJSON() as {
      items: Array<{ clientRowId: string; itemName: string; barcode: string }>;
    };
    const failed = scenario.save === 'partial' ? items.slice(0, 1) : [];
    const succeeded = items.slice(failed.length).map((item) => ({
      clientRowId: item.clientRowId,
      result: {
        itemId: `item-${item.clientRowId}`,
        itemName: item.itemName,
        barcode: item.barcode,
        batchId: `batch-${item.clientRowId}`,
        batchNumber: 'AUDIT-BATCH',
        batchQuantity: 1,
        totalQuantity: 1,
        supplierId: null,
        stockTransactionId: `tx-${item.clientRowId}`,
        performedAt: '2026-07-29T00:00:00.000Z',
      },
    }));
    await route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        requestedCount: items.length,
        successCount: succeeded.length,
        failedCount: failed.length,
        succeeded,
        failed: failed.map((item) => ({
          clientRowId: item.clientRowId,
          itemName: item.itemName,
          barcode: item.barcode,
          errors: [{ code: 'Inventory.AuditFailure', description: 'Audit row failure' }],
        })),
      }),
    });
  });
}

async function seedDraft(page: Page, rows: readonly ReturnType<typeof draftRow>[]): Promise<void> {
  await page.evaluate(async (draftRows) => {
    const database = await new Promise<IDBDatabase>((resolve, reject) => {
      const request = indexedDB.open('intelibill-offline-drafts', 1);
      request.onupgradeneeded = () => {
        if (!request.result.objectStoreNames.contains('inventory-inbound-batch')) {
          request.result.createObjectStore('inventory-inbound-batch', { keyPath: 'shopId' });
        }
      };
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction('inventory-inbound-batch', 'readwrite');
      transaction.objectStore('inventory-inbound-batch').put({
        shopId: 'shop-primary',
        rows: draftRows,
        updatedAt: '2026-07-29T00:00:00.000Z',
      });
      transaction.oncomplete = () => resolve();
      transaction.onerror = () => reject(transaction.error);
    });
    database.close();
  }, rows);
}

async function waitForBatchPage(page: Page): Promise<void> {
  const collector = collectBrowserFailures(page);
  try {
    await waitForStablePage(page);
    await expect(page.locator('.inventory-batch-page')).toBeVisible();
    assertNoUnexpectedBrowserFailures(collector.failures);
  } finally {
    collector.dispose();
  }
}

async function assertNoHorizontalOverflow(page: Page): Promise<void> {
  await expect
    .poll(() => page.evaluate(() => document.documentElement.scrollWidth))
    .toBeLessThanOrEqual(await page.evaluate(() => innerWidth));
}

function visiblePendingRows(page: Page) {
  return page.locator('.pending-row-card:visible, .desktop-pending-table tbody tr:visible');
}

function draftRow(clientRowId: string, itemName: string) {
  return {
    clientRowId,
    itemName,
    barcode: `AUDIT-${clientRowId}`,
    itemDescription: null,
    uom: 'PCS',
    batchNumber: 'AUDIT-BATCH',
    quantity: 1,
    totalPurchaseCost: 10,
    mrp: 12,
    salesPrice: 11,
    taxRatePercent: 5,
    taxIncluded: true,
    purchaseTaxIncluded: true,
    hsnCode: null,
    expiryDate: null,
    manufacturingDate: null,
    supplierId: null,
    referenceNumber: null,
    notes: null,
    performedAt: '2026-07-29T00:00:00.000Z',
  };
}
