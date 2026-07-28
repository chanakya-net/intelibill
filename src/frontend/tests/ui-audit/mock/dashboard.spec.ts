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
import { SHELL_ENGLISH_VIEWPORTS, SHELL_LOCALE_VIEWPORTS } from '../fixtures/shell.fixture';
import {
  createDashboardScenario,
  filterDeclaredDashboardFailures,
  installDashboardFixture,
  type DashboardScenario,
} from '../fixtures/dashboard.fixture';
import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  waitForStablePage,
} from '../support/audit-page';

const MANAGEMENT_ROLES = [
  { role: 'Owner', offlineEnabled: true },
  { role: 'Manager', offlineEnabled: false },
] as const;
const BROWSER_TYPES = [chromium, firefox, webkit] as const;

test.describe('dashboard', () => {
  test('redirects staff to sales and renders dashboard for management roles', async ({}, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-mobile', 'representative role profiles');

    for (const profile of MANAGEMENT_ROLES) {
      await withDashboardPage(
        chromium,
        { width: 1440, height: 900 },
        createDashboardScenario(profile),
        async (page) => {
          await expect(page.locator('.dashboard-page')).toBeVisible();
          await expect(page).toHaveURL(/\/dashboard$/);
        },
      );
    }

    await withDashboardPage(
      chromium,
      { width: 1440, height: 900 },
      createDashboardScenario({ role: 'Staff' }),
      async (page) => {
        await expect(page).toHaveURL(/\/sales$/);
        await expect(page.locator('.dashboard-page')).toHaveCount(0);
      },
    );
  });

  test('renders populated dashboard without unexpected failures', async ({ page }) => {
    const scenario = createDashboardScenario();
    const collector = collectBrowserFailures(page);

    try {
      await openDashboard(page, scenario);
      await expect(page.locator('.dashboard-page')).toBeVisible();
      await expect(page.locator('.dashboard-hero h1')).toBeVisible();
      await expect(page.locator('.dashboard-hero__subtitle')).toBeVisible();
      await expect(page.locator('.dashboard-hero__period-pill')).toBeVisible();
      await expect(page.locator('.period-toggle__button')).toHaveCount(3);
      await expect(page.locator('.period-toggle__button.is-active')).toHaveCount(1);
      await expect(page.locator('.kpi-card')).toHaveCount(8);
      await expect(page.getByTestId('sales-revenue-card')).toBeVisible();
      await expect(page.getByTestId('expenses-kpi-value')).toBeVisible();
      await expect(page.locator('.dashboard-chart-card')).toHaveCount(2);
      await expect(page.locator('.dashboard-chart-card h2')).toHaveCount(2);
      await expect(page.locator('.dashboard-chart-card canvas')).toHaveCount(2);
      await expect(page.locator('.latest-sales-list__item')).toHaveCount(3);
      await expect(page.locator('.alerts-list__item')).toHaveCount(3);
      await expect(page.locator('.alerts-list__badge')).toHaveCount(3);
      await expect(page.locator('.dashboard-quick-actions__button')).toHaveCount(5);
      await assertChartCanvasesRendered(page);
      assertNoUnexpectedBrowserFailures(
        filterDeclaredDashboardFailures(collector.failures, scenario),
      );
    } finally {
      collector.dispose();
    }
  });

  test('renders loading, error, and empty dashboard states', async ({}, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-mobile', 'representative response states');

    await withDashboardPage(
      chromium,
      { width: 360, height: 800 },
      createDashboardScenario({ state: 'loading' }),
      async (page) => {
        await expect(page.locator('.dashboard-page__loading[aria-busy="true"]')).toBeVisible();
        await expect(page.locator('.dashboard-page__loading p-progressspinner')).toBeVisible();
        await expect(page.locator('.dashboard-quick-actions')).toHaveCount(0);
      },
    );

    await withDashboardPage(
      chromium,
      { width: 360, height: 800 },
      createDashboardScenario({ state: 'error' }),
      async (page) => {
        await expect(page.locator('.dashboard-page__error')).toHaveText(
          'Unable to load dashboard.',
        );
        await expect(page.locator('.dashboard-quick-actions__button')).toHaveCount(5);
        await assertDashboardBounds(page, 360);
      },
    );

    await withDashboardPage(
      chromium,
      { width: 360, height: 800 },
      createDashboardScenario({ state: 'empty' }),
      async (page) => {
        await expect(page.locator('.latest-sales-panel__empty')).toBeVisible();
        await expect(page.locator('.alerts-panel__empty')).toBeVisible();
        await expect(page.locator('.kpi-card')).toHaveCount(8);
        await expect(page.getByTestId('sales-revenue-value')).toContainText('₹0');
        await expect(page.locator('.dashboard-chart-card canvas')).toHaveCount(2);
        await assertChartCanvasesRendered(page);
        await assertDashboardBounds(page, 360);
      },
    );
  });

  test('keeps large values and long labels inside every English viewport', async ({}, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-mobile', 'manual browser and viewport matrix');
    test.setTimeout(240_000);

    for (const browserType of BROWSER_TYPES) {
      for (const viewport of SHELL_ENGLISH_VIEWPORTS) {
        await withDashboardPage(
          browserType,
          viewport,
          createDashboardScenario({ state: 'large' }),
          async (page) => {
            await assertDashboardBounds(page, viewport.width);
            await assertResponsiveGrids(page, viewport.width);
            await assertLongContentFits(page);
            await assertChartCanvasesRendered(page);
          },
        );
      }
    }
  });

  test('renders localized dashboards inside mobile and desktop widths', async ({}, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-mobile', 'manual locale matrix');
    test.setTimeout(240_000);

    for (const locale of SUPPORTED_LANGUAGES) {
      for (const viewport of SHELL_LOCALE_VIEWPORTS) {
        await withDashboardPage(
          chromium,
          viewport,
          createDashboardScenario({ state: 'long-label', locale }),
          async (page) => {
            await expect(page.locator('.kpi-label').first()).not.toHaveText(
              'dashboard.kpis.salesRevenue',
            );
            await expect(page.locator('.dashboard-chart-card h2').first()).not.toHaveText(
              'dashboard.charts.salesTrend',
            );
            await expect(page.locator('.dashboard-quick-actions__button').first()).not.toHaveText(
              'dashboard.quickActions.newSale',
            );
            await assertDashboardBounds(page, viewport.width);
            await assertResponsiveGrids(page, viewport.width);
            await assertLongContentFits(page);
          },
        );
        await withDashboardPage(
          chromium,
          viewport,
          createDashboardScenario({ state: 'empty', locale }),
          async (page) => {
            await expect(page.locator('.latest-sales-panel__empty')).not.toContainText(
              'dashboard.latestSales.',
            );
            await expect(page.locator('.alerts-panel__empty')).not.toHaveText(
              'dashboard.alerts.empty',
            );
            await assertDashboardBounds(page, viewport.width);
          },
        );
        await withDashboardPage(
          chromium,
          viewport,
          createDashboardScenario({ state: 'error', locale }),
          async (page) => {
            await expect(page.locator('.dashboard-page__error')).not.toHaveText(
              'dashboard.errors.loadFailed',
            );
            await assertDashboardBounds(page, viewport.width);
          },
        );
      }
    }
  });
});

