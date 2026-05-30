import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { DashboardDateRangeComponent } from '../../components/dashboard-date-range.component';
import { DashboardPageComponent } from './dashboard-page.component';
import { DashboardFacade } from '../../state/dashboard.facade';
import { DashboardDto } from '../../models/dashboard-dto';

const makeOwnerDto = (overrides: Partial<DashboardDto> = {}): DashboardDto => ({
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
  paymentMixTrendSeries: [],
  previousPeriodSummary: null,
  ...overrides,
});

const makeStaffDto = (): DashboardDto => ({
  ...makeOwnerDto({
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
    topFiveDueCustomers: null,
    salesTrendSeries: null,
    profitTrendSeries: null,
    paymentMixTrendSeries: null,
    alerts: [{ alertType: 'RunningLowStock', priority: 3 }],
  }),
});

function createFacade(dto: DashboardDto | null, errorMessage = '', overrides: Partial<TestFacade> = {}) {
  return {
    data$: of(dto),
    loading$: of(false),
    error$: of(errorMessage),
    hasDashboardData$: of(!!dto),
    startDate$: of('2026-03-31'),
    endDate$: of('2026-04-29'),
    selectedPreset$: of('last30' as const),
    loadDashboard: vi.fn(),
    refresh: vi.fn(),
    applyRange: vi.fn(),
    ...overrides,
  } as TestFacade;
}

interface TestFacade {
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
}

function createFixture(dto: DashboardDto | null, errorMessage = '') {
  const facade = createFacade(dto, errorMessage);
  TestBed.configureTestingModule({
    imports: [DashboardPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
    providers: [{ provide: DashboardFacade, useValue: facade }],
  });
  const fixture = TestBed.createComponent(DashboardPageComponent);
  fixture.detectChanges();
  return fixture;
}

describe('DashboardPageComponent', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('loads dashboard data on init', () => {
    const facade = createFacade(makeOwnerDto());
    TestBed.configureTestingModule({
      imports: [DashboardPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [{ provide: DashboardFacade, useValue: facade }],
    });

    const fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    expect(facade.loadDashboard).toHaveBeenCalledTimes(1);
    expect(fixture.componentInstance.data()).toEqual(makeOwnerDto());
  });

  it('dispatches refresh on header button click', () => {
    const facade = createFacade(makeOwnerDto());
    TestBed.configureTestingModule({
      imports: [DashboardPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [{ provide: DashboardFacade, useValue: facade }],
    });

    const fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();
    fixture.debugElement
      .query(By.css('button[icon="pi pi-refresh"]'))
      ?.nativeElement
      .click();

    expect(facade.refresh).toHaveBeenCalledTimes(1);
  });

  it('handles apply range action from date-range component', () => {
    const facade = createFacade(makeOwnerDto());
    TestBed.configureTestingModule({
      imports: [DashboardPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [{ provide: DashboardFacade, useValue: facade }],
    });

    const fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const range = fixture.debugElement.query(By.directive(DashboardDateRangeComponent));
    (range.componentInstance as DashboardDateRangeComponent).rangeChange.emit({
      startDate: '2026-04-01',
      endDate: '2026-04-30',
      preset: 'last30',
    });

    expect(facade.applyRange).toHaveBeenCalledWith('2026-04-01', '2026-04-30', 'last30');
  });

  it('shows no activity warning for staff data', () => {
    const facade = createFacade(makeStaffDto());
    TestBed.configureTestingModule({
      imports: [DashboardPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [{ provide: DashboardFacade, useValue: facade }],
    });

    const fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const noActivity = fixture.debugElement.query(By.css('.dashboard-no-activity'));
    expect(noActivity).toBeNull();
  });

  it('hides financial KPI sections for staff role', () => {
    const facade = createFacade(makeStaffDto());
    TestBed.configureTestingModule({
      imports: [DashboardPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [{ provide: DashboardFacade, useValue: facade }],
    });

    const fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const sections = fixture.debugElement.queryAll(By.css('.dashboard-section'));
    expect(sections.length).toBeLessThan(2);
  });

  it('toggles section panel state', () => {
    const facade = createFacade(makeOwnerDto());
    TestBed.configureTestingModule({
      imports: [DashboardPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [{ provide: DashboardFacade, useValue: facade }],
    });

    const fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();
    expect(fixture.componentInstance.sectionExpanded().expenses).toBe(false);

    fixture.componentInstance.toggleSection('expenses');
    expect(fixture.componentInstance.sectionExpanded().expenses).toBe(true);
  });

  it('coerces pie chart selection to bar for sales metric', () => {
    const facade = createFacade(
      makeOwnerDto({
        salesTrendSeries: [
          { date: '2026-04-28', amount: 200, netAmount: 200 },
          { date: '2026-04-29', amount: 300, netAmount: 300 },
        ],
        profitTrendSeries: [
          { date: '2026-04-28', profitBeforeTax: 80, profitAfterTax: 100 },
          { date: '2026-04-29', profitBeforeTax: 120, profitAfterTax: 150 },
        ],
      }),
    );

    TestBed.configureTestingModule({
      imports: [DashboardPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [{ provide: DashboardFacade, useValue: facade }],
    });

    const fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();
    fixture.componentInstance.selectedMetric.set('sales');
    fixture.componentInstance.onChartTypeChange('pie');

    expect(fixture.componentInstance.selectedChartType()).toBe('bar');
  });

  it('does not expose standalone profit metric option', () => {
    const facade = createFacade(makeOwnerDto());
    TestBed.configureTestingModule({
      imports: [DashboardPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [{ provide: DashboardFacade, useValue: facade }],
    });

    const fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.metricOptions().map((option) => option.value)).not.toContain('profit');
  });

  it('shows alert ribbon when alerts are present', () => {
    const fixture = createFixture(makeOwnerDto({
      alerts: [{ alertType: 'CriticalStock', priority: 1 }, { alertType: 'RunningLowStock', priority: 3 }],
    }));
    const alerts = fixture.debugElement.queryAll(By.css('.dashboard-alert'));
    expect(alerts).toHaveLength(2);
  });

  it('shows no-activity hint when hasNoSalesActivity is true', () => {
    const fixture = createFixture(makeOwnerDto({ hasNoSalesActivity: true }));
    const noActivity = fixture.debugElement.query(By.css('.dashboard-no-activity'));
    expect(noActivity).not.toBeNull();
  });

  it('shows stale data warning when error and data both present', () => {
    const fixture = createFixture(makeOwnerDto(), 'Network timeout');
    const warning = fixture.debugElement.query(By.css('.dashboard-stale-warning'));
    expect(warning).not.toBeNull();
  });

  it('shows error panel when no data and error present', () => {
    const fixture = createFixture(null, 'Network timeout');
    const error = fixture.debugElement.query(By.css('.dashboard-error'));
    expect(error).not.toBeNull();
  });

  it('toggleSection collapses section when toggled twice', () => {
    const fixture = createFixture(makeOwnerDto());
    const component = fixture.componentInstance;
    component.toggleSection('expenses');
    expect(component.sectionExpanded().expenses).toBe(true);
    component.toggleSection('expenses');
    expect(component.sectionExpanded().expenses).toBe(false);
  });
});
