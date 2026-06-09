import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';

import { environment } from '../../../../../environments/environment';
import type { DashboardDto } from '../../services/dashboard.models';
import { DashboardPageComponent } from './dashboard-page.component';

const stubDashboard: DashboardDto = {
  generatedAt: '2026-06-01T00:00:00Z',
  startDate: '2026-05-01',
  endDate: '2026-05-31',
  salesCount: 15,
  salesRevenue: 7500,
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
};

describe('DashboardPageComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [DashboardPageComponent],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('fetches dashboard data on init', () => {
    const fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/dashboard`);
    expect(req.request.method).toBe('GET');
    req.flush(stubDashboard);
  });

  it('renders sales revenue KPI from API response', () => {
    const fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    httpMock.expectOne(`${environment.apiBaseUrl}/dashboard`).flush(stubDashboard);
    fixture.detectChanges();

    const valueEl = fixture.nativeElement.querySelector('[data-testid="sales-revenue-value"]');
    expect(valueEl?.textContent?.trim()).toBeTruthy();
  });

  it('renders invoice count KPI from API response', () => {
    const fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    httpMock.expectOne(`${environment.apiBaseUrl}/dashboard`).flush(stubDashboard);
    fixture.detectChanges();

    const valueEl = fixture.nativeElement.querySelector('[data-testid="invoice-count-value"]');
    expect(valueEl?.textContent?.trim()).toContain('15');
  });

  it('binds fetched dashboard to KPI cards child component', () => {
    const fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    httpMock.expectOne(`${environment.apiBaseUrl}/dashboard`).flush(stubDashboard);
    fixture.detectChanges();

    const revenueEl = fixture.nativeElement.querySelector('[data-testid="sales-revenue-value"]');
    const countEl = fixture.nativeElement.querySelector('[data-testid="invoice-count-value"]');
    expect(revenueEl).toBeTruthy();
    expect(countEl?.textContent?.trim()).toContain('15');
  });
});
