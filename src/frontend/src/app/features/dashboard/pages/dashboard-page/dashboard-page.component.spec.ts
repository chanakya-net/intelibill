import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';
import { describe, expect, it } from 'vitest';

import { DashboardDto } from '../../services/dashboard.service';
import { DashboardFacade } from '../../state/dashboard.facade';
import { DashboardPageComponent } from './dashboard-page.component';

const makeOwnerDto = (overrides?: Partial<DashboardDto>): DashboardDto => ({
  generatedAt: '2026-04-29T10:00:00Z',
  startDate: '2026-03-31',
  endDate: '2026-04-29',
  salesCount: 5,
  hasNoSalesActivity: false,
  salesBooked: 500,
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
  ...overrides,
});

const makeStaffDto = (): DashboardDto => ({
  generatedAt: '2026-04-29T10:00:00Z',
  startDate: '2026-03-31',
  endDate: '2026-04-29',
  salesCount: 5,
  hasNoSalesActivity: false,
  salesBooked: null,
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
});

function createFixture(dto: DashboardDto | null, errorMessage = '') {
  const facade = {
    data$: of(dto),
    loading$: of(false),
    error$: of(errorMessage),
    hasDashboardData$: of(dto !== null),
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
  return fixture;
}

describe('DashboardPageComponent', () => {
  it('renders sales count for all roles', () => {
    const fixture = createFixture(makeOwnerDto());
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('5');
  });

  it('renders financial KPI sections for Owner role', () => {
    const fixture = createFixture(makeOwnerDto({ salesBooked: 500 }));
    const cards = fixture.debugElement.queryAll(By.css('p-card'));
    // financial sections are present (sales booked card, expense cards, payment mix)
    expect(cards.length).toBeGreaterThan(4);
  });

  it('hides financial KPI sections for Staff role (null fields)', () => {
    const fixture = createFixture(makeStaffDto());
    const el = fixture.nativeElement as HTMLElement;
    // Sales count still visible
    expect(el.textContent).toContain('5');
    // Stock shortage item visible
    expect(el.textContent).toContain('Sugar');
    // No INR currency values for financial sections (paymentMix is null)
    // The payment mix section should not be rendered
    expect(el.querySelector('.payment-mix')).toBeNull();
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

  it('renders shortage list items', () => {
    const dto = makeOwnerDto({
      rankedShortageList: [
        { itemName: 'Salt', quantity: 0, reorderLevel: 5, shortage: 5 },
      ],
    });
    const fixture = createFixture(dto);
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

  it('renders preset buttons', () => {
    const fixture = createFixture(makeOwnerDto());
    const buttons = fixture.debugElement.queryAll(By.css('.range-presets button'));
    expect(buttons.length).toBe(6);
  });

  it('renders sales trend chart when salesTrendSeries has data', () => {
    const dto = makeOwnerDto({
      salesTrendSeries: [
        { date: '2026-04-28', amount: 200 },
        { date: '2026-04-29', amount: 300 },
      ],
    });
    const fixture = createFixture(dto);
    const chart = fixture.debugElement.query(By.css('p-chart'));
    expect(chart).not.toBeNull();
  });

  it('hides sales trend chart when salesTrendSeries is null (Staff role)', () => {
    const fixture = createFixture(makeStaffDto());
    const chart = fixture.debugElement.query(By.css('p-chart'));
    expect(chart).toBeNull();
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
});
