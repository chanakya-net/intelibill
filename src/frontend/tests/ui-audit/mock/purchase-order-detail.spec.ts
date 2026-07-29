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

const STATUS_CASES = [
  {
    order: PURCHASE_ORDER_STATUSES[0]!,
    status: 'Draft',
    actions: [/edit/i, /place order/i, /delete/i],
  },
  { order: PURCHASE_ORDER_STATUSES[1]!, status: 'Placed', actions: [/receive/i] },
  { order: PURCHASE_ORDER_STATUSES[2]!, status: 'Partially', actions: [/receive/i, /close/i] },
  { order: PURCHASE_ORDER_STATUSES[3]!, status: 'Received', actions: [] },
  { order: PURCHASE_ORDER_STATUSES[4]!, status: 'Closed', actions: [] },
  { order: PURCHASE_ORDER_STATUSES[5]!, status: 'Cancelled', actions: [] },
] as const;

test.describe('purchase-order-detail', () => {
  for (const statusCase of STATUS_CASES) {
    test(`renders ${statusCase.status} status and permitted actions`, async ({ page }) => {
      await withDetailPage(
        page,
        createPurchaseOrdersScenario({ orders: [statusCase.order] }),
        async () => {
          await expect(page.locator('.po-hero')).toBeVisible();
          await expect(page.locator('.po-status-pill')).toContainText(statusCase.status);
          await expect(page.locator('.summary-card')).toHaveCount(4);
          await expect(page.locator('.po-detail-page p-table').first()).toBeVisible();
          await assertActions(page, statusCase.actions);
        },
        statusCase.order.purchaseOrderId,
      );
    });
  }

  test('renders long lines and populated receipt history values', async ({ page }) => {
    const scenario = createPurchaseOrdersScenario({
      orders: [{ ...LONG_PURCHASE_ORDER, status: 'Received' }],
      withLongLines: true,
      withReceiptHistory: true,
    });
    await withDetailPage(
      page,
      scenario,
      async () => {
        const mainRows = page.locator('.po-detail-page > .directory-panel tbody tr');
        await expect(mainRows).toHaveCount(2);
        await expect(mainRows.first()).toContainText('Very Long Description');
        await expect(mainRows.first()).toContainText('100');
        await expect(mainRows.first()).toContainText('75');

        const history = page.locator('app-purchase-order-receipt-history');
        await expect(history).toBeVisible();
        await expect(history.locator('.receipt-block')).toHaveCount(2);
        await expect(history.locator('.receipt-number')).toHaveText([
          'REC-2026-001',
          'REC-2026-002',
        ]);
        await expect(history.locator('tbody tr')).toHaveCount(2);
        await expect(history).toContainText('BATCH-2026-001');
        await expect(history).toContainText('50');
        await expect(history).toContainText('25');
      },
      LONG_PURCHASE_ORDER.purchaseOrderId,
    );
  });

  test('renders loading and accessible error states', async ({ page }) => {
    await installPurchaseOrdersFixture(page, createPurchaseOrdersScenario({ apiState: 'loading' }));
    await page.goto(`${ROUTE}/${PURCHASE_ORDER_STATUSES[0]!.purchaseOrderId}`);
    await expect(page.locator('.directory-panel--loading[aria-busy="true"]')).toBeVisible();
    await page.unrouteAll({ behavior: 'ignoreErrors' });

    await installPurchaseOrdersFixture(page, createPurchaseOrdersScenario({ apiState: 'error' }));
    await page.goto(`${ROUTE}/${PURCHASE_ORDER_STATUSES[0]!.purchaseOrderId}`);
    await expect(page.getByRole('alert')).toBeVisible();
    await expect(page.locator('.po-hero')).toHaveCount(0);
  });

  test('validates receipt quantity then submits a receive result', async ({ page }) => {
    const scenario = createPurchaseOrdersScenario({
      orders: [LONG_PURCHASE_ORDER],
      withLongLines: true,
      withReceiptHistory: true,
    });
    await withDetailPage(
      page,
      scenario,
      async () => {
        await page.getByRole('button', { name: /receive/i }).click();
        const dialog = page.getByRole('dialog');
        const row = dialog.getByTestId('receipt-row');
        const quantity = row.locator('.p-inputnumber-input').first();
        const submit = dialog.getByRole('button', { name: /^receive$/i });

        await expect(dialog).toBeVisible();
        await expect(row).toHaveCount(1);
        await quantity.fill('26');
        await expect(dialog.getByRole('alert')).toHaveText(
          'Quantity cannot exceed remaining quantity.',
        );
        await expect(submit).toBeDisabled();

        await quantity.fill('25');
        await row.locator('.barcode-autocomplete input').fill('AUDIT-BARCODE-001');
        const received = page.waitForResponse(
          (response) =>
            response.request().method() === 'POST' &&
            response.url().endsWith('/purchase-orders/po-long-values/receipts'),
        );
        await submit.click();
        await received;

        await expect(dialog).toHaveCount(0);
        await expect(page.locator('.po-status-pill')).toContainText('Received');
        const history = page.locator('app-purchase-order-receipt-history');
        await expect(history.locator('.receipt-number')).toHaveText([
          'REC-2026-001',
          'REC-2026-002',
          'REC-2026-003',
        ]);
        await expect(history.locator('.receipt-block').last()).toContainText('25');
      },
      LONG_PURCHASE_ORDER.purchaseOrderId,
    );
  });

  test('keeps detail content responsive and localized across the matrix', async ({}, testInfo) => {
    test.skip(
      testInfo.project.name !== 'chromium-mobile',
      'scoped browser, viewport, and locale matrix',
    );
    test.setTimeout(180_000);

    const scenario = createPurchaseOrdersScenario({
      orders: [LONG_PURCHASE_ORDER],
      withLongLines: true,
    });
    for (const browserType of [chromium, firefox, webkit]) {
      for (const viewport of VIEWPORTS) {
        await withDetailBrowser(browserType, viewport, scenario, (page) =>
          assertResponsiveDetail(page, viewport),
        );
      }
    }
    for (const locale of SUPPORTED_LANGUAGES) {
      for (const viewport of [VIEWPORTS[0], VIEWPORTS.at(-1)!]) {
        await withDetailBrowser(
          chromium,
          viewport,
          createPurchaseOrdersScenario({
            locale,
            orders: [LONG_PURCHASE_ORDER],
            withLongLines: true,
          }),
          async (page) => {
            await assertLocalizedDetail(page, locale);
            await assertResponsiveDetail(page, viewport);
          },
        );
      }
    }
  });
});