async function withDashboardPage(
  browserType: BrowserType,
  viewport: { readonly width: number; readonly height: number },
  scenario: DashboardScenario,
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
  const collector = collectBrowserFailures(page);

  try {
    await openDashboard(page, scenario);
    await assertion(page);
    assertNoUnexpectedBrowserFailures(
      filterDeclaredDashboardFailures(collector.failures, scenario),
    );
  } finally {
    collector.dispose();
    await context.close();
    await browser.close();
  }
}

async function openDashboard(page: Page, scenario: DashboardScenario): Promise<void> {
  await installDashboardFixture(page, scenario);
  await page.clock.setFixedTime(new Date('2026-07-29T09:30:00+05:30'));
  await page.goto('/dashboard');
  await waitForStablePage(page);
}

async function assertDashboardBounds(page: Page, viewportWidth: number): Promise<void> {
  const metrics = await page.evaluate(() => {
    const dashboard = document.querySelector<HTMLElement>('.dashboard-page');
    const outside = dashboard
      ? [dashboard, ...dashboard.querySelectorAll<HTMLElement>('*')]
          .filter((element) => {
            const box = element.getBoundingClientRect();
            return box.left < -1 || box.right > innerWidth + 1;
          })
          .slice(0, 20)
          .map((element) => ({
            element: `${element.tagName.toLowerCase()}.${element.className}`,
            right: Math.round(element.getBoundingClientRect().right),
            scrollWidth: element.scrollWidth,
            clientWidth: element.clientWidth,
          }))
      : ['dashboard missing'];

    return { documentWidth: document.documentElement.scrollWidth, outside };
  });

  expect(metrics).toEqual({
    documentWidth: expect.any(Number),
    outside: [],
  });
  expect(metrics.documentWidth).toBeLessThanOrEqual(viewportWidth);
}

