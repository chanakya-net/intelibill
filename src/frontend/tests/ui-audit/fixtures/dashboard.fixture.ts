import type { Page, Route } from '@playwright/test';

import type { SupportedLanguage } from '../../../src/app/core/i18n/language.constants';
import type { DashboardDto } from '../../../src/app/features/dashboard/services/dashboard.models';
import type { BrowserFailure } from '../support/audit-page';
import {
  createShellScenario,
  filterDeclaredShellFailures,
  installShellFixture,
  type ShellRole,
  type ShellScenario,
} from './shell.fixture';

const API_BASE = 'http://localhost:5277/api';
const DASHBOARD_PATH = '/dashboard';

export type DashboardState = 'normal' | 'empty' | 'large' | 'long-label' | 'loading' | 'error';

export interface DashboardScenario {
  readonly state: DashboardState;
  readonly shell: ShellScenario;
  readonly dashboard: DashboardDto | null;
  readonly errorStatus: number | null;
}

export interface DashboardScenarioOptions {
  readonly state?: DashboardState;
  readonly role?: ShellRole;
  readonly locale?: SupportedLanguage;
  readonly offlineEnabled?: boolean;
}

export function createDashboardScenario(options: DashboardScenarioOptions = {}): DashboardScenario {
  const state = options.state ?? 'normal';
  const longLabels = state === 'long-label' || state === 'large';
  const baseShell = createShellScenario({
    role: options.role,
    locale: options.locale,
    offlineEnabled: options.offlineEnabled,
    longLabels,
  });
  const shell = {
    ...baseShell,
    session: {
      ...baseShell.session,
      accessTokenExpiresAt: '2026-08-10T00:00:00.000Z',
      refreshTokenExpiresAt: '2026-08-20T00:00:00.000Z',
    },
  };

  return {
    state,
    shell,
    dashboard: state === 'loading' || state === 'error' ? null : createDashboard(state),
    errorStatus: state === 'error' ? 500 : null,
  };
}

export async function installDashboardFixture(
  page: Page,
  scenario: DashboardScenario,
): Promise<void> {
  await installShellFixture(page, scenario.shell);
  await page.route(`${API_BASE}/dashboard**`, async (route) => {
    await handleDashboardRoute(route, scenario);
  });
}

export function filterDeclaredDashboardFailures(
  failures: readonly BrowserFailure[],
  scenario: DashboardScenario,
): BrowserFailure[] {
  const dashboardFailures = failures.filter((failure) => {
    if (
      scenario.state === 'loading' &&
      failure.kind === 'request' &&
      failure.url?.startsWith(`${API_BASE}${DASHBOARD_PATH}`) &&
      failure.message.includes('ABORTED')
    ) {
      return false;
    }

    if (scenario.errorStatus === null) {
      return true;
    }

    if (
      failure.kind === 'response' &&
      failure.url?.startsWith(`${API_BASE}${DASHBOARD_PATH}`) &&
      failure.message === `HTTP ${scenario.errorStatus}`
    ) {
      return false;
    }

    return !(
      failure.kind === 'console' &&
      (failure.message.includes(`status of ${scenario.errorStatus}`) ||
        failure.message.includes(`status: ${scenario.errorStatus}`))
    );
  });

  return filterDeclaredShellFailures(dashboardFailures, scenario.shell);
}

async function handleDashboardRoute(route: Route, scenario: DashboardScenario): Promise<void> {
  if (scenario.state === 'loading') {
    await new Promise<void>(() => undefined);
    return;
  }

  if (scenario.errorStatus !== null) {
    await route.fulfill({
      status: scenario.errorStatus,
      contentType: 'application/json',
      body: JSON.stringify({ title: 'UiAudit.DeclaredDashboardError' }),
    });
    return;
  }

  await route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(scenario.dashboard),
  });
}

