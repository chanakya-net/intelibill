import en from '../../../../../../public/assets/i18n/en-IN.json';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type {
  ProfitLossReportItemDto,
  ProfitLossSummaryDto,
} from '../../services/sale.models';
import { formatLocalIsoDate } from '../../../../shared/utils/date-time.util';
import { SalesFacade } from '../../state/sales.facade';
import { ProfitLossPageComponent } from './profit-loss-page.component';
import { SalesExportService } from '../../services/sales-export.service';
import { HttpResponse } from '@angular/common/http';
import { of } from 'rxjs';

describe('ProfitLossPageComponent', () => {
  const reportSignal = signal<readonly ProfitLossReportItemDto[]>([]);
  const summarySignal = signal<ProfitLossSummaryDto | null>(null);
  const paginationSignal = signal({ totalCount: 0, pageNumber: 1, pageSize: 20 });
  const loadingSignal = signal(false);
  const errorSignal = signal('');

  const salesFacade = {
    profitLossItems: reportSignal,
    profitLossSummary: summarySignal,
    profitLossPagination: paginationSignal,
    loadingProfitLossReport: loadingSignal,
    errorMessage: errorSignal,
    loadProfitLossReport: vi.fn(),
  };

  const salesExportService = {
    exportProfitLoss: vi.fn(),
    extractFilenameWithPrefix: vi.fn(),
    triggerDownload: vi.fn(),
  };

  const baseTranslation = {
    sales: {
      profitLoss: {
        economics: en.sales.profitLoss.economics,
        status: en.sales.profitLoss.status,
        limitations: en.sales.profitLoss.limitations,
        eyebrow: 'Sales Report',
        title: 'Profit & Loss Report',
        subtitle: 'Analyze your margins from each sale',
        reportingPeriod: 'Reporting period: {{from}} - {{to}}',
        ledgerTitle: 'Ledger',
        ledgerSubtitle: 'Browse and filter your profitability records.',
        controls: {
          from: 'From',
          to: 'To',
          type: 'Type',
          searchPlaceholder: 'Search by reference or customer',
          clearFilters: 'Clear filters',
        },
        filters: {
          type: {
            all: 'All',
            sale: 'Sale',
            saleReturn: 'Sale Return',
            inventoryAdjustment: 'Inventory Adjustment',
          },
        },
        kpi: {
          netProfit: 'Net Profit (After Tax)',
          revenue: 'Revenue (Incl Tax)',
          totalCost: 'Total Cost',
          avgMargin: 'Avg. Margin',
        },
        table: {
          reference: 'Reference',
          date: 'Date',
          type: 'Type',
          customer: 'Customer',
          margin: 'Margin',
        },
        profitBeforeTax: 'Profit (Before Tax)',
        profitAfterTax: 'Profit (After Tax)',
        revenueInclTax: 'Revenue (Incl Tax)',
        cost: 'Total Cost',
        wastageCost: 'Wastage Cost',
        rowTypes: {
          Sale: 'Sale',
          SaleReturn: 'Sale Return',
          InventoryAdjustment: 'Inventory Adjustment',
        },
        adjustmentParty: 'Inventory adjustment',
        emptyState: {
          title: 'No records found',
          description: 'No profit and loss rows match the current filters.',
        },
        footer: {
          invoices: 'Invoices',
          returns: 'Returns',
          adjustments: 'Adjustments',
        },
        pagination: {
          showing: 'Showing {{start}}-{{end}} of {{total}}',
          pageSize: 'Rows',
          pageStatus: 'Page {{page}} of {{totalPages}}',
          prev: 'Previous',
          next: 'Next',
          page: 'Page {{page}}',
          ariaLabel: 'Profit and loss pagination',
        },
        actions: {
          export: 'Export',
        },
      },
      history: {
        walkIn: 'Walk-in',
      },
    },
  };

  function createReportItem(overrides: Partial<ProfitLossReportItemDto> = {}): ProfitLossReportItemDto {
    return {
      saleId: 's1',
      referenceNumber: 'INV-001',
      occurredAt: new Date().toISOString(),
      partyName: 'Customer',
      totalCost: 120,
      wastageCost: 10,
      revenueBeforeTax: 220,
      revenueAfterTax: 232,
      profitBeforeTax: 100,
      profitAfterTax: 90,
      marginPercent: 38.4,
      rowType: 'Sale',
      inventoryAdjustmentId: null,
      ...overrides,
    };
  }

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-05-30T10:00:00.000Z'));

    reportSignal.set([]);
    summarySignal.set(null);
    loadingSignal.set(false);
    errorSignal.set('');
    paginationSignal.set({ totalCount: 0, pageNumber: 1, pageSize: 20 });

    salesFacade.loadProfitLossReport.mockReset();
    salesExportService.exportProfitLoss.mockReset();
    salesExportService.extractFilenameWithPrefix.mockReset();
    salesExportService.triggerDownload.mockReset();
    salesExportService.extractFilenameWithPrefix.mockReturnValue('profit-loss-export.xlsx');

    TestBed.configureTestingModule({
      imports: [
        ProfitLossPageComponent,
        TranslocoTestingModule.forRoot({
          preloadLangs: true,
          translocoConfig: {
            availableLangs: ['en-IN'],
            defaultLang: 'en-IN',
            reRenderOnLangChange: true,
          },
          langs: {
            'en-IN': baseTranslation,
          },
        }),
      ],
      providers: [
        { provide: SalesFacade, useValue: salesFacade },
        { provide: SalesExportService, useValue: salesExportService },
      ],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });

  it('loads report with default query params', () => {
    const fixture = TestBed.createComponent(ProfitLossPageComponent);
    fixture.detectChanges();

    const baseDate = new Date();
    const expectedFrom = new Date(baseDate);
    expectedFrom.setDate(expectedFrom.getDate() - 6);
    expectedFrom.setHours(0, 0, 0, 0);
    const expectedTo = new Date(baseDate);
    expectedTo.setHours(23, 59, 59, 999);

    expect(salesFacade.loadProfitLossReport).toHaveBeenCalledWith({
      from: formatLocalIsoDate(expectedFrom),
      to: formatLocalIsoDate(expectedTo),
      type: undefined,
      search: undefined,
      page: 1,
      pageSize: 20,
    });
  });

  it('debounces search and reloads report', () => {
    const fixture = TestBed.createComponent(ProfitLossPageComponent);
    const component = fixture.componentInstance;

    fixture.detectChanges();
    salesFacade.loadProfitLossReport.mockClear();

    component.searchValue.set('inv-9');
    fixture.detectChanges();
    vi.advanceTimersByTime(299);
    expect(salesFacade.loadProfitLossReport).not.toHaveBeenCalled();

    vi.advanceTimersByTime(2);
    expect(salesFacade.loadProfitLossReport).toHaveBeenCalledWith(
      expect.objectContaining({
        search: 'inv-9',
        page: 1,
      }),
    );
  });

  it('resets page number on from/to/type/search/page size change', () => {
    const fixture = TestBed.createComponent(ProfitLossPageComponent);
    const component = fixture.componentInstance;

    fixture.detectChanges();
    component.pageNumber.set(4);
    component.fromDate.set(new Date('2026-05-10T00:00:00.000Z'));

    fixture.detectChanges();
    expect(component.pageNumber()).toBe(1);

    component.pageNumber.set(3);
    component.typeFilter.set('sale');
    fixture.detectChanges();

    expect(component.pageNumber()).toBe(1);
    expect(salesFacade.loadProfitLossReport).toHaveBeenCalledWith(
      expect.objectContaining({ type: 'sale', page: 1 }),
    );

    component.pageSize.set(50);
    fixture.detectChanges();
    expect(component.pageNumber()).toBe(1);

    component.searchValue.set('cust');
    fixture.detectChanges();
    vi.advanceTimersByTime(300);
    fixture.detectChanges();
    expect(component.pageNumber()).toBe(1);
    expect(salesFacade.loadProfitLossReport).toHaveBeenCalledWith(
      expect.objectContaining({
        search: 'cust',
        page: 1,
      }),
    );
  });

  it('keeps a filter-triggered page reset when the previous response was page two', () => {
    paginationSignal.set({ totalCount: 63, pageNumber: 2, pageSize: 20 });
    const fixture = TestBed.createComponent(ProfitLossPageComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    component.fromDate.set(new Date('2026-05-10T00:00:00.000Z'));
    fixture.detectChanges();

    expect(component.pageNumber()).toBe(1);
    expect(salesFacade.loadProfitLossReport).toHaveBeenLastCalledWith(
      expect.objectContaining({ page: 1 }),
    );
  });

  it('clearFilters restores default state', () => {
    const fixture = TestBed.createComponent(ProfitLossPageComponent);
    const component = fixture.componentInstance;

    component.searchValue.set('abc');
    component.fromDate.set(new Date('2026-01-01T00:00:00.000Z'));
    component.toDate.set(new Date('2026-01-04T00:00:00.000Z'));
    component.typeFilter.set('sale');
    component.pageNumber.set(4);
    component.pageSize.set(50);
    fixture.detectChanges();

    component.clearFilters();

    const expectedFrom = new Date();
    expectedFrom.setDate(expectedFrom.getDate() - 6);
    expectedFrom.setHours(0, 0, 0, 0);
    const expectedTo = new Date();
    expectedTo.setHours(23, 59, 59, 999);

    expect(component.pageNumber()).toBe(1);
    expect(component.pageSize()).toBe(20);
    expect(component.typeFilter()).toBe('all');
    expect(component.searchValue()).toBe('');
    expect(formatLocalIsoDate(component.fromDate())).toBe(formatLocalIsoDate(expectedFrom));
    expect(formatLocalIsoDate(component.toDate())).toBe(formatLocalIsoDate(expectedTo));
  });

  it.each(['FilteredActivity', 'UnclassifiedExpenses', 'AccountingReview', 'ServiceCostsUnmodeled'] as const)(
    'keeps trading visible and explains unavailable operating profit: %s', (status) => {
      summarySignal.set({ netProfitAfterTax: 999, revenueIncludingTax: 999, totalCost: 999, averageMarginPercent: 999, invoiceCount: 1, returnCount: 0, adjustmentCount: 0,
        economics: { version: 2, costBasis: 'recorded-sale-cost/1', netSalesExcludingTax: 200, cogs: 100, stockLoss: 0,
          returnedGoodsWastageIncludedInCogs: 0, grossTradingProfit: 100, operatingExpenses: status === 'FilteredActivity' ? null : 20,
          operatingProfit: null, grossMarginPercent: 50, markupPercent: 100, unclassifiedExpenseCount: 1, unclassifiedExpenseAmount: 20,
          operatingProfitStatus: status, limitations: ['service-costs-unmodeled'] } });
      const fixture = TestBed.createComponent(ProfitLossPageComponent); fixture.detectChanges();
      const text = fixture.nativeElement.textContent;
      expect(text).toContain('₹100.00'); expect(text).toContain('50.0%'); expect(text).toContain('100.0%');
      expect(text).toContain('Not available'); expect(text).toContain(en.sales.profitLoss.status[status]);
      expect(text).toContain('Period-wide unclassified expenses: 1, ₹20.00');
      expect(text).toContain('Service costs are not modeled.');
      expect(text).not.toContain('₹999.00');
    },
  );

  it('renders summary cards from report summary', () => {
    const summary: ProfitLossSummaryDto = {
      economics: { version: 2, costBasis: 'recorded-sale-cost/1', netSalesExcludingTax: 2200, cogs: 800, stockLoss: 200,
        returnedGoodsWastageIncludedInCogs: 0, grossTradingProfit: 1200, operatingExpenses: 20, operatingProfit: 1180,
        grossMarginPercent: 54.55, markupPercent: 150, unclassifiedExpenseCount: 0, unclassifiedExpenseAmount: 0,
        operatingProfitStatus: 'Complete', limitations: [] },
      netProfitAfterTax: 1200,
      revenueIncludingTax: 2200,
      totalCost: 800,
      averageMarginPercent: 18.57,
      invoiceCount: 4,
      returnCount: 1,
      adjustmentCount: 2,
    };

    summarySignal.set(summary);
    reportSignal.set([
      createReportItem(),
      createReportItem({
        referenceNumber: 'INV-002',
        profitAfterTax: 150,
        marginPercent: 45,
      }),
    ]);

    const fixture = TestBed.createComponent(ProfitLossPageComponent);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('₹1,200.00');
    expect(text).toContain('₹2,200.00');
    expect(text).toContain('₹800.00');
    expect(text).toContain('54.6%');
    expect(text).toContain('150.0%');
    expect(text).toContain('Gross margin %');
    expect(text).toContain('Markup %');
    expect(text).not.toContain('Net Profit (After Tax)');
  });

  it('formats signed report values with the sign before the currency symbol', () => {
    summarySignal.set({
      economics: { version: 2, costBasis: 'recorded-sale-cost/1', netSalesExcludingTax: 0, cogs: 1234567890.5, stockLoss: 0,
        returnedGoodsWastageIncludedInCogs: 0, grossTradingProfit: -1234567890.5, operatingExpenses: 0, operatingProfit: -1234567890.5,
        grossMarginPercent: null, markupPercent: -100, unclassifiedExpenseCount: 0, unclassifiedExpenseAmount: 0,
        operatingProfitStatus: 'Complete', limitations: [] },
      netProfitAfterTax: -1234567890.5,
      revenueIncludingTax: 2200,
      totalCost: 1234567890.5,
      averageMarginPercent: -18.57,
      invoiceCount: 4,
      returnCount: 1,
      adjustmentCount: 2,
    });
    reportSignal.set([
      createReportItem({
        profitBeforeTax: -1234567890.5,
        profitAfterTax: -987654321.25,
      }),
    ]);

    const fixture = TestBed.createComponent(ProfitLossPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.totalAmount(-1234567890.5)).toBe('-₹1,23,45,67,890.50');
    expect(fixture.nativeElement.textContent).toContain('-₹1,23,45,67,890.50');
    expect(fixture.nativeElement.textContent).not.toContain('₹-');
  });

  it('exports the current profit-loss filters and downloads the file', () => {
    const response = new HttpResponse({ status: 200, body: new Blob(['pl']) });
    salesExportService.exportProfitLoss.mockReturnValue(of(response));

    const fixture = TestBed.createComponent(ProfitLossPageComponent);
    const component = fixture.componentInstance;
    const fromDate = new Date('2026-05-01T00:00:00.000Z');
    const toDate = new Date('2026-05-13T23:59:59.999Z');
    component.fromDate.set(fromDate);
    component.toDate.set(toDate);
    component.typeFilter.set('saleReturn');
    component.searchValue.set('customer');
    fixture.detectChanges();
    vi.advanceTimersByTime(300);
    fixture.detectChanges();

    component.onExport();
    fixture.detectChanges();

    expect(salesExportService.exportProfitLoss).toHaveBeenCalledWith({
      from: formatLocalIsoDate(fromDate),
      to: formatLocalIsoDate(toDate),
      type: 'saleReturn',
      search: 'customer',
      format: 'xlsx',
    });
    expect(salesExportService.extractFilenameWithPrefix).toHaveBeenCalledWith(response, 'profit-loss-export');
    expect(salesExportService.triggerDownload).toHaveBeenCalledWith(response.body!, 'profit-loss-export.xlsx');
  });

  it('renders not available when economic ratio is null', () => {
    reportSignal.set([
      createReportItem({
        referenceNumber: 'INV-004',
        marginPercent: null,
      }),
    ]);

    const fixture = TestBed.createComponent(ProfitLossPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Not available');
  });

  it('supports pagination helpers and navigation', () => {
    paginationSignal.set({ totalCount: 95, pageNumber: 1, pageSize: 20 });

    const fixture = TestBed.createComponent(ProfitLossPageComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.totalPages()).toBe(5);
    expect(component.rangeStart()).toBe(1);
    expect(component.rangeEnd()).toBe(20);
    expect(component.paginationItems().length).toBeGreaterThan(0);

    component.onPageChange(3);
    fixture.detectChanges();
    expect(component.pageNumber()).toBe(3);
    expect(salesFacade.loadProfitLossReport).toHaveBeenCalledWith(
      expect.objectContaining({ page: 3 }),
    );

    component.onPageChange(100);
    fixture.detectChanges();
    expect(component.pageNumber()).toBe(5);
  });

  it('renders loading and error states', () => {
    loadingSignal.set(true);

    const loadingFixture = TestBed.createComponent(ProfitLossPageComponent);
    loadingFixture.detectChanges();

    expect(loadingFixture.nativeElement.querySelector('.loading-state')).toBeTruthy();

    loadingSignal.set(false);
    errorSignal.set('Something went wrong');
    reportSignal.set([]);

    const errorFixture = TestBed.createComponent(ProfitLossPageComponent);
    errorFixture.detectChanges();

    expect(errorFixture.nativeElement.textContent).toContain('Something went wrong');
    expect(errorFixture.nativeElement.querySelector('.empty-state')).toBeNull();
  });

  it('gives loading and error feedback accessible semantics without stale report actions', () => {
    reportSignal.set([createReportItem({ referenceNumber: 'STALE-ROW' })]);
    summarySignal.set({
      netProfitAfterTax: 1200,
      revenueIncludingTax: 2200,
      totalCost: 800,
      averageMarginPercent: 18.57,
      invoiceCount: 1,
      returnCount: 0,
      adjustmentCount: 0,
    });
    loadingSignal.set(true);

    const fixture = TestBed.createComponent(ProfitLossPageComponent);
    fixture.detectChanges();

    const loading = fixture.nativeElement.querySelector('.loading-state');
    expect(loading.getAttribute('role')).toBe('status');
    expect(loading.getAttribute('aria-busy')).toBe('true');
    expect(fixture.nativeElement.querySelector('.clear-filters-btn').disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('.report-footer')).toBeNull();

    loadingSignal.set(false);
    errorSignal.set('Unable to load profit and loss report.');
    fixture.detectChanges();

    const error = fixture.nativeElement.querySelector('.error');
    expect(error.getAttribute('role')).toBe('alert');
    expect(fixture.nativeElement.querySelector('.kpi-row')).toBeNull();
    expect(fixture.nativeElement.querySelector('.report-content')).toBeNull();
    expect(fixture.nativeElement.querySelector('.report-footer')).toBeNull();
    expect(fixture.nativeElement.querySelector('.export-btn').disabled).toBe(true);
  });

  it('renders mobile cards with core values', () => {
    reportSignal.set([
      createReportItem({
        referenceNumber: 'INV-MOBILE',
        economics: { version: 2, netSalesExcludingTax: 200, cogs: 100, stockLoss: 0, returnedGoodsWastageIncludedInCogs: 0, grossTradingProfit: 100, grossMarginPercent: 50, markupPercent: 100, hasUnmodeledServiceCost: false },
        occurredAt: '2026-05-27T09:15:00.000Z',
        partyName: null,
      }),
    ]);

    const fixture = TestBed.createComponent(ProfitLossPageComponent);
    fixture.detectChanges();

    const cards = fixture.nativeElement.querySelectorAll('.mobile-report-card');
    expect(cards.length).toBe(1);
    expect(cards[0].textContent).toContain('INV-MOBILE');
    expect(cards[0].textContent).toContain('Net sales excluding tax');
    expect(cards[0].textContent).toContain('₹200.00');
  });

  it('handles serverError cleanly by showing the error and hiding the report surface', () => {
    reportSignal.set([
      createReportItem({
        referenceNumber: 'INV-403',
        partyName: 'Alice',
      }),
    ]);

    const fixture = TestBed.createComponent(ProfitLossPageComponent);
    fixture.detectChanges();

    errorSignal.set('Forbidden (403)');
    fixture.detectChanges();

    const element = fixture.nativeElement;
    const errorEl = element.querySelector('.error');

    expect(errorEl).not.toBeNull();
    expect(errorEl.textContent).toContain('Forbidden (403)');
    expect(element.querySelector('p-table')).toBeNull();
    expect(element.querySelector('.mobile-report-card')).toBeNull();
    expect(element.querySelector('.report-footer')).toBeNull();
  });
 it('requests cancellation postings separately from ordinary returns',()=>{const f=TestBed.createComponent(ProfitLossPageComponent);const c=f.componentInstance;f.detectChanges();c.typeFilter.set('saleCancellation');f.detectChanges();expect(salesFacade.loadProfitLossReport).toHaveBeenLastCalledWith(expect.objectContaining({type:'saleCancellation'}));expect(c.getRowTypeTranslationKey('saleCancellation')).toBe('sales.profitLoss.rowTypes.saleCancellation');});
});
