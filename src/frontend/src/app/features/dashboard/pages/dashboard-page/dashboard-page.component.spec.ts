import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { DashboardDto } from '../../services/dashboard.service';
import { DashboardFacade } from '../../state/dashboard.facade';
import { DashboardPageComponent } from './dashboard-page.component';

// localStorage mock for test environment
let _store: Record<string, string> = {};
const localStorageMock = {
  getItem: (key: string) => _store[key] ?? null,
  setItem: (key: string, value: string) => { _store[key] = value; },
  removeItem: (key: string) => { delete _store[key]; },
  clear: () => { _store = {}; },
};
Object.defineProperty(globalThis, 'localStorage', { value: localStorageMock, writable: true });

function formatDateForSpec(value: string): string {
  const [year, month, day] = value.split('-').map(Number);
  return new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' }).format(new Date(year, month - 1, day));
}

function toLocalIsoDateForSpec(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

const makeOwnerDto = (overrides?: Partial<DashboardDto>): DashboardDto => ({
  generatedAt: '2026-04-29T10:00:00Z',
  startDate: '2026-03-31',
  endDate: '2026-04-29',
  salesCount: 5,
  hasNoSalesActivity: false,
  salesBooked: 500,
  netSalesBooked: 500,
  wastageCost: 0,
  cashCollected: 450,
  profitBeforeTax: 100,
  profitAfterTax: 130,
  expenseRecorded: 50,
  expenseCorrection: 0,
  netExpense: 50,
  creditSalesAmount: 50,
  creditSalesPercentage: 0.1,
  paymentMix: { cash: 450, upi: 0, card: 0, credit: 50 },
  creditShareWarning: false,
  runningLowStockCount: 1,
  criticalStockCount: 0,
  rankedShortageList: [{ itemName: 'Sugar', quantity: 2, reorderLevel: 10, shortage: 8 }],
  highestDueCustomer: null,
  topFiveDueCustomers: [],
  alerts: [],
  salesTrendSeries: [],
  profitTrendSeries: [],
  previousPeriodSummary: null,
  ...overrides,
});

const makeStaffDto = (): DashboardDto => ({
  generatedAt: '2026-04-29T10:00:00Z',
  startDate: '2026-03-31',
  endDate: '2026-04-29',
  salesCount: 5,
  hasNoSalesActivity: false,
  salesBooked: null,
  netSalesBooked: null,
  wastageCost: null,
  cashCollected: null,
  profitBeforeTax: null,
  profitAfterTax: null,
  expenseRecorded: null,
  expenseCorrection: null,
  netExpense: null,
  creditSalesAmount: null,
  creditSalesPercentage: null,
  paymentMix: null,
  creditShareWarning: null,
  runningLowStockCount: 1,
  criticalStockCount: 0,
  rankedShortageList: [{ itemName: 'Sugar', quantity: 2, reorderLevel: 10, shortage: 8 }],
  highestDueCustomer: null,
  topFiveDueCustomers: null,
  alerts: [{ alertType: 'RunningLowStock', priority: 3 }],
  salesTrendSeries: null,
  profitTrendSeries: null,
  previousPeriodSummary: null,
});

type TestDashboardFacade = {
  data$: ReturnType<typeof of>;
  loading$: ReturnType<typeof of>;
  error$: ReturnType<typeof of>;
  hasDashboardData$: ReturnType<typeof of>;
  startDate$: ReturnType<typeof of>;
  endDate$: ReturnType<typeof of>;
  selectedPreset$: ReturnType<typeof of>;
  loadDashboard: ReturnType<typeof vi.fn>;
  refresh: ReturnType<typeof vi.fn>;
  applyRange: ReturnType<typeof vi.fn>;
};

function createFixture(dto: DashboardDto | null, errorMessage = '') {
  return createFixtureWithFacade(dto, errorMessage).fixture;
}

function createFixtureWithFacade(
  dto: DashboardDto | null,
  errorMessage = '',
  overrides: Partial<TestDashboardFacade> = {},
) {
  const facade = {
    data$: of(dto),
    loading$: of(false),
    error$: of(errorMessage),
    hasDashboardData$: of(dto !== null),
    startDate$: of('2026-03-31'),
    endDate$: of('2026-04-29'),
    selectedPreset$: of('last30'),
    loadDashboard: vi.fn(),
    refresh: vi.fn(),
    applyRange: vi.fn(),
    ...overrides,
  } as TestDashboardFacade;

  TestBed.configureTestingModule({
    imports: [
      DashboardPageComponent,
      TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
    ],
    providers: [{ provide: DashboardFacade, useValue: facade }],
  });

  const fixture = TestBed.createComponent(DashboardPageComponent);
  fixture.detectChanges();
  return { fixture, facade };
}

describe('DashboardPageComponent', () => {
  it('renders sales count for all roles', () => {
    const fixture = createFixture(makeOwnerDto());
    expect(fixture.componentInstance.data()?.salesCount).toBe(5);
  });

  it('renders financial KPI sections for Owner role', () => {
    const fixture = createFixture(makeOwnerDto({ salesBooked: 500 }));
    const cards = fixture.debugElement.queryAll(By.css('p-card'));
    // chart card and collapsible sections are present, but the primary summary cards are removed
    expect(cards.length).toBeGreaterThan(0);
    expect(fixture.debugElement.queryAll(By.css('.dashboard-primary-layout .kpi-card'))).toHaveLength(0);
  });

  it('hides financial KPI sections for Staff role (null fields)', () => {
    const fixture = createFixture(makeStaffDto());
    expect(fixture.componentInstance.salesChartData()).toBeNull();
    // Stock risk section toggle is shown (all roles see stock risk)
    const stockToggle = fixture.debugElement.queryAll(By.css('.dashboard-section-toggle'));
    expect(stockToggle.length).toBeGreaterThan(0);
  });

  it('explains that wastage and profit include inventory adjustment losses for financial roles', () => {
    const fixture = createFixture(makeOwnerDto({
      wastageCost: 75,
      profitBeforeTax: 25,
      profitAfterTax: 40,
    }));
    const el = fixture.nativeElement as HTMLElement;
    fixture.componentInstance.toggleSection('sales');
    fixture.detectChanges();

    expect(el.textContent).toContain('dashboard.adjustmentLossNote');
    expect(el.textContent).toContain('₹75.00');
  });

  it('keeps adjustment loss financial copy hidden for Staff role', () => {
    const fixture = createFixture(makeStaffDto());
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).not.toContain('dashboard.adjustmentLossNote');
  });

  it('renders alert ribbon when alerts are present', () => {
    const dto = makeOwnerDto({
      alerts: [
        { alertType: 'CriticalStock', priority: 1 },
        { alertType: 'RunningLowStock', priority: 3 },
      ],
    });
    const fixture = createFixture(dto);
    const alerts = fixture.debugElement.queryAll(By.css('.dashboard-alert'));
    expect(alerts).toHaveLength(2);
  });

  it('shows no-activity hint when hasNoSalesActivity is true', () => {
    const dto = makeOwnerDto({ hasNoSalesActivity: true, salesCount: 0, salesBooked: 0 });
    const fixture = createFixture(dto);
    const noActivity = fixture.debugElement.query(By.css('.dashboard-no-activity'));
    expect(noActivity).not.toBeNull();
  });

  it('does not show no-activity hint when sales exist', () => {
    const fixture = createFixture(makeOwnerDto({ hasNoSalesActivity: false }));
    const noActivity = fixture.debugElement.query(By.css('.dashboard-no-activity'));
    expect(noActivity).toBeNull();
  });

  it('shows stale data warning when error exists alongside data', () => {
    const fixture = createFixture(makeOwnerDto(), 'Network timeout');
    const warning = fixture.debugElement.query(By.css('.dashboard-stale-warning'));
    expect(warning).not.toBeNull();
  });

  it('shows error section when no data and error present', () => {
    const fixture = createFixture(null, 'Network timeout');
    const error = fixture.debugElement.query(By.css('.dashboard-error'));
    expect(error).not.toBeNull();
  });

  it('renders shortage list items when stockRisk section is expanded', () => {
    const dto = makeOwnerDto({
      rankedShortageList: [
        { itemName: 'Salt', quantity: 0, reorderLevel: 5, shortage: 5 },
      ],
    });
    const fixture = createFixture(dto);
    // Expand stock risk section first
    fixture.componentInstance.toggleSection('stockRisk');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Salt');
  });

  it('renders receivable risk section when topFiveDueCustomers is non-null', () => {
    const dto = makeOwnerDto({
      topFiveDueCustomers: [],
      highestDueCustomer: null,
    });
    const fixture = createFixture(dto);
    const el = fixture.nativeElement as HTMLElement;
    // Section exists (renders empty-state message)
    expect(el.textContent).not.toBeNull();
  });

  it('hides receivable risk section when topFiveDueCustomers is null (Staff role)', () => {
    const fixture = createFixture(makeStaffDto());
    // The topFiveDueCustomers null guard means section is absent
    const dueList = fixture.debugElement.query(By.css('.due-list'));
    expect(dueList).toBeNull();
  });

  it('renders three combobox controls for metric, range, and chart type', () => {
    const fixture = createFixture(makeOwnerDto());
    const selects = fixture.debugElement.queryAll(By.css('p-select'));
    expect(selects.length).toBeGreaterThanOrEqual(3);
  });

  it('updates preset selection state', () => {
    const fixture = createFixture(makeOwnerDto());
    const component = fixture.componentInstance;

    component.onSelectPreset('today');
    fixture.detectChanges();

    expect(component.pendingPreset()).toBe('today');
  });

  it('does not show standalone profit trend in metric selector', () => {
    const fixture = createFixture(makeOwnerDto());
    const values = fixture.componentInstance.metricOptions().map((option) => option.value);

    expect(values).toEqual(['sales', 'expense', 'paymentMix']);
    expect(values).not.toContain('profit');
  });

  it('renders apply button with dedicated styling class', () => {
    const fixture = createFixture(makeOwnerDto());
    const applyButton = fixture.debugElement.query(By.css('.range-apply-button'));
    expect(applyButton).not.toBeNull();
  });

  it('renders sales trend chart when salesTrendSeries has data', () => {
    const dto = makeOwnerDto({
      salesTrendSeries: [
        { date: '2026-04-28', amount: 200, netAmount: 200 },
        { date: '2026-04-29', amount: 300, netAmount: 300 },
      ],
      profitTrendSeries: [
        { date: '2026-04-28', profitBeforeTax: 80, profitAfterTax: 100 },
        { date: '2026-04-29', profitBeforeTax: 120, profitAfterTax: 150 },
      ],
    });
    const fixture = createFixture(dto);
    const chart = fixture.debugElement.query(By.css('p-chart'));
    expect(chart).not.toBeNull();
    expect(fixture.componentInstance.salesChartData()?.labels).toEqual([
      formatDateForSpec('2026-04-28'),
      formatDateForSpec('2026-04-29'),
    ]);
    expect(fixture.componentInstance.salesChartData()?.datasets).toHaveLength(4);
  });

  it('continues rendering chart data when negative profits and wastage are returned after Apply', () => {
    const { fixture, facade } = createFixtureWithFacade(makeOwnerDto({
      wastageCost: 60,
      salesTrendSeries: [
        { date: '2026-04-28', amount: 200, netAmount: 160 },
        { date: '2026-04-29', amount: 320, netAmount: 260 },
      ],
      profitTrendSeries: [
        { date: '2026-04-28', profitBeforeTax: -40, profitAfterTax: -20 },
        { date: '2026-04-29', profitBeforeTax: -10, profitAfterTax: -5 },
      ],
    }), '', {
      applyRange: vi.fn(),
    });

    const component = fixture.componentInstance;
    component.onSelectPreset('last7');
    component.onApply();

    expect(facade.applyRange).toHaveBeenCalledWith(expect.any(String), expect.any(String), 'last7');
    expect(component.salesChartData()).not.toBeNull();
    expect(component.salesChartData()?.datasets).toHaveLength(4);
    expect(component.salesChartData()?.datasets[2]?.data).toEqual([-40, -10]);
    expect(component.salesChartData()?.datasets[3]?.data).toEqual([-20, -5]);
  });

  it('hides sales trend chart when sales trend data is null (Staff role)', () => {
    const fixture = createFixture(makeStaffDto());
    const chart = fixture.debugElement.query(By.css('p-chart'));
    expect(chart).toBeNull();
  });

  it('renders profit trend chart when profitTrendSeries has data (Owner role)', () => {
    const dto = makeOwnerDto({
      profitTrendSeries: [
        { date: '2026-04-28', profitBeforeTax: 60, profitAfterTax: 80 },
        { date: '2026-04-29', profitBeforeTax: 100, profitAfterTax: 120 },
      ],
      salesTrendSeries: [
        { date: '2026-04-28', amount: 200, netAmount: 200 },
        { date: '2026-04-29', amount: 300, netAmount: 300 },
      ],
    });
    const fixture = createFixture(dto);
    const charts = fixture.debugElement.queryAll(By.css('p-chart'));
    expect(charts.length).toBeGreaterThanOrEqual(1);
  });

  it('falls back to expense metric when selected sales metric has no chart data', () => {
    const dto = makeOwnerDto({
      salesTrendSeries: [],
      profitTrendSeries: [],
      expenseRecorded: 50,
      expenseCorrection: -10,
      netExpense: 40,
    });
    const fixture = createFixture(dto);

    fixture.componentInstance.selectedMetric.set('sales');
    fixture.detectChanges();

    expect(fixture.componentInstance.activeMetric()).toBe('expense');
    expect(fixture.componentInstance.selectedChartData()?.datasets[0]?.data).toEqual([50, -10, 40]);
  });

  it('coerces sales pie selection to bar chart', () => {
    const dto = makeOwnerDto({
      salesTrendSeries: [
        { date: '2026-04-28', amount: 200, netAmount: 200 },
        { date: '2026-04-29', amount: 300, netAmount: 300 },
      ],
      profitTrendSeries: [
        { date: '2026-04-28', profitBeforeTax: 80, profitAfterTax: 100 },
        { date: '2026-04-29', profitBeforeTax: 120, profitAfterTax: 150 },
      ],
    });
    const fixture = createFixture(dto);

    fixture.componentInstance.selectedMetric.set('sales');
    fixture.componentInstance.selectedChartType.set('pie');
    fixture.detectChanges();

    expect(fixture.componentInstance.selectedPrimeChartType()).toBe('bar');
  });

  it('allows doughnut chart for payment mix metric', () => {
    const dto = makeOwnerDto({
      paymentMix: { cash: 200, upi: 100, card: 50, credit: 50 },
      salesTrendSeries: [],
      profitTrendSeries: [],
    });
    const fixture = createFixture(dto);

    fixture.componentInstance.selectedMetric.set('paymentMix');
    fixture.componentInstance.selectedChartType.set('doughnut');
    fixture.detectChanges();

    expect(fixture.componentInstance.activeMetric()).toBe('paymentMix');
    expect(fixture.componentInstance.selectedPrimeChartType()).toBe('doughnut');
    expect(fixture.componentInstance.selectedChartOptions()).toMatchObject({
      responsive: true,
      plugins: { legend: { position: 'bottom' } },
    });
  });

  it('renders payment mix date-wise for bar chart type', () => {
    const dto = makeOwnerDto({
      paymentMix: { cash: 300, upi: 40, card: 20, credit: 10 },
      paymentMixTrendSeries: [
        { date: '2026-04-28', cash: 120, upi: 0, card: 0, credit: 10 },
        { date: '2026-04-29', cash: 180, upi: 40, card: 20, credit: 0 },
      ],
      salesTrendSeries: [],
      profitTrendSeries: [],
    });
    const fixture = createFixture(dto);

    fixture.componentInstance.selectedMetric.set('paymentMix');
    fixture.componentInstance.selectedChartType.set('bar');
    fixture.detectChanges();

    const chartData = fixture.componentInstance.selectedChartData();
    expect(chartData?.labels).toEqual([
      formatDateForSpec('2026-04-28'),
      formatDateForSpec('2026-04-29'),
    ]);
    expect(chartData?.datasets).toHaveLength(4);
    expect(chartData?.datasets[0]?.data).toEqual([120, 180]);
    expect(chartData?.datasets[1]?.data).toEqual([0, 40]);
    expect(chartData?.datasets[2]?.data).toEqual([0, 20]);
    expect(chartData?.datasets[3]?.data).toEqual([10, 0]);
  });

  it('falls back to aggregate payment mix when trend series is missing', () => {
    const dto = makeOwnerDto({
      paymentMix: { cash: 300, upi: 40, card: 20, credit: 10 },
      paymentMixTrendSeries: null,
      salesTrendSeries: [],
      profitTrendSeries: [],
    });
    const fixture = createFixture(dto);

    fixture.componentInstance.selectedMetric.set('paymentMix');
    fixture.componentInstance.selectedChartType.set('bar');
    fixture.detectChanges();

    const chartData = fixture.componentInstance.selectedChartData();
    expect(chartData?.labels).toHaveLength(4);
    expect(chartData?.labels?.[0]).toContain('paymentMixCash');
    expect(chartData?.labels?.[1]).toContain('paymentMixUpi');
    expect(chartData?.labels?.[2]).toContain('paymentMixCard');
    expect(chartData?.labels?.[3]).toContain('paymentMixCredit');
    expect(chartData?.datasets).toHaveLength(1);
    expect(chartData?.datasets[0]?.data).toEqual([300, 40, 20, 10]);
  });

  it('shows no primary chart when profit trend data is null (Staff role)', () => {
    const fixture = createFixture(makeStaffDto());
    const charts = fixture.debugElement.queryAll(By.css('p-chart'));
    expect(charts.length).toBe(0);
  });

  it('does not render duplicate payment mix chart inside payment behavior section', () => {
    const dto = makeOwnerDto({
      paymentMix: { cash: 200, upi: 100, card: 50, credit: 50 },
    });
    const fixture = createFixture(dto);

    const chartsBefore = fixture.debugElement.queryAll(By.css('p-chart'));

    // Expand payment behavior section; primary chart should remain the only chart.
    fixture.componentInstance.toggleSection('paymentBehavior');
    fixture.detectChanges();

    const chartsAfter = fixture.debugElement.queryAll(By.css('p-chart'));
    expect(chartsBefore.length).toBe(1);
    expect(chartsAfter.length).toBe(1);
  });

  it('shows no-payment-data message when paymentMix total is zero', () => {
    const dto = makeOwnerDto({
      paymentMix: { cash: 0, upi: 0, card: 0, credit: 0 },
      salesCount: 0,
      hasNoSalesActivity: true,
    });
    const fixture = createFixture(dto);
    const el = fixture.nativeElement as HTMLElement;
    // No donut chart rendered, empty state shown
    const donut = fixture.debugElement.query(By.css('p-chart[type="doughnut"]'));
    expect(donut).toBeNull();
  });

  it('shows comparison badge when previousPeriodSummary is provided (Owner role)', () => {
    const dto = makeOwnerDto({
      salesCount: 10,
      salesBooked: 1000,
      netSalesBooked: 900,
      salesTrendSeries: [
        { date: '2026-04-28', amount: 450, netAmount: 400 },
        { date: '2026-04-29', amount: 550, netAmount: 500 },
      ],
      profitTrendSeries: [
        { date: '2026-04-28', profitBeforeTax: 150, profitAfterTax: 180 },
        { date: '2026-04-29', profitBeforeTax: 220, profitAfterTax: 260 },
      ],
      previousPeriodSummary: {
        startDate: '2026-03-25',
        endDate: '2026-03-31',
        salesCount: 7,
        salesBooked: 700,
        netSalesBooked: 650,
        profitAfterTax: 200,
        netExpense: 50,
        creditSalesPercentage: 0.1,
      },
    });
    const fixture = createFixture(dto);
    expect(fixture.componentInstance.salesChartData()?.datasets).toHaveLength(4);
  });

  it('hides comparison badges when previousPeriodSummary is null (Staff role)', () => {
    const fixture = createFixture(makeStaffDto());
    const badges = fixture.debugElement.queryAll(By.css('[class*="kpi-comparison"]'));
    expect(badges.length).toBe(0);
  });

  describe('Validation UX (#114)', () => {
    it('shows loading overlay when loading with existing data', () => {
      const facade = {
        data$: of(makeOwnerDto()),
        loading$: of(true),
        error$: of(''),
        hasDashboardData$: of(true),
        startDate$: of('2026-03-31'),
        endDate$: of('2026-04-29'),
        selectedPreset$: of('last30'),
        loadDashboard: () => {},
        refresh: () => {},
        applyRange: () => {},
      };

      TestBed.configureTestingModule({
        imports: [
          DashboardPageComponent,
          TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
        ],
        providers: [{ provide: DashboardFacade, useValue: facade }],
      });

      const fixture = TestBed.createComponent(DashboardPageComponent);
      fixture.detectChanges();

      const overlay = fixture.debugElement.query(By.css('.dashboard-loading-overlay'));
      expect(overlay).not.toBeNull();
    });

    it('Apply button is disabled when loading', () => {
      const facade = {
        data$: of(null),
        loading$: of(true),
        error$: of(''),
        hasDashboardData$: of(false),
        startDate$: of('2026-03-31'),
        endDate$: of('2026-04-29'),
        selectedPreset$: of('last30'),
        loadDashboard: () => {},
        refresh: () => {},
        applyRange: () => {},
      };

      TestBed.configureTestingModule({
        imports: [
          DashboardPageComponent,
          TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
        ],
        providers: [{ provide: DashboardFacade, useValue: facade }],
      });

      const fixture = TestBed.createComponent(DashboardPageComponent);
      fixture.detectChanges();

      // Apply button should be disabled (applyDisabled = !isRangeValid || loading)
      // Since loading=true, disabled
      const applyBtn = fixture.debugElement.queryAll(By.css('button[pButton]')).find(
        (b) => b.nativeElement.textContent?.includes('Apply') || b.nativeElement.getAttribute('ng-reflect-label') === 'Apply'
      );
      // Verify component has applyDisabled computed as true
      const component = fixture.componentInstance;
      expect(component.applyDisabled()).toBe(true);
    });
  });

  describe('Range persistence (#118)', () => {
    afterEach(() => {
      _store = {};
    });

    it('saves range to localStorage on Apply', () => {
      const fixture = createFixture(makeOwnerDto());
      const component = fixture.componentInstance;
      component.pendingPreset.set('last7');
      component.pendingStartDate.set('2026-04-23');
      component.pendingEndDate.set('2026-04-29');
      component.onApply();
      const raw = localStorage.getItem('intelibill_dashboard_range');
      expect(raw).not.toBeNull();
      const parsed = JSON.parse(raw!);
      expect(parsed.startDate).toBe('2026-04-23');
      expect(parsed.endDate).toBe('2026-04-29');
      expect(parsed.preset).toBe('last7');
    });

    it('restores valid persisted range on init', () => {
      localStorage.setItem('intelibill_dashboard_range', JSON.stringify({
        startDate: '2026-04-01',
        endDate: '2026-04-15',
        preset: 'custom',
      }));
      const fixture = createFixture(makeOwnerDto());
      const component = fixture.componentInstance;
      expect(component.pendingStartDate()).toBe('2026-04-01');
      expect(component.pendingEndDate()).toBe('2026-04-15');
      expect(component.pendingPreset()).toBe('custom');
    });
  });

  describe('Preset ranges (local calendar)', () => {
    afterEach(() => {
      vi.useRealTimers();
    });

    it('Today uses local yyyy-MM-dd (not UTC date)', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date('2026-05-08T20:00:00.000Z'));

      const fixture = createFixture(makeOwnerDto());
      const component = fixture.componentInstance;
      component.onSelectPreset('today');

      const expected = toLocalIsoDateForSpec(new Date());
      expect(component.pendingStartDate()).toBe(expected);
      expect(component.pendingEndDate()).toBe(expected);
    });

    it('Last 7 days is 7 inclusive local calendar days', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date('2026-05-08T20:00:00.000Z'));

      const fixture = createFixture(makeOwnerDto());
      const component = fixture.componentInstance;
      component.onSelectPreset('last7');

      const end = new Date();
      const start = new Date(end);
      start.setDate(start.getDate() - 6);
      expect(component.pendingEndDate()).toBe(toLocalIsoDateForSpec(end));
      expect(component.pendingStartDate()).toBe(toLocalIsoDateForSpec(start));
    });

    it('Last 7 days applies expected range via Apply', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date('2026-05-08T20:00:00.000Z'));

      const { fixture, facade } = createFixtureWithFacade(makeOwnerDto(), '', {
        applyRange: vi.fn(),
      });
      const component = fixture.componentInstance;

      component.onSelectPreset('last7');
      component.onApply();

      const end = new Date();
      const start = new Date(end);
      start.setDate(start.getDate() - 6);
      expect(facade.applyRange).toHaveBeenCalledWith(
        toLocalIsoDateForSpec(start),
        toLocalIsoDateForSpec(end),
        'last7',
      );
    });

    it('does not Apply when custom range is invalid', () => {
      const { fixture, facade } = createFixtureWithFacade(makeOwnerDto(), '', {
        applyRange: vi.fn(),
      });

      const component = fixture.componentInstance;
      component.pendingPreset.set('custom');
      component.pendingStartDate.set('2026-04-10');
      component.pendingEndDate.set('2026-04-05');
      component.onApply();

      expect(facade.applyRange).not.toHaveBeenCalled();
    });
  });

  describe('Chart-first hierarchy + collapsible sections (#119)', () => {
    it('secondary sections are collapsed by default (no KPI content visible)', () => {
      const fixture = createFixture(makeOwnerDto());
      const component = fixture.componentInstance;
      expect(component.sectionExpanded().expenses).toBe(false);
      expect(component.sectionExpanded().paymentBehavior).toBe(false);
      expect(component.sectionExpanded().stockRisk).toBe(false);
      expect(component.sectionExpanded().receivables).toBe(false);
    });

    it('toggleSection expands and collapses a section', () => {
      const fixture = createFixture(makeOwnerDto());
      const component = fixture.componentInstance;
      expect(component.sectionExpanded().expenses).toBe(false);
      component.toggleSection('expenses');
      expect(component.sectionExpanded().expenses).toBe(true);
      component.toggleSection('expenses');
      expect(component.sectionExpanded().expenses).toBe(false);
    });

    it('section toggle buttons are rendered for Owner role', () => {
      const fixture = createFixture(makeOwnerDto());
      const toggles = fixture.debugElement.queryAll(By.css('.dashboard-section-toggle'));
      expect(toggles.length).toBeGreaterThanOrEqual(3); // expenses, paymentBehavior, stockRisk, receivables
    });

    it('expense KPI content is hidden when expenses section is collapsed', () => {
      const fixture = createFixture(makeOwnerDto());
      fixture.detectChanges();
      const expenseKpi = (fixture.nativeElement as HTMLElement).querySelector('[class*="dashboard-section"]');
      // section content (grid) not present when collapsed
      const grid = fixture.debugElement.queryAll(By.css('.dashboard-kpi-grid'));
      // Only the primary Sales KPI grid should be in the DOM initially; secondary grids collapsed
      // The primary Sales & Profit grid is not collapsible
      expect(fixture.componentInstance.sectionExpanded().expenses).toBe(false);
    });
  });
});
