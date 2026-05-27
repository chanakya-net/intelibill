import { createEntityAdapter, EntityState } from '@ngrx/entity';
import { createFeature, createReducer, on } from '@ngrx/store';

import type { SaleDto, SaleListItemDto, ProfitLossReportItemDto, SaleReturnPreviewDto, SalesHistorySummaryDto } from '../services/sale.models';
import { SaleMutationType, SalesActions } from './sales.actions';

export const salesFeatureKey = 'sales';

export const salesAdapter = createEntityAdapter<SaleListItemDto>({
  selectId: (sale) => sale.saleId,
  sortComparer: (left, right) =>
    new Date(right.soldAt).getTime() - new Date(left.soldAt).getTime(),
});

export interface SalesState extends EntityState<SaleListItemDto> {
  readonly loadingSales: boolean;
  readonly submitting: boolean;
  readonly errorMessage: string;
  readonly lastMutationType: SaleMutationType | null;
  readonly lastMutationSucceeded: boolean;
  readonly selectedSale: SaleDto | null;
  readonly loadingSaleDetail: boolean;
  readonly returnPreview: SaleReturnPreviewDto | null;
  readonly loadingReturnPreview: boolean;
  readonly returnPreviewErrorMessage: string;
  readonly profitLossReport: readonly ProfitLossReportItemDto[];
  readonly loadingProfitLossReport: boolean;
  readonly lastRecordedSale: SaleDto | null;
  readonly totalCount: number;
  readonly pageNumber: number;
  readonly pageSize: number;
  readonly historySummary: SalesHistorySummaryDto | null;
}

const initialState: SalesState = salesAdapter.getInitialState({
  loadingSales: false,
  submitting: false,
  errorMessage: '',
  lastMutationType: null,
  lastMutationSucceeded: false,
  selectedSale: null,
  loadingSaleDetail: false,
  returnPreview: null,
  loadingReturnPreview: false,
  returnPreviewErrorMessage: '',
  profitLossReport: [],
  loadingProfitLossReport: false,
  lastRecordedSale: null,
  totalCount: 0,
  pageNumber: 1,
  pageSize: 0,
  historySummary: null,
});

export const salesReducer = createReducer(
  initialState,
  on(SalesActions.loadSalesRequested, (state) => ({
    ...state,
    loadingSales: true,
    errorMessage: '',
  })),
  on(
    SalesActions.loadSalesSucceeded,
    (state, { sales, totalCount, pageNumber, pageSize, summary }) =>
    salesAdapter.setAll([...sales], {
      ...state,
      loadingSales: false,
      errorMessage: '',
      totalCount,
      pageNumber,
      pageSize,
      historySummary: summary,
    })
  ),
  on(SalesActions.loadSalesFailed, (state, { errorMessage }) => ({
    ...state,
    loadingSales: false,
    errorMessage,
  })),

  on(SalesActions.loadProfitLossReportRequested, (state) => ({
    ...state,
    loadingProfitLossReport: true,
    errorMessage: '',
  })),
  on(SalesActions.loadProfitLossReportSucceeded, (state, { report }) => ({
    ...state,
    loadingProfitLossReport: false,
    profitLossReport: report,
    errorMessage: '',
  })),
  on(SalesActions.loadProfitLossReportFailed, (state, { errorMessage }) => ({
    ...state,
    loadingProfitLossReport: false,
    errorMessage,
  })),

  on(SalesActions.loadSaleDetailRequested, (state) => ({
    ...state,
    loadingSaleDetail: true,
    errorMessage: '',
    selectedSale: null,
    returnPreview: null,
    returnPreviewErrorMessage: '',
  })),
  on(SalesActions.loadSaleDetailSucceeded, (state, { sale }) => ({
    ...state,
    loadingSaleDetail: false,
    selectedSale: sale,
  })),
  on(SalesActions.loadSaleDetailFailed, (state, { errorMessage }) => ({
    ...state,
    loadingSaleDetail: false,
    errorMessage,
  })),

  on(SalesActions.previewSaleReturnRequested, (state) => ({
    ...state,
    loadingReturnPreview: true,
    returnPreview: null,
    returnPreviewErrorMessage: '',
  })),
  on(SalesActions.previewSaleReturnSucceeded, (state, { preview }) => ({
    ...state,
    loadingReturnPreview: false,
    returnPreview: preview,
    returnPreviewErrorMessage: '',
  })),
  on(SalesActions.previewSaleReturnFailed, (state, { errorMessage }) => ({
    ...state,
    loadingReturnPreview: false,
    returnPreviewErrorMessage: errorMessage,
  })),

  on(SalesActions.recordSaleReturnRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    returnPreviewErrorMessage: '',
    lastMutationType: 'record-return' as SaleMutationType,
    lastMutationSucceeded: false,
  })),
  on(SalesActions.recordSaleReturnSucceeded, (state, { sale }) => ({
    ...state,
    submitting: false,
    selectedSale: sale,
    returnPreview: null,
    returnPreviewErrorMessage: '',
    lastMutationType: 'record-return' as SaleMutationType,
    lastMutationSucceeded: true,
  })),
  on(SalesActions.recordSaleReturnFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    returnPreviewErrorMessage: errorMessage,
    lastMutationType: 'record-return' as SaleMutationType,
    lastMutationSucceeded: false,
  })),

  on(SalesActions.voidSaleReturnRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    returnPreviewErrorMessage: '',
    lastMutationType: 'void-return' as SaleMutationType,
    lastMutationSucceeded: false,
  })),
  on(SalesActions.voidSaleReturnSucceeded, (state, { sale }) => ({
    ...state,
    submitting: false,
    selectedSale: sale,
    returnPreviewErrorMessage: '',
    lastMutationType: 'void-return' as SaleMutationType,
    lastMutationSucceeded: true,
  })),
  on(SalesActions.voidSaleReturnFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    returnPreviewErrorMessage: errorMessage,
    lastMutationType: 'void-return' as SaleMutationType,
    lastMutationSucceeded: false,
  })),

  on(SalesActions.recordSaleRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'record-sale' as SaleMutationType,
    lastMutationSucceeded: false,
  })),
  on(SalesActions.recordSaleSucceeded, (state, { sale }) => ({
    ...state,
    submitting: false,
    errorMessage: '',
    lastMutationType: 'record-sale' as SaleMutationType,
    lastMutationSucceeded: true,
    lastRecordedSale: sale,
  })),
  on(SalesActions.recordSaleFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'record-sale' as SaleMutationType,
    lastMutationSucceeded: false,
  })),

  on(SalesActions.clearError, (state) => ({
    ...state,
    errorMessage: '',
  })),
  on(SalesActions.clearMutationStatus, (state) => ({
    ...state,
    lastMutationType: null,
    lastMutationSucceeded: false,
  })),
  on(SalesActions.clearSaleDetail, (state) => ({
    ...state,
    selectedSale: null,
    returnPreview: null,
    returnPreviewErrorMessage: '',
  })),
  on(SalesActions.clearSaleReturnPreview, (state) => ({
    ...state,
    returnPreview: null,
    returnPreviewErrorMessage: '',
    loadingReturnPreview: false,
  })),
  on(SalesActions.clearLastRecordedSale, (state) => ({
    ...state,
    lastRecordedSale: null,
  }))
);

export const salesFeature = createFeature({
  name: salesFeatureKey,
  reducer: salesReducer,
});
