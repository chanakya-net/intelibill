import { readFile } from 'node:fs/promises';

import { expect, test, type Page, type Route } from '@playwright/test';

import { SUPPORTED_LANGUAGES } from '../../../src/app/core/i18n/language.constants';
import type { OfflineQueuedSaleRecord, OfflineSaleQueueStatus } from '../../../src/app/features/sales/services/offline-sale-core.types';
import type {
  ReconciliationCase,
  ReconciliationCaseSummary,
  RecoverySubmissionResult,
} from '../../../src/app/features/sales/services/offline-reconciliation.models';
import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  mockExternalRequests,
  waitForStablePage,
} from '../support/audit-page';

type ListState = 'ready' | 'loading' | 'empty' | 'error';

const SHOP_ID = 'ui-audit-shop';
const DEVICE_ID = 'ui-audit-console-device';
const OWNER_ID = 'ui-audit-owner';
const OTHER_CASHIER_ID = 'ui-audit-cashier-2';
const LINKED_SALE_ID = '3f2a9c1e-5b7d-4e8f-9a0b-1c2d3e4f5a6b';

const LOCAL_RECORDS: readonly OfflineQueuedSaleRecord[] = [
  queuedRecord('own-failed', 'OFF-2026-27-VERY-LONG-OFFLINE-REFERENCE-0000000001', 'Failed', OWNER_ID),
  {
    ...queuedRecord('held', 'OFF-2026-27-0002', 'NeedsReview', OWNER_ID),
    errorCode: 'OfflineReconciliation.HistoricalLeaseReviewRequired',
    reviewCaseId: 'case-held',
  },
  queuedRecord('other-pending', 'OFF-2026-27-0003', 'Pending', OTHER_CASHIER_ID),
  queuedRecord('other-failed', 'OFF-2026-27-0004', 'Failed', OTHER_CASHIER_ID),
];

const OPEN_CASES: readonly ReconciliationCaseSummary[] = [
  summary('case-held', 'OFF-2026-27-0002', 'IssueTimingReview', 'OfflineReconciliation.HistoricalLeaseReviewRequired'),
  summary('case-stock', 'OFF-2026-27-0005', 'StockVariance', 'offline_sync.stock_shortage'),
  summary('case-legacy', null, 'InvoiceConflict', 'Sale.IdempotencyConflict', { status: 'Escalated' }),
];
const PAGE_TWO_CASES: readonly ReconciliationCaseSummary[] = [
  summary('case-page-two', 'OFF-PAGE-TWO-ONLY', 'PricingVariance', 'offline_sync.pricing_variance'),
];
const RESOLVED_CASES: readonly ReconciliationCaseSummary[] = [
  summary('case-resolved', 'OFF-RESOLVED-ONLY', 'CustomerVariance', 'offline_sync.customer_changed', {
    status: 'Resolved',
    resolution: 'AcknowledgeVariance',
  }),
];