function createDashboard(state: Exclude<DashboardState, 'loading' | 'error'>): DashboardDto {
  const empty = state === 'empty';
  const large = state === 'large';
  const longLabels = state === 'long-label' || large;
  const value = large ? 9_876_543_210_123 : 128_450;

  return {
    generatedAt: '2026-07-29T09:30:00.000Z',
    startDate: '2026-07-01',
    endDate: '2026-07-29',
    salesCount: empty ? 0 : large ? 1_234_567_890_123 : 48,
    salesRevenue: empty ? 0 : value,
    hasNoSalesActivity: empty,
    customerCreditDue: empty ? 0 : value - 12_345,
    salesBooked: empty ? 0 : value,
    netSalesBooked: empty ? 0 : value - 10_000,
    wastageCost: empty ? 0 : large ? 123_456_789_012 : 2_700,
    cashCollected: empty ? 0 : value - 25_000,
    profitBeforeTax: empty ? 0 : value / 3,
    profitAfterTax: empty ? 0 : value / 4,
    netProfit: empty ? 0 : value / 4,
    netProfitChangePercent: empty ? null : large ? 987_654_321.9 : 12.5,
    expenseRecorded: empty ? 0 : value / 8,
    expenseCorrection: 0,
    netExpense: empty ? 0 : value / 8,
    supplierPayables: empty ? 0 : value - 33_333,
    creditSalesAmount: empty ? 0 : value / 5,
    creditSalesPercentage: empty ? 0 : 20,
    paymentMix: empty ? null : { cash: 50, upi: 25, card: 15, credit: 10 },
    creditNoteSettlementAmount: 0,
    creditShareWarning: false,
    runningLowStockCount: empty ? 0 : 7,
    lowStockItemCount: empty ? 0 : large ? 1_234_567_890_123 : 7,
    criticalStockCount: empty ? 0 : 2,
    rankedShortageList: [],
    highestDueCustomer: null,
    topFiveDueCustomers: [],
    alerts: empty ? [] : createAlerts(longLabels),
    salesTrendSeries: empty ? [] : createTrend(value),
    revenueVsExpenses: empty ? [] : createRevenueVsExpenses(value),
    profitTrendSeries: [],
    paymentMixTrendSeries: [],
    previousPeriodSummary: null,
    latestSales: empty ? [] : createLatestSales(value, longLabels),
    stockValue: empty ? 0 : value + 987_654,
  };
}

function createTrend(value: number): DashboardDto['salesTrendSeries'] {
  return Array.from({ length: 7 }, (_, index) => ({
    date: `2026-07-${String(index + 23).padStart(2, '0')}`,
    amount: Math.round(value * ((index + 4) / 10)),
    netAmount: Math.round(value * ((index + 3) / 10)),
  }));
}

function createRevenueVsExpenses(value: number): NonNullable<DashboardDto['revenueVsExpenses']> {
  return Array.from({ length: 7 }, (_, index) => ({
    date: `2026-07-${String(index + 23).padStart(2, '0')}`,
    revenue: Math.round(value * ((index + 4) / 10)),
    expenses: Math.round(value * ((index + 1) / 20)),
  }));
}

function createLatestSales(value: number, longLabels: boolean): DashboardDto['latestSales'] {
  const invoice = longLabels
    ? 'INV-2026-ULTRA-LONG-UNBROKEN-INVOICE-IDENTIFIER-00000000000000000001'
    : 'INV-2026-0048';
  const customer = longLabels
    ? 'Alexandria-Cassandra International Wholesale Customer Collective'
    : 'Ananya Traders';

  return Array.from({ length: 3 }, (_, index) => ({
    saleId: `sale-${index + 1}`,
    invoiceNumber: `${invoice}-${index + 1}`,
    customerDisplayName: `${customer} ${index + 1}`,
    soldAt: `2026-07-29T0${index + 7}:30:00.000Z`,
    totalAmount: Math.round(value / (index + 5)),
  }));
}

function createAlerts(longLabels: boolean): DashboardDto['alerts'] {
  const suffix = longLabels
    ? ' — Immediate replenishment required for the international warehouse overflow location'
    : '';

  return [
    {
      alertType: 'LowStock',
      priority: 1,
      title: longLabels ? 'ExtremelyLongUnbrokenLowStockClassificationLabel' : 'Low stock',
      message: `Premium basmati rice is below its reorder level${suffix}.`,
      actionLabel: 'View',
      actionRoute: '/items',
    },
    {
      alertType: 'ExpiringBatch',
      priority: 2,
      title: 'Expiring batch',
      message: `Three batches expire this week${suffix}.`,
      actionLabel: 'View',
      actionRoute: '/inventory/batches',
    },
    {
      alertType: 'PendingPurchaseOrder',
      priority: 3,
      title: 'Pending purchase order',
      message: `Purchase order approval remains outstanding${suffix}.`,
      actionLabel: 'View',
      actionRoute: '/inventory/purchase-orders',
    },
  ];
}
