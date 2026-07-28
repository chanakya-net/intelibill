import { expect, test, type Browser, type Page, type Route } from '@playwright/test';

import type {
  ProfitLossReportItemDto,
  ProfitLossReportResultDto,
} from '../../../src/app/features/sales/services/sale.models';
import { SUPPORTED_LANGUAGES } from '../../../src/app/core/i18n/language.constants';
import {
  createShellScenario,
  installShellFixture,
  type ShellRole,
} from '../fixtures/shell.fixture';
import { waitForStablePage } from '../support/audit-page';
const ROUTE = '/sales/profit-loss';
const API_PATH = '/api/sales/profit-loss';
type ReportState = 'ready' | 'empty' | 'loading' | 'error';
const EMPTY_REPORT: ProfitLossReportResultDto = {
  items: [],
  totalCount: 0,
  pageNumber: 1,
  pageSize: 20,
  summary: {
    netProfitAfterTax: 0,
    revenueIncludingTax: 0,
    totalCost: 0,
    averageMarginPercent: null,
    invoiceCount: 0,
    returnCount: 0,
    adjustmentCount: 0,
  },
  appliedFilters: {
    from: '2026-07-23',
    to: '2026-07-29',
    type: 'all',
    search: null,
    pageNumber: 1,
    pageSize: 20,
  },
};
const POPULATED_REPORT: ProfitLossReportResultDto = {
  ...EMPTY_REPORT,
  items: [
    reportItem({
      referenceNumber: 'INV-VERY-LONG-REFERENCE-2026-000000000000000001',
      partyName:
        'Alexandria Cassandra International Wholesale and Retail Customer Partnership',
      revenueAfterTax: 1234567890.5,
      profitBeforeTax: 345678901.25,
      profitAfterTax: 300000000.75,
      marginPercent: 24.3,
    }),
    reportItem({
      saleId: 'sale-return',
      referenceNumber: 'RET-0002',
      rowType: 'SaleReturn',
      totalCost: -234567890.5,
      wastageCost: -4567890.25,
      revenueAfterTax: -987654321.25,
      profitBeforeTax: -753086430.75,
      profitAfterTax: -800000000.5,
      marginPercent: -81,
    }),
    reportItem({
      saleId: null,
      inventoryAdjustmentId: 'adjustment-1',
      referenceNumber: 'ADJ-0003',
      rowType: 'InventoryAdjustment',
      partyName: null,
      revenueAfterTax: 0,
      profitBeforeTax: -1234.5,
      profitAfterTax: -1234.5,
      marginPercent: null,
    }),
  ],
  totalCount: 63,
  summary: {
    netProfitAfterTax: -500001234.25,
    revenueIncludingTax: 1234567890.5,
    totalCost: 987654321.25,
    averageMarginPercent: -18.57,
    invoiceCount: 37,
    returnCount: 19,
    adjustmentCount: 7,
  },
};