test.describe('offline-reconciliation', () => {
  test('lists local and server work, filters, pages, and resolves a case through a version conflict', async ({ page }) => {
    const collector = collectBrowserFailures(page, {
      ignoreConsole: (message) => message.includes('409') && message.includes('Failed to load resource'),
      ignoreResponse: (response) => response.url().includes('/resolve') && response.status() === 409,
    });
    const resolveBodies: Record<string, unknown>[] = [];
    try {
      await installConsoleScenario(page, { resolveBodies, seed: true });
      await visitConsole(page);

      const unsent = page.locator('.queue-group.unsent .queue-row');
      await expect(unsent).toHaveCount(1);
      await expect(unsent).toContainText(LOCAL_RECORDS[0].invoiceNumber);
      await expect(unsent.locator('a.print-link')).toHaveAttribute('href', '/sales/own-failed/print?offline=1');

      const awaiting = page.locator('.queue-group.awaiting .queue-row');
      await expect(awaiting).toHaveCount(2);
      await expect(awaiting.first()).toContainText(OTHER_CASHIER_ID);
      await expect(page.locator('.queue-group.awaiting button')).toHaveCount(0);
      await expect(page.locator('.handoff-notice.unsafe')).toContainText('4 invoices have not reached the server.');

      await expect(page.locator('.case-row')).toHaveCount(3);
      await expect(page.locator('.cases-table')).toContainText('Reached the server after its invoice range expired');
      await expect(page.locator('.unresolved-count')).toContainText('3 unresolved');

      const resolvedResponse = waitForList(page, 'resolved', '1');
      await page.locator('.case-status-filter').getByRole('button', { name: 'Resolved', exact: true }).click();
      await resolvedResponse;
      await expect(page.locator('.case-row')).toHaveCount(1);
      await expect(page.locator('.cases-table')).toContainText('OFF-RESOLVED-ONLY');

      const openResponse = waitForList(page, 'open', '1');
      await page.locator('.case-status-filter').getByRole('button', { name: 'Open', exact: true }).click();
      await openResponse;
      const pageTwo = waitForList(page, 'open', '2');
      await page.locator('.cases-paginator .p-paginator-next').click();
      await pageTwo;
      await expect(page.locator('.cases-table')).toContainText('OFF-PAGE-TWO-ONLY');
      await page.locator('.cases-paginator .p-paginator-first').click();
      await expect(page.locator('.case-row')).toHaveCount(3);

      // A held local invoice opens its own server case.
      await page.locator('.queue-group.review').getByRole('button', { name: 'Open case' }).click();
      const detail = page.locator('.case-detail');
      await expect(detail).toBeVisible();
      await expect(page.locator('.case-row.selected')).toContainText('OFF-2026-27-0002');
      await expect(detail.locator('.people-list')).toContainText('Unknown user');
      await expect(detail.locator('.people-list')).toContainText('Not recorded');
      await expect(detail.locator('.snapshot-lines')).toContainText('Masala chai with a very long localized catalogue name');
      await expect(detail.locator('.event-list')).toContainText('Asked the cashier for the paper copy');
      await expect(detail.locator('.linked-sale a')).toHaveAttribute('href', '/sales/sale-linked-001/print');

      await detail.locator('.resolve-action-select').click();
      await page.getByRole('option', { name: 'Link to an existing sale' }).click();
      await expect(detail.locator('.linked-operation-input')).toBeVisible();
      await detail.locator('.resolve-submit-btn').click();
      await expect(detail.locator('.field-error')).toHaveCount(2);

      await detail.locator('.resolve-reason').fill('x'.repeat(501));
      await expect(detail.locator('.reason-count.over')).toContainText('501/500');
      await detail.locator('.resolve-reason').fill('Matched the paper copy to the posted sale.');
      await detail.locator('.linked-operation-input').fill('not-an-id');
      await expect(detail.locator('.field-error')).toContainText('Enter a valid ID.');
      await detail.locator('.linked-operation-input').fill(LINKED_SALE_ID);

      const conflict = page.waitForResponse((response) => response.url().endsWith('/case-held/resolve'));
      await detail.locator('.resolve-submit-btn').click();
      await conflict;
      await expect(detail.locator('.resolve-message.error')).toContainText(
        'This case changed while you were looking at it. It has been reloaded',
      );
      await expect(detail.locator('.case-status')).toContainText('Open');

      const resolved = page.waitForResponse((response) => response.url().endsWith('/case-held/resolve'));
      await detail.locator('.resolve-submit-btn').click();
      await resolved;
      await expect(detail.locator('.resolve-message.success')).toContainText('The server updated this case.');
      await expect(detail.locator('.case-status')).toContainText('Resolved');
      await expect(detail.locator('.resolve-form')).toHaveCount(0);
      expect(resolveBodies).toHaveLength(2);
      expect(resolveBodies[0]).toMatchObject({ action: 'LinkExistingSale', expectedVersion: 7, linkedOperationId: LINKED_SALE_ID });
      expect(resolveBodies[1]).toMatchObject({ expectedVersion: 8 });
      expect(resolveBodies[1]['operationId']).not.toBe(resolveBodies[0]['operationId']);

      await page.locator('.case-row', { hasText: 'OFF-2026-27-0005' }).locator('.case-open-btn').click();
      await expect(detail.locator('.quantity-cell.unresolved')).toContainText('2');
      await page.locator('.case-row').nth(2).locator('.case-open-btn').click();
      await expect(detail.locator('.envelope-missing')).toContainText('Original envelope not recorded');
      await detail.locator('.close-detail-btn').click();
      await expect(detail).toHaveCount(0);

      const retried = page.waitForResponse((response) => response.url().includes('/api/sales/offline-sync'));
      await unsent.getByRole('button', { name: /Retry invoice/ }).click();
      await retried;
      await expect(page.locator('.queue-group.unsent')).toHaveCount(0);

      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('keeps the console usable in every locale and audit viewport', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-mobile', 'manual locale and viewport matrix');
    test.setTimeout(180_000);
    const collector = collectBrowserFailures(page);
    try {
      for (const locale of SUPPORTED_LANGUAGES) {
        for (const viewport of [
          { width: 1440, height: 900 },
          { width: 360, height: 800 },
        ]) {
          await page.unrouteAll({ behavior: 'ignoreErrors' });
          await installConsoleScenario(page, { locale, seed: true });
          await page.setViewportSize(viewport);
          await visitConsole(page);
          await expect(page.locator('.case-row')).toHaveCount(3);
          await page.locator('.case-row').first().locator('.case-open-btn').click();
          await expect(page.locator('.case-detail .snapshot-lines')).toBeVisible();
          await expect(page.locator('.reconciliation-page')).not.toContainText('sales.offlineReconciliation.');
          await expect(page.locator('.reconciliation-page')).not.toContainText('sales.history.');
          await expect(page.locator('.cases-table-wrap')).toHaveCSS('overflow-x', 'auto');
          await assertNoPageOverflow(page);
        }
      }
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('presents loading, empty, and error feedback', async ({ page }) => {
    const loadingCollector = collectBrowserFailures(page);
    try {
      await installConsoleScenario(page, { listState: 'loading' });
      await page.goto('/sales/offline-reconciliation');
      await expect(page.locator('.cases-table p-skeleton').first()).toBeVisible();
      await expect(page.locator('button.refresh-cases-btn')).toBeDisabled();
      await expect(page.locator('.case-row')).toHaveCount(3);

      await page.locator('.case-row').first().locator('.case-open-btn').click();
      await expect(page.locator('.detail-loading')).toBeVisible();
      await expect(page.locator('.case-detail .snapshot-lines')).toBeVisible();
      assertNoUnexpectedBrowserFailures(loadingCollector.failures);
    } finally {
      loadingCollector.dispose();
    }

    const emptyCollector = collectBrowserFailures(page);
    try {
      await page.unrouteAll({ behavior: 'ignoreErrors' });
      await installConsoleScenario(page, { listState: 'empty' });
      await visitConsole(page);
      await expect(page.locator('.case-empty')).toContainText('No cases match this filter.');
      await expect(page.locator('.queue-empty')).toContainText('Every invoice billed on this browser has reached the server.');
      await expect(page.locator('.handoff-notice.safe')).toBeVisible();
      await expect(page.locator('button.export-bundle-btn')).toBeDisabled();
      assertNoUnexpectedBrowserFailures(emptyCollector.failures);
    } finally {
      emptyCollector.dispose();
    }

    const errorCollector = collectBrowserFailures(page, {
      ignoreConsole: (message) => message.includes('503') && message.includes('Failed to load resource'),
      ignoreResponse: (response) => response.url().includes('/api/offline-reconciliation') && response.status() === 503,
    });
    try {
      await page.unrouteAll({ behavior: 'ignoreErrors' });
      await installConsoleScenario(page, { listState: 'error' });
      await visitConsole(page);
      await expect(page.locator('.cases-error[role="alert"]')).toContainText('Review cases could not be loaded.');
      await expect(page.locator('.cases-panel')).toBeVisible();
      assertNoUnexpectedBrowserFailures(errorCollector.failures);
    } finally {
      errorCollector.dispose();
    }
  });

  test('exports a support bundle and imports one with per-invoice results', async ({ page }) => {
    const collector = collectBrowserFailures(page, {
      ignoreConsole: (message) => message.includes('400') && message.includes('Failed to load resource'),
      ignoreResponse: (response) => response.url().includes('/recovery-submissions') && response.status() === 400,
    });
    const recoveryBodies: string[] = [];
    try {
      await installConsoleScenario(page, { seed: true, recoveryBodies });
      await visitConsole(page);
      await expect(page.locator('.handoff-notice.unsafe')).toBeVisible();

      const downloadEvent = page.waitForEvent('download');
      await page.locator('button.export-bundle-btn').click();
      const download = await downloadEvent;
      expect(download.suggestedFilename()).toMatch(/^offline-recovery-ui-audit-console-device-\d{12}\.json$/);
      const bundleText = await readFile(await download.path(), 'utf8');
      const bundle = JSON.parse(bundleText) as { invoices: { clientSaleId: string }[]; checksum: string };
      expect(bundle.invoices.map((invoice) => invoice.clientSaleId).sort()).toEqual(
        ['held', 'other-failed', 'other-pending', 'own-failed'],
      );
      expect(bundleText).not.toMatch(/token|session|password/i);
      await expect(page.locator('.handoff-notice.safe')).toContainText('carries all 4 unsynced invoices');
      await expect(page.locator('.last-export')).toContainText(bundle.checksum.slice(0, 12));

      const fileInput = page.locator('.import-box input[type="file"]');
      await fileInput.setInputFiles({ name: 'tampered.json', mimeType: 'application/json', buffer: Buffer.from('{"format":"x"}') });
      await page.locator('button.submit-import-btn').click();
      await expect(page.locator('.import-error')).toContainText("The bundle's invoices do not match its checksum.");

      await fileInput.setInputFiles({ name: download.suggestedFilename(), mimeType: 'application/json', buffer: Buffer.from(bundleText) });
      await page.locator('button.submit-import-btn').click();
      const results = page.locator('.import-result');
      await expect(results).toHaveCount(3);
      await expect(results.nth(1)).toContainText('The invoice number was not reserved for the bundle');
      await expect(results.nth(2).locator('a.print-link')).toHaveAttribute('href', '/sales/sale-recovered-001/print');
      expect(recoveryBodies).toHaveLength(2);
      expect(JSON.parse(recoveryBodies[1]).bundle.checksum).toBe(bundle.checksum);

      await results.first().getByRole('button', { name: 'Open case' }).click();
      await expect(page.locator('.case-detail')).toBeVisible();

      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('keeps import for owners and managers and shows stale server state while offline', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await installConsoleScenario(page, { seed: true, role: 'Staff' });
      await visitConsole(page);
      await expect(page.locator('.case-row')).toHaveCount(3);
      await expect(page.locator('.import-box')).toHaveCount(0);
      await expect(page.locator('button.export-bundle-btn')).toBeEnabled();

      await page.locator('.case-row').first().locator('.case-open-btn').click();
      await expect(page.locator('.case-detail .case-status')).toContainText('Open');
      await page.evaluate(() => window.dispatchEvent(new Event('offline')));

      await expect(page.locator('.stale-notice')).toContainText('The server cannot be reached.');
      await expect(page.locator('button.refresh-cases-btn')).toBeDisabled();
      await expect(page.locator('button.resolve-submit-btn')).toBeDisabled();
      await expect(page.locator('.queue-group.unsent button')).toBeDisabled();
      await expect(page.locator('.case-detail .case-status')).toContainText('Open');
      await expect(page.locator('button.export-bundle-btn')).toBeEnabled();

      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });
});

interface ConsoleScenarioOptions {
  readonly listState?: ListState;
  readonly locale?: string;
  readonly seed?: boolean;
  readonly role?: 'Owner' | 'Staff';
  readonly resolveBodies?: Record<string, unknown>[];
  readonly recoveryBodies?: string[];
}

async function installConsoleScenario(page: Page, options: ConsoleScenarioOptions): Promise<void> {
  const listState = options.listState ?? 'ready';
  await mockExternalRequests(page, {
    authenticated: true,
    locale: options.locale ?? 'en-IN',
    localStorageEntries: { [`intelibill.offlineSales.deviceId.v1:${SHOP_ID}`]: DEVICE_ID },
  });
  if (options.role === 'Staff') {
    await page.addInitScript(() => {
      const key = 'inventory.auth.session.local';
      const session = JSON.parse(localStorage.getItem(key) ?? '{}') as { shops?: { role: string }[] };
      if (session.shops) session.shops = session.shops.map((shop) => ({ ...shop, role: 'Staff' }));
      localStorage.setItem(key, JSON.stringify(session));
    });
    await page.route('**/api/shops/me', async (route) =>
      fulfillJson(route, [{ shopId: SHOP_ID, shopName: 'UI Audit Shop', role: 'Staff', isDefault: true, lastUsedAt: null }]),
    );
  }

  let resolveAttempts = 0;
  await page.route('**/api/offline-reconciliation**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());

    if (request.method() === 'GET' && url.pathname === '/api/offline-reconciliation') {
      await fulfillList(route, url, listState);
      return;
    }

    if (request.method() === 'POST' && url.pathname === '/api/offline-reconciliation/recovery-submissions') {
      const body = request.postData() ?? '';
      options.recoveryBodies?.push(body);
      if ((options.recoveryBodies?.length ?? 0) === 1) {
        await fulfillJson(route, { title: 'Recovery.BundleChecksumMismatch', status: 400 }, 400);
        return;
      }
      await fulfillJson(route, recoveryResult());
      return;
    }

    const resolveMatch = url.pathname.match(/^\/api\/offline-reconciliation\/([^/]+)\/resolve$/);
    if (request.method() === 'POST' && resolveMatch) {
      options.resolveBodies?.push(request.postDataJSON() as Record<string, unknown>);
      resolveAttempts += 1;
      if (resolveAttempts === 1) {
        await fulfillJson(route, { title: 'Reconciliation.CaseVersionConflict', status: 409 }, 409);
        return;
      }
      await fulfillJson(
        route,
        caseDetail(resolveMatch[1], {
          version: 9,
          status: 'Resolved',
          resolution: 'LinkExistingSale',
          resolutionReason: 'Matched the paper copy to the posted sale.',
          resolvedAt: '2026-11-03T10:00:00.000Z',
          resolvedBy: { userId: OWNER_ID, displayName: 'UI Audit Owner' },
          availableActions: [],
        }),
      );
      return;
    }

    const detailMatch = url.pathname.match(/^\/api\/offline-reconciliation\/([^/]+)$/);
    if (request.method() === 'GET' && detailMatch) {
      if (listState === 'loading') await delay(800);
      // The reload after a conflict answers the newer version the reviewer must look at again.
      await fulfillJson(route, caseDetail(detailMatch[1], resolveAttempts > 0 ? { version: 8 } : {}));
      return;
    }

    await route.fulfill({ status: 404, contentType: 'application/json', body: '{}' });
  });

  await page.route('**/api/sales/offline-sync', async (route) => {
    const body = route.request().postDataJSON() as { sales: { clientSaleId: string; invoiceNumber: string }[] };
    await fulfillJson(route, {
      results: body.sales.map((sale) => ({
        clientSaleId: sale.clientSaleId,
        status: 'Synced',
        saleId: `sale-${sale.clientSaleId}`,
        invoiceNumber: sale.invoiceNumber,
        errors: [],
        warnings: [],
      })),
    });
  });

  if (options.seed) await seedQueue(page, LOCAL_RECORDS);
}

async function seedQueue(page: Page, records: readonly OfflineQueuedSaleRecord[]): Promise<void> {
  await page.goto('/');
  await page.evaluate(async (seed) => {
    const database = await new Promise<IDBDatabase>((resolve, reject) => {
      const request = indexedDB.open('intelibill-offline-sales-queue', 1);
      request.onupgradeneeded = () => {
        if (!request.result.objectStoreNames.contains('queued-sales')) {
          request.result.createObjectStore('queued-sales', { keyPath: 'key' });
        }
      };
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
    await new Promise<void>((resolve, reject) => {
      const tx = database.transaction('queued-sales', 'readwrite');
      seed.forEach((record) => tx.objectStore('queued-sales').put(record));
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error);
    });
    database.close();
  }, records);
}

async function visitConsole(page: Page): Promise<void> {
  await page.goto('/sales/offline-reconciliation');
  await waitForStablePage(page);
  await expect(page.locator('.reconciliation-page')).toBeVisible();
}

function waitForList(page: Page, status: string, pageNumber: string): Promise<import('@playwright/test').Response> {
  return page.waitForResponse((response) => {
    const url = new URL(response.url());
    return (
      url.pathname === '/api/offline-reconciliation' &&
      url.searchParams.get('status') === status &&
      url.searchParams.get('page') === pageNumber
    );
  });
}

async function fulfillList(route: Route, url: URL, state: ListState): Promise<void> {
  if (state === 'loading') await delay(1_000);
  if (state === 'error') {
    await fulfillJson(route, { title: 'UiAudit.Unavailable', status: 503 }, 503);
    return;
  }

  const status = url.searchParams.get('status') ?? 'open';
  const pageNumber = Number(url.searchParams.get('page') ?? '1');
  const items =
    state === 'empty' ? [] : status === 'resolved' ? RESOLVED_CASES : pageNumber === 2 ? PAGE_TWO_CASES : OPEN_CASES;
  await fulfillJson(route, {
    items,
    page: pageNumber,
    pageSize: 20,
    totalCount: state === 'empty' ? 0 : status === 'resolved' ? 1 : 23,
    unresolvedCount: state === 'empty' ? 0 : 3,
  });
}

function recoveryResult(): RecoverySubmissionResult {
  return {
    operationId: 'ui-audit-recovery',
    results: [
      { clientSaleId: 'own-failed', invoiceNumber: 'OFF-2026-27-0001', status: 'ReviewOpened', caseId: 'case-held', saleId: null, errors: [] },
      {
        clientSaleId: 'other-pending',
        invoiceNumber: 'OFF-2026-27-0003',
        status: 'Rejected',
        caseId: null,
        saleId: null,
        errors: [{ code: 'Recovery.LeaseNotOwned', message: 'Lease not owned.' }],
      },
      { clientSaleId: 'other-failed', invoiceNumber: 'OFF-2026-27-0004', status: 'AlreadyPosted', caseId: null, saleId: 'sale-recovered-001', errors: [] },
    ],
  };
}

function summary(
  caseId: string,
  invoiceNumber: string | null,
  issueType: string,
  reasonCode: string,
  overrides: Partial<ReconciliationCaseSummary> = {},
): ReconciliationCaseSummary {
  return {
    caseId,
    version: 7,
    invoiceNumber,
    clientSaleId: `client-${caseId}`,
    deviceId: DEVICE_ID,
    issueType,
    status: 'Open',
    reasonCodes: [reasonCode],
    message: 'The invoice reached the server only after its invoice range expired, so an owner or manager must review it.',
    saleId: null,
    resolution: null,
    createdAt: '2026-11-02T09:00:00.000Z',
    createdBy: { userId: OWNER_ID, displayName: 'UI Audit Owner' },
    resolvedAt: null,
    ...overrides,
  };
}

function caseDetail(caseId: string, overrides: Partial<ReconciliationCase> = {}): ReconciliationCase {
  const listed = [...OPEN_CASES, ...PAGE_TWO_CASES, ...RESOLVED_CASES].find((item) => item.caseId === caseId) ??
    summary(caseId, 'OFF-2026-27-0001', 'RecoveryReview', 'OfflineReconciliation.RecoveryReviewRequired');
  const isStock = caseId === 'case-stock';
  const isLegacy = caseId === 'case-legacy';
  return {
    ...listed,
    saleId: isStock ? 'sale-linked-001' : caseId === 'case-held' ? 'sale-linked-001' : null,
    itemId: isStock ? 'item-1' : null,
    inventoryBatchId: isStock ? 'batch-1' : null,
    printedQuantity: isStock ? 5 : null,
    availableQuantity: isStock ? 3 : null,
    consumedQuantity: isStock ? 3 : null,
    unresolvedQuantity: isStock ? 2 : null,
    resolutionReason: null,
    linkedOperationId: null,
    resolutionStockTransactionId: null,
    resolvedBy: null,
    hasOriginalEnvelope: !isLegacy,
    submission: isLegacy
      ? null
      : {
          submissionId: `submission-${caseId}`,
          source: caseId === 'case-held' ? 'Sync' : 'Recovery',
          claimedSoldAt: '2026-10-28T10:00:00.000Z',
          receivedAt: '2026-11-02T09:00:00.000Z',
          payloadHash: 'a'.repeat(64),
          originalLeaseId: 'lease-1',
          submittedBy: { userId: 'former-member', displayName: '' },
          assertedIssuer: { userId: OTHER_CASHIER_ID, displayName: 'Ravi Cashier' },
          originalIssuer: null,
          recoveryOperationId: caseId === 'case-held' ? null : 'recovery-operation-0001',
          invoice: {
            invoiceNumber: listed.invoiceNumber ?? 'OFF-2026-27-0001',
            soldAt: '2026-10-28T10:00:00.000Z',
            customerName: 'Alexandria Cassandra Long Customer Name For Responsive Console Audits',
            customerPhone: '+919999999999',
            paymentMethod: 'UPI',
            subtotalBeforeDiscount: 400,
            totalDiscountAmount: 20,
            totalTaxAmount: 19,
            totalAmount: 399,
            paidAmount: 399,
            dueAmount: 0,
            lines: [
              {
                itemName: 'Masala chai with a very long localized catalogue name',
                barcode: 'CHAI-0001',
                batchNumber: 'BATCH-2026-0001',
                quantity: 5,
                salesPrice: 80,
                taxRatePercent: 5,
                taxAmount: 19,
                totalAmount: 399,
              },
            ],
          },
        },
    availableActions: isLegacy ? ['Escalate'] : ['RetryHistoricalLease', 'LinkExistingSale', 'Escalate'],
    events: [
      {
        action: 'Escalate',
        fromStatus: 'Open',
        toStatus: 'Escalated',
        actor: { userId: OWNER_ID, displayName: 'UI Audit Owner' },
        reason: 'Asked the cashier for the paper copy before deciding.',
        linkedOperationId: null,
        occurredAt: '2026-11-02T10:00:00.000Z',
      },
    ],
    ...overrides,
  };
}

function queuedRecord(
  clientSaleId: string,
  invoiceNumber: string,
  status: OfflineSaleQueueStatus,
  issuedByUserId: string,
): OfflineQueuedSaleRecord {
  const soldAt = '2026-10-28T10:00:00.000Z';
  return {
    key: `${SHOP_ID}::${DEVICE_ID}::${clientSaleId}`,
    shopId: SHOP_ID,
    deviceId: DEVICE_ID,
    clientSaleId,
    idempotencyKey: `idem-${clientSaleId}`,
    invoiceNumber,
    soldAt,
    payload: {
      clientSaleId,
      idempotencyKey: `idem-${clientSaleId}`,
      shopId: SHOP_ID,
      deviceId: DEVICE_ID,
      invoiceNumber,
      soldAt,
      pricing: {
        lines: [],
        totals: { totalBeforeDiscount: 100, totalDiscount: 0, totalTax: 0, grandTotal: 100, paidAmount: 100, dueAmount: 0 },
      },
      paymentMethod: 1,
      customerId: null,
      customerName: null,
      customerPhone: null,
      issuedByUserId,
    },
    status,
    warnings: [],
    errorCode: status === 'Failed' ? 'offline_sync.request_failed' : null,
    errorMessage: null,
    serverSaleId: null,
    reviewCaseId: null,
    mismatches: [],
    createdAt: soldAt,
    updatedAt: soldAt,
    syncAttemptCount: status === 'Failed' ? 1 : 0,
    lastSyncAttemptAt: status === 'Failed' ? soldAt : null,
    syncAttempts: [],
  };
}

async function assertNoPageOverflow(page: Page): Promise<void> {
  const dimensions = await page.evaluate(() => ({
    body: document.body.scrollWidth,
    document: document.documentElement.scrollWidth,
    viewport: window.innerWidth,
  }));
  expect(dimensions.body).toBeLessThanOrEqual(dimensions.viewport + 5);
  expect(dimensions.document).toBeLessThanOrEqual(dimensions.viewport + 5);
}

async function fulfillJson(route: Route, body: unknown, status = 200): Promise<void> {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}

async function delay(milliseconds: number): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, milliseconds));
}
