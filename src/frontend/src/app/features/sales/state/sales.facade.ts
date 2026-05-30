import { inject, Injectable, Signal } from '@angular/core';
import { Store } from '@ngrx/store';

import type {
  PreviewSaleReturnRequest,
  ProfitLossAppliedFiltersDto,
  ProfitLossReportQueryParams,
  RecordSaleReturnRequest,
  RecordSaleRequest,
  SaleDto,
  SaleListItemDto,
  ProfitLossReportItemDto,
  ProfitLossSummaryDto,
  SalesHistoryQueryParams,
  SalesHistorySummaryDto,
  SaleReturnPreviewDto,
  VoidSaleReturnRequest,
} from '../services/sale.models';
import { SalesActions } from './sales.actions';
import * as SalesSelectors from './sales.selectors';

@Injectable({ providedIn: 'root' })
export class SalesFacade {
  private readonly store = inject(Store);

  readonly allSales: Signal<readonly SaleListItemDto[]> = this.store.selectSignal(SalesSelectors.selectAllSales);
  readonly loadingSales: Signal<boolean> = this.store.selectSignal(SalesSelectors.selectLoadingSales);
  readonly submitting: Signal<boolean> = this.store.selectSignal(SalesSelectors.selectSubmitting);
  readonly errorMessage: Signal<string> = this.store.selectSignal(SalesSelectors.selectErrorMessage);
  readonly lastMutationType: Signal<'record-sale' | 'record-return' | 'void-return' | null> = this.store.selectSignal(SalesSelectors.selectLastMutationType);
  readonly lastMutationSucceeded: Signal<boolean> = this.store.selectSignal(SalesSelectors.selectLastMutationSucceeded);
  readonly selectedSale: Signal<SaleDto | null> = this.store.selectSignal(SalesSelectors.selectSelectedSale);
  readonly loadingSaleDetail: Signal<boolean> = this.store.selectSignal(SalesSelectors.selectLoadingSaleDetail);
  readonly returnPreview: Signal<SaleReturnPreviewDto | null> = this.store.selectSignal(SalesSelectors.selectReturnPreview);
  readonly loadingReturnPreview: Signal<boolean> = this.store.selectSignal(SalesSelectors.selectLoadingReturnPreview);
  readonly returnPreviewErrorMessage: Signal<string> = this.store.selectSignal(SalesSelectors.selectReturnPreviewErrorMessage);
  readonly profitLossItems: Signal<readonly ProfitLossReportItemDto[]> = this.store.selectSignal(SalesSelectors.selectProfitLossItems);
  readonly profitLossReport: Signal<readonly ProfitLossReportItemDto[]> = this.profitLossItems;
  readonly loadingProfitLossReport: Signal<boolean> = this.store.selectSignal(SalesSelectors.selectLoadingProfitLossReport);
  readonly profitLossSummary: Signal<ProfitLossSummaryDto | null> = this.store.selectSignal(SalesSelectors.selectProfitLossSummary);
  readonly profitLossAppliedFilters: Signal<ProfitLossAppliedFiltersDto | null> = this.store.selectSignal(SalesSelectors.selectProfitLossAppliedFilters);
  readonly profitLossPagination: Signal<{
    totalCount: number;
    pageNumber: number;
    pageSize: number;
  }> = this.store.selectSignal(SalesSelectors.selectProfitLossPagination);
  readonly lastRecordedSale: Signal<SaleDto | null> = this.store.selectSignal(SalesSelectors.selectLastRecordedSale);
  readonly salesPagination: Signal<{
    totalCount: number;
    pageNumber: number;
    pageSize: number;
  }> = this.store.selectSignal(SalesSelectors.selectSalesPagination);
  readonly salesHistorySummary: Signal<SalesHistorySummaryDto | null> = this.store.selectSignal(SalesSelectors.selectSalesHistorySummary);

  loadSales(params?: SalesHistoryQueryParams): void {
    this.store.dispatch(SalesActions.loadSalesRequested(params ? { queryParams: params } : {}));
  }

  loadProfitLossReport(params?: ProfitLossReportQueryParams): void {
    this.store.dispatch(SalesActions.loadProfitLossReportRequested(params ? { queryParams: params } : {}));
  }

  loadSaleDetail(saleId: string): void {
    this.store.dispatch(SalesActions.loadSaleDetailRequested({ saleId }));
  }

  previewSaleReturn(saleId: string, payload: PreviewSaleReturnRequest): void {
    this.store.dispatch(SalesActions.previewSaleReturnRequested({ saleId, payload }));
  }

  recordSaleReturn(saleId: string, payload: RecordSaleReturnRequest): void {
    this.store.dispatch(SalesActions.recordSaleReturnRequested({ saleId, payload }));
  }

  voidSaleReturn(saleId: string, saleReturnId: string, payload: VoidSaleReturnRequest): void {
    this.store.dispatch(SalesActions.voidSaleReturnRequested({ saleId, saleReturnId, payload }));
  }

  recordSale(payload: RecordSaleRequest): void {
    this.store.dispatch(SalesActions.recordSaleRequested({ payload }));
  }

  clearError(): void {
    this.store.dispatch(SalesActions.clearError());
  }

  clearMutationStatus(): void {
    this.store.dispatch(SalesActions.clearMutationStatus());
  }

  clearSaleDetail(): void {
    this.store.dispatch(SalesActions.clearSaleDetail());
  }

  clearSaleReturnPreview(): void {
    this.store.dispatch(SalesActions.clearSaleReturnPreview());
  }

  clearLastRecordedSale(): void {
    this.store.dispatch(SalesActions.clearLastRecordedSale());
  }
}
