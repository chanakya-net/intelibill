import '@angular/compiler';

import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';

import { DashboardChartComponent } from './dashboard-chart.component';
import type { DashboardDto } from '../../../services/dashboard.models';

const stubDashboard: DashboardDto = {
  generatedAt: '2026-06-09T10:30:00Z',
  startDate: '2026-06-01',
  endDate: '2026-06-09',
  salesCount: 0,
  salesRevenue: 0,
  hasNoSalesActivity: true,
  customerCreditDue: 0,
  salesBooked: 0,
  netSalesBooked: 0,
  wastageCost: 0,
  cashCollected: 0,
  profitBeforeTax: 0,
  profitAfterTax: 0,
  netProfit: 0,
  netProfitChangePercent: null,
  expenseRecorded: 0,
  expenseCorrection: 0,
  netExpense: 0,
  supplierPayables: 0,
  creditSalesAmount: 0,
  creditSalesPercentage: 0,
  paymentMix: null,
  creditShareWarning: false,
  runningLowStockCount: 0,
  lowStockItemCount: 0,
  criticalStockCount: 0,
  rankedShortageList: [],
  highestDueCustomer: null,
  topFiveDueCustomers: [],
  alerts: [],
  salesTrendSeries: [],
  revenueVsExpenses: [],
  profitTrendSeries: [],
  paymentMixTrendSeries: [],
  previousPeriodSummary: null,
  latestSales: [],
  stockValue: null,
};

describe('DashboardChartComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [DashboardChartComponent],
    });
  });

  it('maps sales trend DTOs into chart labels and datasets', () => {
    const fixture = TestBed.createComponent(DashboardChartComponent);
    fixture.componentRef.setInput('dashboard', {
      ...stubDashboard,
      salesTrendSeries: [
        { date: '2026-06-01', amount: 1200, netAmount: 1100 },
        { date: '2026-06-02', amount: 1800, netAmount: 1650 },
      ],
    });

    expect(fixture.componentInstance.salesTrendChartData()).toEqual({
      labels: ['2026-06-01', '2026-06-02'],
      datasets: [
        expect.objectContaining({ label: 'Gross Sales', data: [1200, 1800] }),
        expect.objectContaining({ label: 'Net Sales', data: [1100, 1650] }),
      ],
    });
  });

  it('maps revenue vs expenses DTOs into chart labels and datasets', () => {
    const fixture = TestBed.createComponent(DashboardChartComponent);
    fixture.componentRef.setInput('dashboard', {
      ...stubDashboard,
      revenueVsExpenses: [
        { date: '2026-06-01', revenue: 2400, expenses: 900 },
        { date: '2026-06-02', revenue: 3200, expenses: 1250 },
      ],
    });

    expect(fixture.componentInstance.revenueVsExpensesChartData()).toEqual({
      labels: ['2026-06-01', '2026-06-02'],
      datasets: [
        expect.objectContaining({ label: 'Revenue', data: [2400, 3200] }),
        expect.objectContaining({ label: 'Expenses', data: [900, 1250] }),
      ],
    });
  });

  it('exposes empty chart datasets when the dashboard series are empty', () => {
    const fixture = TestBed.createComponent(DashboardChartComponent);
    fixture.componentRef.setInput('dashboard', {
      ...stubDashboard,
      salesTrendSeries: [],
      revenueVsExpenses: [],
    });

    expect(fixture.componentInstance.salesTrendChartData()).toEqual({
      labels: [],
      datasets: [
        expect.objectContaining({ label: 'Gross Sales', data: [] }),
        expect.objectContaining({ label: 'Net Sales', data: [] }),
      ],
    });
    expect(fixture.componentInstance.revenueVsExpensesChartData()).toEqual({
      labels: [],
      datasets: [
        expect.objectContaining({ label: 'Revenue', data: [] }),
        expect.objectContaining({ label: 'Expenses', data: [] }),
      ],
    });
  });

  it('formats chart ticks in rupees', () => {
    const fixture = TestBed.createComponent(DashboardChartComponent);

    const tickFormatter = fixture.componentInstance.salesTrendChartOptions.scales?.['y']?.ticks
      ?.callback as ((this: unknown, value: string | number, index: number, ticks: unknown[]) => string) | undefined;
    expect(tickFormatter?.call(undefined, 1250, 0, [])).toBe('₹1,250');
  });
});
