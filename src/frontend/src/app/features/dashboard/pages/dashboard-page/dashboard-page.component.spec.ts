import '@angular/compiler';

import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../../core/auth/auth.service';
import { DashboardDto } from '../../services/dashboard.models';
import { DashboardService } from '../../services/dashboard.service';
import { DashboardPageComponent } from './dashboard-page.component';

describe('DashboardPageComponent', () => {
  const createSession = (role: 'Owner' | 'Manager' | 'Staff') =>
    ({
      activeShopId: 'shop-1',
      shops: [{ shopId: 'shop-1', shopName: 'Main', role, isDefault: true, lastUsedAt: null }],
    }) as never;

  const createDashboard = (overrides: Partial<DashboardDto> = {}): DashboardDto => ({
    generatedAt: '2026-06-09T10:30:00Z',
    startDate: '2026-06-01',
    endDate: '2026-06-09',
    salesCount: 0,
    hasNoSalesActivity: true,
    salesBooked: 0,
    netSalesBooked: 0,
    wastageCost: 0,
    cashCollected: 0,
    profitBeforeTax: 0,
    profitAfterTax: 0,
    expenseRecorded: 0,
    expenseCorrection: 0,
    netExpense: 0,
    creditSalesAmount: 0,
    creditSalesPercentage: 0,
    paymentMix: null,
    creditShareWarning: false,
    runningLowStockCount: 0,
    criticalStockCount: 0,
    rankedShortageList: [],
    highestDueCustomer: null,
    topFiveDueCustomers: [],
    alerts: [],
    salesTrendSeries: [],
    profitTrendSeries: [],
    paymentMixTrendSeries: [],
    previousPeriodSummary: null,
    latestSales: [],
    ...overrides,
  });

  const sessionSignal = signal(createSession('Owner'));

  const authService = {
    session: sessionSignal,
  };

  const dashboardService = {
    getDashboard: vi.fn(),
  };

  const router = {
    navigateByUrl: vi.fn<Router['navigateByUrl']>().mockResolvedValue(true),
  };

  let fixture: ComponentFixture<DashboardPageComponent>;

  beforeEach(() => {
    TestBed.resetTestingModule();
    dashboardService.getDashboard.mockReset();
    router.navigateByUrl.mockClear();
    sessionSignal.set(createSession('Owner'));

    TestBed.configureTestingModule({
      imports: [DashboardPageComponent],
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: DashboardService, useValue: dashboardService },
        { provide: Router, useValue: router },
      ],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it.each(['Owner', 'Manager'] as const)('loads dashboard data for %s roles', (role) => {
    sessionSignal.set(createSession(role));
    dashboardService.getDashboard.mockReturnValue(
      of(
        createDashboard({
          salesCount: 42,
          hasNoSalesActivity: false,
          salesBooked: 15000,
          netSalesBooked: 14000,
          wastageCost: 500,
          cashCollected: 8000,
          profitBeforeTax: 2500,
          profitAfterTax: 2000,
          expenseRecorded: 1000,
          netExpense: 1000,
          creditSalesAmount: 6000,
          creditSalesPercentage: 40,
          runningLowStockCount: 3,
          criticalStockCount: 1,
        }),
      ),
    );

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    expect(dashboardService.getDashboard).toHaveBeenCalledWith({});
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    expect(fixture.componentInstance.dashboard()?.salesCount).toBe(42);
    expect(fixture.componentInstance.isLoading()).toBe(false);
  });

  it('renders expenses KPI card with formatted value when dashboard loaded', () => {
    dashboardService.getDashboard.mockReturnValue(
      of({
        generatedAt: '2026-06-09T10:30:00Z',
        startDate: '2026-06-01',
        endDate: '2026-06-09',
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
        netExpense: 1250.75,
        creditSalesAmount: null,
        creditSalesPercentage: null,
        paymentMix: null,
        creditShareWarning: null,
        runningLowStockCount: 0,
        criticalStockCount: 0,
        rankedShortageList: [],
        highestDueCustomer: null,
        topFiveDueCustomers: [],
        alerts: [],
        salesTrendSeries: [],
        profitTrendSeries: [],
        paymentMixTrendSeries: [],
        previousPeriodSummary: null,
        latestSales: [],
      })
    );

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const kpiValue = fixture.nativeElement.querySelector('[data-testid="expenses-kpi-value"]');
    expect(kpiValue).not.toBeNull();
    expect(kpiValue.textContent).toContain('₹');
    expect(fixture.componentInstance.formattedExpenses()).toContain('₹');
  });

  it('renders expenses KPI as ₹0 when netExpense is zero', () => {
    dashboardService.getDashboard.mockReturnValue(
      of({
        generatedAt: '2026-06-09T10:30:00Z',
        startDate: '2026-06-01',
        endDate: '2026-06-09',
        salesCount: 0,
        hasNoSalesActivity: true,
        salesBooked: null,
        netSalesBooked: null,
        wastageCost: null,
        cashCollected: null,
        profitBeforeTax: null,
        profitAfterTax: null,
        expenseRecorded: null,
        expenseCorrection: null,
        netExpense: 0,
        creditSalesAmount: null,
        creditSalesPercentage: null,
        paymentMix: null,
        creditShareWarning: null,
        runningLowStockCount: 0,
        criticalStockCount: 0,
        rankedShortageList: [],
        highestDueCustomer: null,
        topFiveDueCustomers: [],
        alerts: [],
        salesTrendSeries: [],
        profitTrendSeries: [],
        paymentMixTrendSeries: [],
        previousPeriodSummary: null,
        latestSales: [],
      })
    );

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.formattedExpenses()).toContain('₹');
    expect(fixture.componentInstance.formattedExpenses()).toContain('0');
  });

  it('redirects staff users to /sales before loading dashboard data', () => {
    sessionSignal.set(createSession('Staff'));

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    expect(router.navigateByUrl).toHaveBeenCalledWith('/sales');
    expect(dashboardService.getDashboard).not.toHaveBeenCalled();
    expect(fixture.componentInstance.isLoading()).toBe(false);
    expect(fixture.componentInstance.dashboard()).toBeNull();
  });

  it('renders the empty recent activity state', () => {
    dashboardService.getDashboard.mockReturnValue(of(createDashboard()));

    const fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Latest Sales');
    expect(host.textContent).toContain('No recent sales');
  });

  it('renders populated recent activity entries', () => {
    dashboardService.getDashboard.mockReturnValue(
      of(
        createDashboard({
          salesCount: 12,
          hasNoSalesActivity: false,
          latestSales: [
            {
              saleId: 'sale-1',
              invoiceNumber: 'INV-000123',
              customerDisplayName: 'Walk-in Customer',
              soldAt: '2026-06-09T10:00:00Z',
              totalAmount: 1250,
            },
            {
              saleId: 'sale-2',
              invoiceNumber: 'INV-000122',
              customerDisplayName: 'Asha',
              soldAt: '2026-06-09T09:15:00Z',
              totalAmount: 980,
            },
          ],
        }),
      ),
    );

    const fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('INV-000123');
    expect(host.textContent).toContain('Walk-in Customer');
    expect(host.textContent).toContain('1,250.00');
    expect(host.textContent).toContain('Asha');
  });
});
