import '@angular/compiler';

import { CurrencyPipe } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
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
  netProfit: 0,
  netProfitChangePercent: null,
  expenseRecorded: null,
  expenseCorrection: null,
  netExpense: null,
  supplierPayables: 0,
  creditSalesAmount: null,
  creditSalesPercentage: null,
  paymentMix: null,
  creditNoteSettlementAmount: null,
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
  lowStockItemCount: 3,
  stockValue: null,
};

const expectedSalesRevenue = new CurrencyPipe('en-US').transform(
  stubDashboard.salesRevenue,
  'INR',
  'symbol',
  '1.0-0',
);

describe('DashboardKpiCardsComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [
        DashboardKpiCardsComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [provideZonelessChangeDetection()],
    });
  });

  it('renders sales revenue card', () => {
    const fixture = TestBed.createComponent(DashboardKpiCardsComponent);
    fixture.componentRef.setInput('dashboard', stubDashboard);
    fixture.detectChanges();

    const card = fixture.nativeElement.querySelector('[data-testid="sales-revenue-card"]');
    expect(card).toBeTruthy();
    expect(card?.textContent).toContain('dashboard.kpis.salesRevenue');
  });

  it('prefers shared trading economics over legacy net profit and exposes incomplete operating status', () => {
    const fixture = TestBed.createComponent(DashboardKpiCardsComponent);
    fixture.componentRef.setInput('dashboard', { ...stubDashboard, netProfit: 999,
      economics: { version: 2, costBasis: 'recorded-sale-cost/1', netSalesExcludingTax: 200, cogs: 100, stockLoss: 0,
        returnedGoodsWastageIncludedInCogs: 0, grossTradingProfit: 100, operatingExpenses: 20, operatingProfit: null,
        grossMarginPercent: 50, markupPercent: 100, unclassifiedExpenseCount: 1, unclassifiedExpenseAmount: 20,
        operatingProfitStatus: 'UnclassifiedExpenses', limitations: ['unclassified-expenses'] } });
    fixture.detectChanges();
    const value = fixture.nativeElement.querySelector('[data-testid="net-profit-value"]').textContent;
    expect(value).toContain('100'); expect(value).not.toContain('999');
    const notes = fixture.nativeElement.querySelector('[data-testid="dashboard-economics"]').textContent;
    expect(notes).toContain('sales.profitLoss.status.UnclassifiedExpenses');
    expect(notes).toContain('sales.profitLoss.economics.notAvailable');
  });

  it('preserves an explicit v2 null comparison instead of showing legacy growth', () => {
    const fixture = TestBed.createComponent(DashboardKpiCardsComponent);
    fixture.componentRef.setInput('dashboard', { ...stubDashboard, grossTradingProfitChangePercent: null, netProfitChangePercent: 19900 });
    fixture.detectChanges();
    expect(fixture.componentInstance.netProfitChangePercent()).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="net-profit-change-value"]')).toBeNull();
  });

  it('renders invoice count card', () => {
    const fixture = TestBed.createComponent(DashboardKpiCardsComponent);
    fixture.componentRef.setInput('dashboard', stubDashboard);
    fixture.detectChanges();

    const card = fixture.nativeElement.querySelector('[data-testid="invoice-count-card"]');
    expect(card).toBeTruthy();
  });

  it('renders low stock items card', () => {
    const fixture = TestBed.createComponent(DashboardKpiCardsComponent);
    fixture.componentRef.setInput('dashboard', stubDashboard);
    fixture.detectChanges();

    const card = fixture.nativeElement.querySelector('[data-testid="low-stock-items-card"]');
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

  it.each([
    { netProfit: 2500, label: 'positive' },
    { netProfit: -1250.5, label: 'negative' },
    { netProfit: 0, label: 'zero' },
  ])('displays net profit value', ({ netProfit }) => {
    const fixture = TestBed.createComponent(DashboardKpiCardsComponent);
    fixture.componentRef.setInput('dashboard', { ...stubDashboard, netProfit });
    fixture.detectChanges();

    const valueEl = fixture.nativeElement.querySelector('[data-testid="net-profit-value"]');
    expect(valueEl?.textContent?.trim()).toBe(new CurrencyPipe('en-US').transform(netProfit, 'INR', 'symbol', '1.0-0'));
  });

  it('renders net profit card', () => {
    const fixture = TestBed.createComponent(DashboardKpiCardsComponent);
    fixture.componentRef.setInput('dashboard', stubDashboard);
    fixture.detectChanges();

    const card = fixture.nativeElement.querySelector('[data-testid="net-profit-card"]');
    expect(card).toBeTruthy();
  });

  it.each([
    { netProfitChangePercent: 12.5, expected: '+12.5%' },
    { netProfitChangePercent: -8.75, expected: '-8.8%' },
    { netProfitChangePercent: 0, expected: '0.0%' },
  ])('displays net profit change when present', ({ netProfitChangePercent, expected }) => {
    const fixture = TestBed.createComponent(DashboardKpiCardsComponent);
    fixture.componentRef.setInput('dashboard', { ...stubDashboard, netProfitChangePercent });
    fixture.detectChanges();

    const valueEl = fixture.nativeElement.querySelector('[data-testid="net-profit-change-value"]');
    expect(valueEl?.textContent?.trim()).toContain(expected);
    expect(valueEl?.textContent?.trim()).toContain('dashboard.kpis.vsPreviousPeriod');
  });

  it('hides net profit change when null', () => {
    const fixture = TestBed.createComponent(DashboardKpiCardsComponent);
    fixture.componentRef.setInput('dashboard', { ...stubDashboard, netProfitChangePercent: null });
    fixture.detectChanges();

    const valueEl = fixture.nativeElement.querySelector('[data-testid="net-profit-change-value"]');
    expect(valueEl).toBeNull();
  });

  it('displays low stock items value', () => {
    const fixture = TestBed.createComponent(DashboardKpiCardsComponent);
    fixture.componentRef.setInput('dashboard', stubDashboard);
    fixture.detectChanges();

    const valueEl = fixture.nativeElement.querySelector('[data-testid="low-stock-items-value"]');
    expect(valueEl?.textContent?.trim()).toContain('3');
  });

  it('displays zero low stock items value', () => {
    const fixture = TestBed.createComponent(DashboardKpiCardsComponent);
    fixture.componentRef.setInput('dashboard', { ...stubDashboard, lowStockItemCount: 0 });
    fixture.detectChanges();

    const valueEl = fixture.nativeElement.querySelector('[data-testid="low-stock-items-value"]');
    expect(valueEl?.textContent?.trim()).toContain('0');
  });

  it('handles null dashboard gracefully', () => {
    const fixture = TestBed.createComponent(DashboardKpiCardsComponent);
    fixture.componentRef.setInput('dashboard', null);
    fixture.detectChanges();

    const container = fixture.nativeElement.querySelector('.kpi-cards');
    expect(container).toBeTruthy();
  });
});