async function withDetailPage(
  page: Page,
  scenario: PurchaseOrdersScenario,
  assertion: () => Promise<void>,
  purchaseOrderId: string,
): Promise<void> {
  const collector = collectBrowserFailures(page);
  try {
    await installPurchaseOrdersFixture(page, scenario);
    await page.goto(`${ROUTE}/${purchaseOrderId}`);
    await waitForStablePage(page);
    await assertion();
    assertNoUnexpectedBrowserFailures(collector.failures);
  } finally {
    collector.dispose();
  }
}

async function assertActions(page: Page, visibleActions: readonly RegExp[]): Promise<void> {
  for (const action of [/edit/i, /place order/i, /delete/i, /receive/i, /close/i]) {
    const button = page.getByRole('button', { name: action });
    if (visibleActions.some((visible) => visible.source === action.source)) {
      await expect(button).toBeVisible();
    } else {
      await expect(button).toHaveCount(0);
    }
  }
}

async function withDetailBrowser(
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

  try {
    await withDetailPage(
      page,
      scenario,
      () => assertion(page),
      LONG_PURCHASE_ORDER.purchaseOrderId,
    );
  } finally {
    await context.close();
    await browser.close();
  }
}

async function assertResponsiveDetail(
  page: Page,
  viewport: (typeof VIEWPORTS)[number],
): Promise<void> {
  const metrics = await page.evaluate(() => {
    const lines = document.querySelector<HTMLElement>('.po-detail-page .directory-panel__surface');
    const table = lines?.querySelector<HTMLElement>('.p-datatable-table-container');
    const lineCells = Array.from(
      document.querySelectorAll<HTMLElement>('.po-detail-page tbody tr td:first-child'),
    );
    const cards = Array.from(document.querySelectorAll<HTMLElement>('.summary-card'));
    const grid = document.querySelector<HTMLElement>('.summary-grid');
    const lineBounds = lines?.getBoundingClientRect();
    return {
      documentWidth: document.documentElement.scrollWidth,
      summaryColumns: grid ? getComputedStyle(grid).gridTemplateColumns.split(' ').length : 0,
      cardsInsideViewport: cards.every((card) => {
        const bounds = card.getBoundingClientRect();
        return bounds.left >= 0 && bounds.right <= window.innerWidth;
      }),
      linesContainRows:
        !!lineBounds &&
        lineCells.every((cell) => {
          const bounds = cell.getBoundingClientRect();
          return bounds.top >= lineBounds.top && bounds.bottom <= lineBounds.bottom;
        }),
      lineTableAccessible: !!lines && !!table,
    };
  });

  expect(metrics.documentWidth).toBeLessThanOrEqual(viewport.width);
  expect(metrics.summaryColumns).toBe(viewport.summaryColumns);
  expect(metrics.cardsInsideViewport).toBe(true);
  expect(metrics.linesContainRows).toBe(true);
  expect(metrics.lineTableAccessible).toBe(true);
}

async function assertLocalizedDetail(
  page: Page,
  locale: (typeof SUPPORTED_LANGUAGES)[number],
): Promise<void> {
  const labels = await page.evaluate(async (activeLocale) => {
    const response = await fetch(`/assets/i18n/${activeLocale}.json`);
    const translations = (await response.json()) as {
      purchaseOrders: { detail: { title: string; lines: string }; actions: { receive: string } };
    };
    return translations.purchaseOrders;
  }, locale);

  await expect(page.locator('.po-detail-page h2')).toContainText(labels.detail.title);
  await expect(page.locator('.directory-panel__eyebrow')).toContainText(labels.detail.lines);
  await expect(page.getByRole('button', { name: labels.actions.receive })).toBeVisible();
}
