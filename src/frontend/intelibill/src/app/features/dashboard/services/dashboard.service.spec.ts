import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../../environments/environment';
import { DashboardService } from './dashboard.service';

describe('DashboardService', () => {
  let service: DashboardService;
  let httpMock: HttpTestingController;
  const apiUrl = `${environment.apiBaseUrl}/dashboard`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(DashboardService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getDashboard performs GET to /dashboard without query params', () => {
    const mockData = {
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
      netProfit: 2000,
      expenseRecorded: 1000,
      expenseCorrection: 0,
      netExpense: 1000,
      supplierPayables: 2750,
      creditSalesAmount: 6000,
      creditSalesPercentage: 40,
      paymentMix: null,
      creditNoteSettlementAmount: 0,
      creditShareWarning: false,
      runningLowStockCount: 3,
      lowStockItemCount: 2,
      criticalStockCount: 1,
      rankedShortageList: [],
      highestDueCustomer: null,
      topFiveDueCustomers: [],
      alerts: [],
      salesTrendSeries: [],
      profitTrendSeries: [],
      paymentMixTrendSeries: [],
      previousPeriodSummary: null,
    };

    service.getDashboard({}).subscribe((res) => expect(res).toEqual(mockData));

    const req = httpMock.expectOne(apiUrl);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.keys()).toEqual([]);
    req.flush(mockData);
  });

  it('getDashboard includes from date query param when provided', () => {
    const mockData = {
      generatedAt: '2026-06-09T10:30:00Z',
      startDate: '2026-05-01',
      endDate: '2026-06-09',
      salesCount: 150,
      hasNoSalesActivity: false,
      salesBooked: 50000,
      netSalesBooked: 46000,
      wastageCost: 1200,
      cashCollected: 25000,
      profitBeforeTax: 8000,
      profitAfterTax: 6400,
      netProfit: 6400,
      expenseRecorded: 3500,
      expenseCorrection: 0,
      netExpense: 3500,
      supplierPayables: 4800,
      creditSalesAmount: 20000,
      creditSalesPercentage: 40,
      paymentMix: null,
      creditNoteSettlementAmount: 0,
      creditShareWarning: false,
      runningLowStockCount: 5,
      lowStockItemCount: 4,
      criticalStockCount: 2,
      rankedShortageList: [],
      highestDueCustomer: null,
      topFiveDueCustomers: [],
      alerts: [],
      salesTrendSeries: [],
      profitTrendSeries: [],
      paymentMixTrendSeries: [],
      previousPeriodSummary: null,
    };

    service.getDashboard({ from: '2026-05-01' }).subscribe((res) => expect(res).toEqual(mockData));

    const req = httpMock.expectOne(`${apiUrl}?from=2026-05-01`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('from')).toBe('2026-05-01');
    req.flush(mockData);
  });

  it('getDashboard includes to date query param when provided', () => {
    const mockData = {
      generatedAt: '2026-06-09T10:30:00Z',
      startDate: '2026-06-01',
      endDate: '2026-06-30',
      salesCount: 60,
      hasNoSalesActivity: false,
      salesBooked: 22000,
      netSalesBooked: 20500,
      wastageCost: 700,
      cashCollected: 11000,
      profitBeforeTax: 3500,
      profitAfterTax: 2800,
      netProfit: 2800,
      expenseRecorded: 1500,
      expenseCorrection: 0,
      netExpense: 1500,
      supplierPayables: 2250,
      creditSalesAmount: 9000,
      creditSalesPercentage: 41,
      paymentMix: null,
      creditNoteSettlementAmount: 0,
      creditShareWarning: false,
      runningLowStockCount: 4,
      lowStockItemCount: 1,
      criticalStockCount: 1,
      rankedShortageList: [],
      highestDueCustomer: null,
      topFiveDueCustomers: [],
      alerts: [],
      salesTrendSeries: [],
      profitTrendSeries: [],
      paymentMixTrendSeries: [],
      previousPeriodSummary: null,
    };

    service.getDashboard({ to: '2026-06-30' }).subscribe((res) => expect(res).toEqual(mockData));

    const req = httpMock.expectOne(`${apiUrl}?to=2026-06-30`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('to')).toBe('2026-06-30');
    req.flush(mockData);
  });

  it('getDashboard includes both from and to date query params', () => {
    const mockData = {
      generatedAt: '2026-06-15T14:20:00Z',
      startDate: '2026-05-15',
      endDate: '2026-06-15',
      salesCount: 95,
      hasNoSalesActivity: false,
      salesBooked: 35000,
      netSalesBooked: 32500,
      wastageCost: 900,
      cashCollected: 17500,
      profitBeforeTax: 5200,
      profitAfterTax: 4160,
      netProfit: 4160,
      expenseRecorded: 2200,
      expenseCorrection: 0,
      netExpense: 2200,
      supplierPayables: 3900,
      creditSalesAmount: 14000,
      creditSalesPercentage: 40,
      paymentMix: null,
      creditNoteSettlementAmount: 0,
      creditShareWarning: false,
      runningLowStockCount: 4,
      lowStockItemCount: 1,
      criticalStockCount: 2,
      rankedShortageList: [],
      highestDueCustomer: null,
      topFiveDueCustomers: [],
      alerts: [],
      salesTrendSeries: [],
      profitTrendSeries: [],
      paymentMixTrendSeries: [],
      previousPeriodSummary: null,
    };

    service.getDashboard({ from: '2026-05-15', to: '2026-06-15' }).subscribe((res) => expect(res).toEqual(mockData));

    const req = httpMock.expectOne(`${apiUrl}?from=2026-05-15&to=2026-06-15`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('from')).toBe('2026-05-15');
    expect(req.request.params.get('to')).toBe('2026-06-15');
    req.flush(mockData);
  });
});