test.describe('profit-loss', () => {
  test('allows management roles and redirects staff', async ({ browser }, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-mobile', 'representative guard outcomes');
    for (const role of ['Owner', 'Manager'] as const) {
      await withAuditPage(browser, { role }, async (page) => {
        await expect(page).toHaveURL(/\/sales\/profit-loss$/);
        await expect(page.locator('.profit-loss-page')).toBeVisible();
      });
    }
    await withAuditPage(browser, { role: 'Staff' }, async (page) => {
      await expect(page).toHaveURL(/\/sales$/);
      await expect(page.locator('.profit-loss-page')).toHaveCount(0);
    });
  });

  test('applies and clears report date filters', async ({ page }) => {
    const requests: URL[] = [];
    await installProfitLossScenario(page, 'Owner', requests, () => 'ready');
    await page.clock.setFixedTime(new Date('2026-07-29T09:30:00+05:30'));
    const initialResponse = waitForReportRequest(page, {
      from: '2026-07-23',
      to: '2026-07-29',
      page: '1',
      pageSize: '20',
    });
    await page.goto(ROUTE);
    await initialResponse;
    await waitForStablePage(page);
    await expect(page.locator('.period-pill')).toContainText(/23 Jul, 2026.*29 Jul, 2026/);
    const pageTwoResponse = waitForReportRequest(page, { page: '2' });
    await page.getByRole('button', { name: 'Next' }).click();
    await pageTwoResponse;
    await expect(page.locator('.page-status')).toContainText('Page 2 of 4');
    const fromResponse = waitForReportRequest(page, { from: '2026-07-10', page: '1' });
    await selectDate(page, 0, 10);
    await fromResponse;
    const toResponse = waitForReportRequest(page, {
      from: '2026-07-10',
      to: '2026-07-20',
      page: '1',
    });
    await selectDate(page, 1, 20);
    await toResponse;
    const typeResponse = waitForReportRequest(page, { type: 'saleReturn', page: '1' });
    await page.locator('p-select.profit-loss-type').click();
    await page.locator('.p-select-overlay:visible').getByText('Sale return', { exact: true }).click();
    await typeResponse;
    const searchResponse = waitForReportRequest(page, { search: 'alexandria', page: '1' });
    await page.locator('.search-field input').fill('alexandria');
    await searchResponse;

    const clearResponse = waitForReportRequest(page, {
      from: '2026-07-23',
      to: '2026-07-29',
      type: null,
      search: null,
      page: '1',
      pageSize: '20',
    });
    await page.locator('.clear-filters-btn').click();
    await clearResponse;
    await expect(page.locator('.search-field input')).toHaveValue('');
    expect(requests.filter((url) => url.searchParams.get('search') === 'alexandria')).toHaveLength(
      1,
    );
  });
  test('renders populated and large reports responsively', async ({ page }) => {
    await installProfitLossScenario(page, 'Owner', [], () => 'ready');
    await page.clock.setFixedTime(new Date('2026-07-29T09:30:00+05:30'));
    await page.goto(ROUTE);
    await waitForStablePage(page);
    const pageRoot = page.locator('.profit-loss-page');
    await expect(pageRoot.locator('.kpi-card')).toHaveCount(4);
    await expect(pageRoot).toContainText('-₹50,00,01,234.25');
    await expect(pageRoot).toContainText('₹1,23,45,67,890.50');
    await expect(pageRoot).not.toContainText('₹-');
    await expect(pageRoot.locator('.totals-grid')).toContainText(/37.*19.*7/);
    if (page.viewportSize()!.width > 1024) {
      const table = page.locator('.desktop-table');
      await expect(table).toBeVisible();
      await expect(table.locator('th')).toHaveCount(10);
      await expect(table).toHaveAttribute('tabindex', '0');
      await expect(table).toContainText('INV-VERY-LONG-REFERENCE-2026-000000000000000001');
    } else {
      const cards = page.locator('.mobile-report-card');
      await expect(cards).toHaveCount(3);
      for (const label of [
        'Rev (Incl Tax)',
        'Total Cost',
        'Wastage Cost',
        'Profit (Before Tax)',
        'Profit (After Tax)',
        'Margin',
      ]) {
        await expect(cards.first()).toContainText(label);
      }
      await expect(cards.nth(1)).toContainText('-₹80,00,00,000.50');
    }
    await assertPageBounds(page);
  });

  test('presents loading, empty, and error feedback', async ({ page }) => {
    await installProfitLossScenario(page, 'Owner', [], () => 'loading');
    await page.goto(ROUTE);
    await expect(page.locator('.loading-state[role="status"][aria-busy="true"]')).toBeVisible();
    await expect(page.locator('.clear-filters-btn')).toBeDisabled();
    await expect(page.locator('[data-qa="profit-loss-export"]')).toBeDisabled();
    await expect(page.locator('.report-footer')).toHaveCount(0);
    await page.unrouteAll({ behavior: 'ignoreErrors' });
    await installProfitLossScenario(page, 'Owner', [], () => 'empty');
    await page.reload();
    await expect(page.locator('.empty-state:visible, .mobile-empty-state:visible')).toBeVisible();
    await expect(
      page.locator('.empty-state:visible h3, .mobile-empty-state:visible h3'),
    ).toHaveText('No P&L records');

    let state: ReportState = 'ready';
    await page.unrouteAll({ behavior: 'ignoreErrors' });
    await installProfitLossScenario(page, 'Owner', [], () => state);
    await page.reload();
    await expect(page.locator('.kpi-value').first()).toContainText('-₹50,00,01,234.25');
    state = 'error';
    const failed = waitForReportRequest(page, { search: 'failure' });
    await page.locator('.search-field input').fill('failure');
    await failed;
    await expect(page.getByRole('alert')).toContainText('Unable to load profit and loss report.');
    await expect(page.locator('.kpi-row, .report-content, .report-footer')).toHaveCount(0);
    await expect(page.locator('[data-qa="profit-loss-export"]')).toBeDisabled();
  });

  test('keeps every locale within audit viewport bounds', async ({ browser }, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-mobile', 'representative locale matrix');
    test.setTimeout(120_000);
    for (const locale of SUPPORTED_LANGUAGES) {
      await withAuditPage(
        browser,
        { locale, viewport: { width: 360, height: 800 }, state: 'ready' },
        async (page) => {
          for (const viewport of [{ width: 360, height: 800 }, { width: 1440, height: 900 }]) {
            await page.setViewportSize(viewport);
            await expect(page.locator('.profit-loss-page')).not.toContainText('sales.profitLoss.');
            await assertPageBounds(page);
          }
        },
      );
    }
  });
});

async function withAuditPage(
  browser: Browser,
  options: {
    role?: ShellRole;
    locale?: (typeof SUPPORTED_LANGUAGES)[number];
    viewport?: { width: number; height: number };
    state?: ReportState;
  },
  assertion: (page: Page) => Promise<void>,
): Promise<void> {
  const locale = options.locale ?? 'en-IN';
  const context = await browser.newContext({
    baseURL: 'http://127.0.0.1:4300',
    locale,
    timezoneId: 'Asia/Kolkata',
    viewport: options.viewport ?? { width: 1440, height: 900 },
    serviceWorkers: 'block',
  });
  const page = await context.newPage();
  try {
    await installProfitLossScenario(
      page,
      options.role,
      [],
      () => options.state ?? 'empty',
      locale,
    );
    await page.clock.setFixedTime(new Date('2026-07-29T09:30:00+05:30'));
    await page.goto(ROUTE);
    await waitForStablePage(page);
    await assertion(page);
  } finally {
    await context.close();
  }
}