async function assertResponsiveGrids(page: Page, viewportWidth: number): Promise<void> {
  const expected = {
    kpis: viewportWidth <= 640 ? 1 : viewportWidth <= 1100 ? 2 : 4,
    charts: viewportWidth <= 960 ? 1 : 2,
    panels: viewportWidth <= 1100 ? 1 : 2,
    quickActions: viewportWidth <= 640 ? 1 : viewportWidth <= 1100 ? 2 : 5,
  };
  const actual = await page.evaluate(() => {
    const columns = (selector: string) =>
      getComputedStyle(document.querySelector<HTMLElement>(selector)!)
        .gridTemplateColumns.split(' ')
        .filter(Boolean).length;
    return {
      kpis: columns('.kpi-cards'),
      charts: columns('.dashboard-charts'),
      panels: columns('.dashboard-panels'),
      quickActions: columns('.dashboard-quick-actions'),
    };
  });

  expect(actual).toEqual(expected);
}

async function assertLongContentFits(page: Page): Promise<void> {
  const overflow = await page.evaluate(() => {
    const selectors = [
      '.kpi-value',
      '.kpi-change',
      '.dashboard-hero h1',
      '.dashboard-hero__period-pill',
      '.latest-sales-list__customer',
      '.alerts-list__badge',
      '.alerts-list__message',
      '.dashboard-quick-actions__button .p-button-label',
    ];

    return selectors.flatMap((selector) =>
      [...document.querySelectorAll<HTMLElement>(selector)]
        .filter((element) => element.scrollWidth > element.clientWidth + 1)
        .map(() => selector),
    );
  });

  expect(overflow).toEqual([]);
  const invoicesTruncateInsideViewport = await page
    .locator('.latest-sales-list__invoice')
    .evaluateAll((elements) =>
      elements.every((element) => {
        const box = element.getBoundingClientRect();
        const style = getComputedStyle(element);
        return (
          box.left >= -1 &&
          box.right <= innerWidth + 1 &&
          style.overflow === 'hidden' &&
          style.textOverflow === 'ellipsis'
        );
      }),
    );
  expect(invoicesTruncateInsideViewport).toBe(true);
}

async function assertChartCanvasesRendered(page: Page): Promise<void> {
  const canvases = await page.locator('.dashboard-chart-card canvas').evaluateAll((elements) =>
    elements.map((element) => {
      const canvas = element as HTMLCanvasElement;
      const box = canvas.getBoundingClientRect();
      return { width: box.width, height: box.height, pixels: canvas.toDataURL().length };
    }),
  );

  expect(canvases).toHaveLength(2);
  for (const canvas of canvases) {
    expect(canvas.width).toBeGreaterThan(0);
    expect(canvas.height).toBeGreaterThan(0);
    expect(canvas.pixels).toBeGreaterThan(100);
  }
}
