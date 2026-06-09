import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { DashboardService } from '../../services/dashboard.service';
import { DashboardPageComponent } from './dashboard-page.component';

describe('DashboardPageComponent', () => {
  const dashboardService = {
    getDashboard: vi.fn(),
  };

  beforeEach(() => {
    dashboardService.getDashboard.mockReset();

    TestBed.configureTestingModule({
      imports: [DashboardPageComponent],
      providers: [{ provide: DashboardService, useValue: dashboardService }],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('renders the empty recent activity state', () => {
    dashboardService.getDashboard.mockReturnValue(
      of({
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
      }),
    );

    const fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Latest Sales');
    expect(host.textContent).toContain('No recent sales');
  });

  it('renders populated recent activity entries', () => {
    dashboardService.getDashboard.mockReturnValue(
      of({
        generatedAt: '2026-06-09T10:30:00Z',
        startDate: '2026-06-01',
        endDate: '2026-06-09',
        salesCount: 12,
        hasNoSalesActivity: false,
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