async function installProfitLossScenario(
  page: Page,
  role: ShellRole = 'Owner',
  requests: URL[] = [],
  getState: () => ReportState = () => 'empty',
  locale: (typeof SUPPORTED_LANGUAGES)[number] = 'en-IN',
): Promise<void> {
  const shell = createShellScenario({ role, locale, longLabels: true });
  await installShellFixture(page, {
    ...shell,
    session: {
      ...shell.session,
      accessTokenExpiresAt: '2026-07-30T04:00:00.000Z',
      refreshTokenExpiresAt: '2026-08-29T04:00:00.000Z',
    },
  });
  await page.route(`**${API_PATH}?**`, async (route) => {
    const url = new URL(route.request().url());
    requests.push(url);
    const state = getState();
    if (state === 'loading') return new Promise<void>(() => undefined);
    if (state === 'error') {
      await route.fulfill({
        status: 503,
        contentType: 'application/json',
        body: JSON.stringify({ detail: 'Unable to load profit and loss report.' }),
      });
      return;
    }
    await fulfillReport(route, reportFor(url, state === 'ready' ? POPULATED_REPORT : EMPTY_REPORT));
  });
}

function reportFor(url: URL, source = EMPTY_REPORT): ProfitLossReportResultDto {
  const pageNumber = Number(url.searchParams.get('page') ?? 1);
  const pageSize = Number(url.searchParams.get('pageSize') ?? 20);
  return {
    ...source,
    pageNumber,
    pageSize,
    appliedFilters: {
      from: url.searchParams.get('from') ?? EMPTY_REPORT.appliedFilters.from,
      to: url.searchParams.get('to') ?? EMPTY_REPORT.appliedFilters.to,
      type:
        (url.searchParams.get('type') as ProfitLossReportResultDto['appliedFilters']['type']) ??
        'all',
      search: url.searchParams.get('search'),
      pageNumber,
      pageSize,
    },
  };
}

function reportItem(overrides: Partial<ProfitLossReportItemDto>): ProfitLossReportItemDto {
  return {
    saleId: 'sale-1',
    referenceNumber: 'INV-0001',
    occurredAt: '2026-07-29T05:00:00.000Z',
    partyName: 'Audit Customer',
    totalCost: 890123456.75,
    wastageCost: 1234567.5,
    revenueBeforeTax: 1100000000,
    revenueAfterTax: 1200000000,
    profitBeforeTax: 209876543.25,
    profitAfterTax: 190000000,
    marginPercent: 15.8,
    rowType: 'Sale',
    inventoryAdjustmentId: null,
    ...overrides,
  };
}

function waitForReportRequest(
  page: Page,
  expected: Readonly<Record<string, string | null>>,
): Promise<import('@playwright/test').Response> {
  return page.waitForResponse((response) => {
    const url = new URL(response.url());
    return (
      url.pathname === API_PATH &&
      Object.entries(expected).every(([key, value]) => url.searchParams.get(key) === value)
    );
  });
}

async function selectDate(page: Page, index: number, day: number): Promise<void> {
  const input = page.getByLabel(index === 0 ? 'From date' : 'To date');
  await input.click();
  await page
    .locator('.p-datepicker-panel:visible .p-datepicker-day')
    .getByText(String(day), { exact: true })
    .click();
  await page.locator('.profit-loss-hero h1').click();
}

async function fulfillReport(route: Route, report: ProfitLossReportResultDto): Promise<void> {
  await route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(report),
  });
}

async function assertPageBounds(page: Page): Promise<void> {
  const metrics = await page.evaluate(() => {
    const root = document.querySelector<HTMLElement>('.profit-loss-page');
    const tableScroller = document.querySelector<HTMLElement>('.desktop-table');
    const outside = root
      ? [root, ...root.querySelectorAll<HTMLElement>('*')].filter(
          (element) => {
            if (element !== tableScroller && element.closest('.desktop-table')) return false;
            const bounds = element.getBoundingClientRect();
            return bounds.left < -1 || bounds.right > innerWidth + 1;
          },
        )
      : ['missing'];
    const tableBounds = tableScroller?.getBoundingClientRect();
    return {
      documentWidth: document.documentElement.scrollWidth,
      outside: outside.length,
      tableInside:
        !tableScroller ||
        (!!tableBounds && tableBounds.left >= -1 && tableBounds.right <= innerWidth + 1),
    };
  });
  expect(metrics.documentWidth).toBeLessThanOrEqual(page.viewportSize()!.width);
  expect(metrics.outside).toBe(0);
  expect(metrics.tableInside).toBe(true);
}
