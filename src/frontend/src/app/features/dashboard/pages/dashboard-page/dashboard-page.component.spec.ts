import '@angular/compiler';

import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../../core/auth/auth.service';
import { DashboardService } from '../../services/dashboard.service';
import { DashboardPageComponent } from './dashboard-page.component';

describe('DashboardPageComponent', () => {
  const sessionSignal = signal({
    activeShopId: 'shop-1',
    shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null }],
  } as never);

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

    sessionSignal.set({
      activeShopId: 'shop-1',
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null }],
    } as never);

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

  it('loads dashboard data for owner and manager roles', () => {
    dashboardService.getDashboard.mockReturnValue(
      of({
        generatedAt: '2026-06-09T10:30:00Z',
        startDate: '2026-06-01',
        endDate: '2026-06-09',
        salesCount: 42,
        hasNoSalesActivity: false,
        salesBooked: 15000,
        netSalesBooked: 14000,
        wastageCost: 500,
        cashCollected: 8000,
        profitBeforeTax: 2500,
        profitAfterTax: 2000,
        expenseRecorded: 1000,
        expenseCorrection: 0,
        netExpense: 1000,
        creditSalesAmount: 6000,
        creditSalesPercentage: 40,
        paymentMix: null,
        creditShareWarning: false,
        runningLowStockCount: 3,
        criticalStockCount: 1,
        rankedShortageList: [],
        highestDueCustomer: null,
        topFiveDueCustomers: [],
        alerts: [],
        salesTrendSeries: [],
        profitTrendSeries: [],
        paymentMixTrendSeries: [],
        previousPeriodSummary: null,
      })
    );

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    expect(dashboardService.getDashboard).toHaveBeenCalledWith({});
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    expect(fixture.componentInstance.dashboard()?.salesCount).toBe(42);
    expect(fixture.componentInstance.isLoading()).toBe(false);
  });

  it('redirects staff users to /sales before loading dashboard data', () => {
    sessionSignal.set({
      activeShopId: 'shop-1',
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Staff', isDefault: true, lastUsedAt: null }],
    } as never);

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    expect(router.navigateByUrl).toHaveBeenCalledWith('/sales');
    expect(dashboardService.getDashboard).not.toHaveBeenCalled();
    expect(fixture.componentInstance.isLoading()).toBe(false);
    expect(fixture.componentInstance.dashboard()).toBeNull();
  });
});
