import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { TranslocoService, TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { HttpResponse } from '@angular/common/http';
import { of } from 'rxjs';

import type { SaleHistoryStatus, SaleListItemDto } from '../services/sale.models';
import { SalesFacade } from '../state/sales.facade';
import { OfflineSalesQueueSyncService } from '../services/offline-sales-queue-sync.service';
import { SaleDetailOverlayComponent } from '../components/sale-detail-overlay.component';
import { SalesExportService } from '../services/sales-export.service';
import { SalesPageComponent } from './sales-page.component';
import { formatLocalIsoDate } from '../../../shared/utils/date-time.util';

@Component({ selector: 'app-sale-detail-overlay', standalone: true, template: '' })
class SaleDetailOverlayStubComponent {
  @Input() visible = false;
  @Output() visibleChange = new EventEmitter<boolean>();
}

describe('SalesPageComponent', () => {
  const salesSignal = signal<readonly SaleListItemDto[]>([]);
  const loadingSignal = signal(false);
  const errorSignal = signal('');
  const salesPaginationSignal = signal({ totalCount: 0, pageNumber: 1, pageSize: 20 });
  const salesHistorySummarySignal = signal({ periodSales: 0, invoiceCount: 0, refundAmount: 0 });

  const salesFacade = {
    allSales: salesSignal,
    loadingSales: loadingSignal,
    errorMessage: errorSignal,
    salesPagination: salesPaginationSignal,
    salesHistorySummary: salesHistorySummarySignal,
    loadSales: vi.fn(),
    loadSaleDetail: vi.fn(),
    clearSaleDetail: vi.fn(),
  };

  const salesExportService = {
    exportSales: vi.fn(),
    extractFilename: vi.fn(),
    triggerDownload: vi.fn(),
  };

  const offlineCountsSignal = signal({
    pending: 0,
    syncing: 0,
    failed: 0,
    warning: 0,
    needsReview: 0,
    totalVisible: 0,
  });

  const offlineSalesQueueSync = {
    visibleCounts: offlineCountsSignal,
    cleanupSyncedRecords: vi.fn().mockResolvedValue(0),
    refreshActiveStatusCounts: vi.fn().mockResolvedValue(offlineCountsSignal()),
    retryActiveShop: vi.fn().mockResolvedValue({
      attemptedCount: 0,
      syncedCount: 0,
      warningCount: 0,
      needsReviewCount: 0,
      failedCount: 0,
      skippedReason: null,
    }),
  };

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-05-27T10:00:00.000Z'));

    salesSignal.set([]);
    loadingSignal.set(false);
    errorSignal.set('');
    salesPaginationSignal.set({ totalCount: 0, pageNumber: 1, pageSize: 20 });
    salesHistorySummarySignal.set({ periodSales: 0, invoiceCount: 0, refundAmount: 0 });
    offlineCountsSignal.set({
      pending: 0,
      syncing: 0,
      failed: 0,
      warning: 0,
      needsReview: 0,
      totalVisible: 0,
    });

    salesFacade.loadSales.mockReset();
    salesFacade.loadSaleDetail.mockReset();
    salesFacade.clearSaleDetail.mockReset();
    salesExportService.exportSales.mockReset();
    salesExportService.extractFilename.mockReset();
    salesExportService.triggerDownload.mockReset();
    salesExportService.extractFilename.mockReturnValue('sales-export.xlsx');
    offlineSalesQueueSync.cleanupSyncedRecords.mockClear();
    offlineSalesQueueSync.refreshActiveStatusCounts.mockClear();
    offlineSalesQueueSync.retryActiveShop.mockClear();

    TestBed.configureTestingModule({
      imports: [
        SalesPageComponent,
        TranslocoTestingModule.forRoot({
          preloadLangs: true,
          translocoConfig: {
            availableLangs: ['en-IN'],
            defaultLang: 'en-IN',
            reRenderOnLangChange: true,
          },
          langs: {
            'en-IN': {
              sales: {
                history: {
                  actions: {
                    viewReceipt: 'View Receipt',
                  },
                },
              },
            },
          },
        }),
      ],
      providers: [
        { provide: SalesFacade, useValue: salesFacade },
        { provide: OfflineSalesQueueSyncService, useValue: offlineSalesQueueSync },
        { provide: SalesExportService, useValue: salesExportService },
      ],
    });

    TestBed.overrideComponent(SalesPageComponent, {
      remove: {
        imports: [SaleDetailOverlayComponent],
      },
      add: {
        imports: [SaleDetailOverlayStubComponent],
      },
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });

  it('loads sales with default query params', () => {
    const fixture = TestBed.createComponent(SalesPageComponent);
    fixture.detectChanges();
    vi.runOnlyPendingTimers();

    const baseDate = new Date();
    const expectedFrom = new Date(baseDate);
    expectedFrom.setDate(expectedFrom.getDate() - 30);
    expectedFrom.setHours(0, 0, 0, 0);
    const expectedTo = new Date(baseDate);
    expectedTo.setHours(23, 59, 59, 999);

    expect(salesFacade.loadSales).toHaveBeenCalled();
    expect(salesFacade.loadSales).toHaveBeenLastCalledWith({
      from: formatLocalIsoDate(expectedFrom),
      to: formatLocalIsoDate(expectedTo),
      search: undefined,
      status: undefined,
      page: 1,
      pageSize: 20,
    });
  });

  it('debounces search and reloads', () => {
    const fixture = TestBed.createComponent(SalesPageComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();
    vi.runOnlyPendingTimers();
    salesFacade.loadSales.mockClear();

    component.searchValue.set('INV-42');
    fixture.detectChanges();

    vi.advanceTimersByTime(299);
    expect(salesFacade.loadSales).not.toHaveBeenCalled();

    vi.advanceTimersByTime(2);
    expect(salesFacade.loadSales).toHaveBeenCalled();
    expect(salesFacade.loadSales.mock.calls.at(-1)?.[0]).toMatchObject({ search: 'INV-42', page: 1 });
  });

  it('resets page number when status filter changes', () => {
    const fixture = TestBed.createComponent(SalesPageComponent);
    const component = fixture.componentInstance;

    component.pageNumber.set(3);
    component.statusFilter.set('paid');
    fixture.detectChanges();

    expect(component.pageNumber()).toBe(1);
    expect(salesFacade.loadSales.mock.calls.at(-1)?.[0]).toMatchObject({ status: 'paid', page: 1 });
  });

  it('exports the current report level and date range', () => {
    const response = new HttpResponse({ status: 200, body: new Blob(['sales']) });
    salesExportService.exportSales.mockReturnValue(of(response));

    const fixture = TestBed.createComponent(SalesPageComponent);
    const component = fixture.componentInstance;

    const fromDate = new Date(2026, 4, 1);
    const toDate = new Date(2026, 4, 13);
    component.fromDate.set(fromDate);
    component.toDate.set(toDate);
    component.reportLevel.set('lineItems');
    fixture.detectChanges();

    const exportButton = fixture.nativeElement.querySelector('button[data-export-format="pdf"]') as HTMLButtonElement;
    exportButton.click();
    fixture.detectChanges();

    expect(salesExportService.exportSales).toHaveBeenCalledWith({
      format: 'pdf',
      level: 'lineItems',
      startDate: formatLocalIsoDate(fromDate),
      endDate: formatLocalIsoDate(toDate),
    });
    expect(salesExportService.triggerDownload).toHaveBeenCalledWith(response.body!, 'sales-export.xlsx');
  });

  it('clearFilters resets controls to defaults', () => {
    const fixture = TestBed.createComponent(SalesPageComponent);
    const component = fixture.componentInstance;

    component.searchValue.set('abc');
    component.statusFilter.set('refunded');
    component.pageNumber.set(4);
    component.pageSize.set(50);
    component.reportLevel.set('lineItems');
    component.clearFilters();

    expect(component.searchValue()).toBe('');
    expect(component.statusFilter()).toBe('all');
    expect(component.pageNumber()).toBe(1);
    expect(component.pageSize()).toBe(20);
    expect(component.reportLevel()).toBe('summary');
  });

  it('opens sale detail overlay from View Receipt action', () => {
    const transloco = TestBed.inject(TranslocoService);
    transloco.setActiveLang('en-IN');

    const fixture = TestBed.createComponent(SalesPageComponent);
    const component = fixture.componentInstance;

    const sale: SaleListItemDto = {
      saleId: 'sale-1',
      invoiceNumber: 'INV-001',
      customerId: null,
      paymentMethod: 1,
      soldAt: new Date().toISOString(),
      paidAmount: 100,
      dueAmount: 0,
      totalBeforeDiscount: 100,
      totalDiscountAmount: 0,
      totalAmount: 100,
      totalTaxAmount: 0,
      customerName: null,
      customerPhone: null,
      itemCount: 2,
      returnNumbers: [],
      status: 'paid' satisfies SaleHistoryStatus,
      refundAmount: 0,
      dueReductionAmount: 0,
    };
    salesSignal.set([sale]);
    fixture.detectChanges();

    const receiptButton = fixture.debugElement.query(By.css('.receipt-btn'));
    expect(receiptButton).toBeTruthy();
    expect(receiptButton.nativeElement.tagName).toBe('BUTTON');
    expect(receiptButton.nativeElement.textContent).toContain('View Receipt');
    expect(receiptButton.nativeElement.textContent).not.toContain('→');
    expect(receiptButton.nativeElement.querySelector('.pi-arrow-right')).toBeTruthy();

    receiptButton.triggerEventHandler('click');
    fixture.detectChanges();

    expect(salesFacade.loadSaleDetail).toHaveBeenCalledWith('sale-1');
    expect(component.showDetailOverlay()).toBe(true);
    expect(component.viewingSaleId()).toBe('sale-1');
  });

  it('shows pagination label with range', () => {
    const fixture = TestBed.createComponent(SalesPageComponent);
    salesPaginationSignal.set({ totalCount: 42, pageNumber: 1, pageSize: 20 });
    fixture.detectChanges();

    const showing = fixture.nativeElement.querySelector('.showing');
    expect(showing).toBeTruthy();
  });

  describe('Layout regression coverage', () => {
    it('header layout: sales-ledger-header exists with flex layout', () => {
      const fixture = TestBed.createComponent(SalesPageComponent);
      fixture.detectChanges();

      const header = fixture.nativeElement.querySelector('.sales-ledger-header');
      expect(header).toBeTruthy();
      const style = window.getComputedStyle(header);
      expect(style.display).toBe('flex');
    });

    it('controls layout: export toolbar sits beside report level controls', () => {
      const fixture = TestBed.createComponent(SalesPageComponent);
      fixture.detectChanges();

      const controlsRow = fixture.nativeElement.querySelector('.controls-row');
      const toolbar = controlsRow.querySelector('app-sales-export-toolbar.inline-export');
      expect(toolbar).toBeTruthy();
      const style = window.getComputedStyle(toolbar);
      expect(style.display).toBe('block');
    });

    it('filters layout: clear filters is grouped with the search box', () => {
      const fixture = TestBed.createComponent(SalesPageComponent);
      fixture.detectChanges();

      const searchActions = fixture.nativeElement.querySelector('.search-actions');
      expect(searchActions).toBeTruthy();

      const searchField = searchActions.querySelector('.search-field');
      const button = searchActions.querySelector('.clear-filters-btn');
      expect(searchField).toBeTruthy();
      expect(button).toBeTruthy();
      const style = window.getComputedStyle(button);
      expect(style.marginLeft).not.toBe('auto');
    });

    it('search-field element present with appropriate width', () => {
      const fixture = TestBed.createComponent(SalesPageComponent);
      fixture.detectChanges();

      const searchField = fixture.nativeElement.querySelector('.search-field');
      expect(searchField).toBeTruthy();
      expect(searchField.querySelector('input[type="text"]')).toBeTruthy();
    });

    it('filters-row contains segmented control and search-field without overflow', () => {
      const fixture = TestBed.createComponent(SalesPageComponent);
      fixture.detectChanges();

      const filtersRow = fixture.nativeElement.querySelector('.filters-row');
      expect(filtersRow).toBeTruthy();

      const segmented = filtersRow.querySelector('.segmented');
      const searchActions = filtersRow.querySelector('.search-actions');
      expect(segmented).toBeTruthy();
      expect(searchActions).toBeTruthy();
      expect(searchActions.querySelector('.search-field')).toBeTruthy();
      expect(searchActions.querySelector('.clear-filters-btn')).toBeTruthy();
    });
  });
});
