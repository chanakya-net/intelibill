import { signal, Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { vi } from 'vitest';

import type {
  ProfitLossAppliedFiltersDto,
  ProfitLossReportItemDto,
  ProfitLossReportQueryParams,
  ProfitLossSummaryDto,
  SalesHistorySummaryDto,
  SalesHistoryQueryParams,
  SaleListItemDto,
} from '../services/sale.models';
import { SalesActions } from './sales.actions';
import { SalesFacade } from './sales.facade';
import {
  selectAllSales,
  selectProfitLossAppliedFilters,
  selectProfitLossItems,
  selectProfitLossPagination,
  selectProfitLossSummary,
  selectSalesHistorySummary,
  selectSalesPagination,
  selectErrorMessage,
  selectLastMutationSucceeded,
  selectLastMutationType,
  selectLoadingProfitLossReport,
  selectLoadingReturnPreview,
  selectLoadingSaleDetail,
  selectLoadingSales,
  selectReturnPreview,
  selectReturnPreviewErrorMessage,
  selectSelectedSale,
  selectSubmitting,
} from './sales.selectors';

describe('SalesFacade', () => {
  const dispatch = vi.fn();
  const salesSignal = signal<SaleListItemDto[]>([]);
  const boolSignal = signal(false);
  const errorSignal = signal('');
  const returnPreviewErrorSignal = signal('');
  const mutationTypeSignal = signal<'record-sale' | 'record-return' | 'void-return' | null>(null);
  const selectedSaleSignal = signal(null);
  const returnPreviewSignal = signal(null);
  const summarySignal = signal<SalesHistorySummaryDto | null>({
    periodSales: 5000,
    invoiceCount: 17,
    refundAmount: 100,
  });
  const profitLossItemsSignal = signal<readonly ProfitLossReportItemDto[]>([]);
  const profitLossSummarySignal = signal<ProfitLossSummaryDto | null>({
    netProfitAfterTax: 100,
    revenueIncludingTax: 200,
    totalCost: 100,
    averageMarginPercent: 50,
    invoiceCount: 1,
    returnCount: 0,
    adjustmentCount: 0,
  });
  const profitLossAppliedFiltersSignal = signal<ProfitLossAppliedFiltersDto | null>({
    from: '2026-05-01',
    to: '2026-05-31',
    type: 'all',
    search: null,
    pageNumber: 1,
    pageSize: 20,
  });
  const profitLossPaginationSignal = signal({
    totalCount: 17,
    pageNumber: 2,
    pageSize: 20,
  });
  const paginationSignal = signal({
    totalCount: 17,
    pageNumber: 2,
    pageSize: 20,
  });

  const store = {
    dispatch,
    selectSignal: vi.fn((selector: unknown): Signal<unknown> => {
      if (selector === selectAllSales) return salesSignal;
      if (selector === selectLoadingSales) return boolSignal;
      if (selector === selectSubmitting) return boolSignal;
      if (selector === selectErrorMessage) return errorSignal;
      if (selector === selectLastMutationType) return mutationTypeSignal;
      if (selector === selectLastMutationSucceeded) return boolSignal;
      if (selector === selectSelectedSale) return selectedSaleSignal;
      if (selector === selectLoadingSaleDetail) return boolSignal;
      if (selector === selectReturnPreview) return returnPreviewSignal;
      if (selector === selectLoadingReturnPreview) return boolSignal;
      if (selector === selectReturnPreviewErrorMessage) return returnPreviewErrorSignal;
      if (selector === selectProfitLossItems) return profitLossItemsSignal;
      if (selector === selectProfitLossSummary) return profitLossSummarySignal;
      if (selector === selectProfitLossAppliedFilters) return profitLossAppliedFiltersSignal;
      if (selector === selectLoadingProfitLossReport) return boolSignal;
      if (selector === selectProfitLossPagination) return profitLossPaginationSignal;
      if (selector === selectSalesHistorySummary) return summarySignal;
      if (selector === selectSalesPagination) return paginationSignal;
      return signal(null);
    }),
  };

  let facade: SalesFacade;

  beforeEach(() => {
    dispatch.mockReset();
    TestBed.configureTestingModule({
      providers: [SalesFacade, { provide: Store, useValue: store }],
    });
    facade = TestBed.inject(SalesFacade);
  });

  it('loadSales dispatches loadSalesRequested', () => {
    facade.loadSales();
    expect(dispatch).toHaveBeenCalledWith(SalesActions.loadSalesRequested({}));
  });

  it('loadSales dispatches loadSalesRequested with query params', () => {
    const queryParams: SalesHistoryQueryParams = {
      from: '2026-05-01',
      to: '2026-05-20',
      search: 'john',
      status: 'partiallyPaid',
      page: 1,
      pageSize: 30,
    };

    facade.loadSales(queryParams);
    expect(dispatch).toHaveBeenCalledWith(
      SalesActions.loadSalesRequested({ queryParams })
    );
  });

  it('loadProfitLossReport dispatches loadProfitLossReportRequested', () => {
    facade.loadProfitLossReport();
    expect(dispatch).toHaveBeenCalledWith(SalesActions.loadProfitLossReportRequested({}));
  });

  it('loadProfitLossReport dispatches loadProfitLossReportRequested with query params', () => {
    const queryParams: ProfitLossReportQueryParams = {
      from: '2026-05-01',
      to: '2026-05-31',
      type: 'sale',
      search: 'INV',
      page: 2,
      pageSize: 15,
    };

    facade.loadProfitLossReport(queryParams);
    expect(dispatch).toHaveBeenCalledWith(SalesActions.loadProfitLossReportRequested({ queryParams }));
  });

  it('loadSaleDetail dispatches loadSaleDetailRequested', () => {
    facade.loadSaleDetail('s1');
    expect(dispatch).toHaveBeenCalledWith(SalesActions.loadSaleDetailRequested({ saleId: 's1' }));
  });

  it('previewSaleReturn dispatches previewSaleReturnRequested', () => {
    const payload = { dueReductionOverrideAmount: null, dueOverrideReason: null, items: [] };
    facade.previewSaleReturn('s1', payload);
    expect(dispatch).toHaveBeenCalledWith(SalesActions.previewSaleReturnRequested({ saleId: 's1', payload }));
  });

  it('recordSaleReturn dispatches recordSaleReturnRequested', () => {
    const payload = { payoutMethod: 1, dueReductionOverrideAmount: null, dueOverrideReason: null, notes: null, items: [] };
    facade.recordSaleReturn('s1', payload);
    expect(dispatch).toHaveBeenCalledWith(SalesActions.recordSaleReturnRequested({ saleId: 's1', payload }));
  });

  it('voidSaleReturn dispatches voidSaleReturnRequested', () => {
    const payload = { reason: 'Wrong return recorded' };
    facade.voidSaleReturn('s1', 'return-1', payload);
    expect(dispatch).toHaveBeenCalledWith(SalesActions.voidSaleReturnRequested({ saleId: 's1', saleReturnId: 'return-1', payload }));
  });

  it('clearError dispatches clearError', () => {
    facade.clearError();
    expect(dispatch).toHaveBeenCalledWith(SalesActions.clearError());
  });

  it('clearMutationStatus dispatches clearMutationStatus', () => {
    facade.clearMutationStatus();
    expect(dispatch).toHaveBeenCalledWith(SalesActions.clearMutationStatus());
  });

  it('clearSaleDetail dispatches clearSaleDetail', () => {
    facade.clearSaleDetail();
    expect(dispatch).toHaveBeenCalledWith(SalesActions.clearSaleDetail());
  });

  it('clearSaleReturnPreview dispatches clearSaleReturnPreview', () => {
    facade.clearSaleReturnPreview();
    expect(dispatch).toHaveBeenCalledWith(SalesActions.clearSaleReturnPreview());
  });

  it('clearLastRecordedSale dispatches clearLastRecordedSale', () => {
    facade.clearLastRecordedSale();
    expect(dispatch).toHaveBeenCalledWith(SalesActions.clearLastRecordedSale());
  });

  it('exposes profit loss selectors', () => {
    expect(facade.profitLossItems()).toEqual([]);
    expect(facade.profitLossSummary()).toEqual({
      netProfitAfterTax: 100,
      revenueIncludingTax: 200,
      totalCost: 100,
      averageMarginPercent: 50,
      invoiceCount: 1,
      returnCount: 0,
      adjustmentCount: 0,
    });
    expect(facade.profitLossAppliedFilters()).toEqual({
      from: '2026-05-01',
      to: '2026-05-31',
      type: 'all',
      search: null,
      pageNumber: 1,
      pageSize: 20,
    });
    expect(facade.profitLossPagination()).toEqual({
      totalCount: 17,
      pageNumber: 2,
      pageSize: 20,
    });
  });
});
