import { CurrencyPipe } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { provideZonelessChangeDetection } from '@angular/core';

import { DashboardKpiCardsComponent } from './dashboard-kpi-cards.component';
import type { DashboardDto } from '../../../services/dashboard.models';

const stubDashboard: DashboardDto = {
  generatedAt: '2026-06-01T00:00:00Z',
  startDate: '2026-05-01',
  endDate: '2026-05-31',
  salesCount: 42,
  salesRevenue: 9500,
  hasNoSalesActivity: false,
  customerCreditDue: 0,
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
  runningLowStockCount: 0,
  criticalStockCount: 0,
  rankedShortageList: [],
  highestDueCustomer: null,
  topFiveDueCustomers: null,
  alerts: [],
  salesTrendSeries: null,
  profitTrendSeries: null,
  paymentMixTrendSeries: null,
  previousPeriodSummary: null,
  latestSales: [],
  stockValue: null,
};

const expectedSalesRevenue = new CurrencyPipe('en-US').transform(
  stubDashboard.salesRevenue,
  'INR',
  'symbol',
  '1.2-2',
);

describe('DashboardKpiCardsComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [DashboardKpiCardsComponent],
      providers: [provideZonelessChangeDetection()],
    });
  });

  it('renders sales revenue card', () => {
    const fixture = TestBed.createComponent(DashboardKpiCardsComponent);
    fixture.componentRef.setInput('dashboard', stubDashboard);
    fixture.detectChanges();

    const card = fixture.nativeElement.querySelector('[data-testid="sales-revenue-card"]');
    expect(card).toBeTruthy();
  });

  it('renders invoice count card', () => {
    const fixture = TestBed.createComponent(DashboardKpiCardsComponent);
    fixture.componentRef.setInput('dashboard', stubDashboard);
    fixture.detectChanges();

    const card = fixture.nativeElement.querySelector('[data-testid="invoice-count-card"]');
    expect(card).toBeTruthy();
  });

  it('displays sales revenue value', () => {
    const fixture = TestBed.createComponent(DashboardKpiCardsComponent);
    fixture.componentRef.setInput('dashboard', stubDashboard);
    fixture.detectChanges();

    const valueEl = fixture.nativeElement.querySelector('[data-testid="sales-revenue-value"]');
    expect(valueEl?.textContent?.trim()).toBe(expectedSalesRevenue);
  });

  it('displays invoice count value', () => {
    const fixture = TestBed.createComponent(DashboardKpiCardsComponent);
    fixture.componentRef.setInput('dashboard', stubDashboard);
    fixture.detectChanges();

    const valueEl = fixture.nativeElement.querySelector('[data-testid="invoice-count-value"]');
    expect(valueEl?.textContent?.trim()).toContain('42');
  });

  it('handles null dashboard gracefully', () => {
    const fixture = TestBed.createComponent(DashboardKpiCardsComponent);
    fixture.componentRef.setInput('dashboard', null);
    fixture.detectChanges();

    const container = fixture.nativeElement.querySelector('.kpi-cards');
    expect(container).toBeTruthy();
  });
});
