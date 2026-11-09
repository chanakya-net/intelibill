import type { LocalizedError } from '../../../core/i18n/api-error-message.service';
import { createEntityAdapter, EntityState } from '@ngrx/entity';
import { createFeature, createReducer, on } from '@ngrx/store';

import type {
  ProfitLossAppliedFiltersDto,
  ProfitLossReportItemDto,
  ProfitLossSummaryDto,
  SaleDto,
  SaleListItemDto,
  SaleReturnPreviewDto,
  SalesHistorySummaryDto,
} from '../services/sale.models';
import { SaleMutationType, SalesActions } from './sales.actions';

export const salesFeatureKey = 'sales';

export const salesAdapter = createEntityAdapter<SaleListItemDto>({
  selectId: (sale) => sale.saleId,
  sortComparer: (left, right) => new Date(right.soldAt).getTime() - new Date(left.soldAt).getTime(),
});

export interface SalesState extends EntityState<SaleListItemDto> {
  readonly loadingSales: boolean;
  readonly submitting: boolean;
  readonly localizedError: LocalizedError | undefined;
  readonly errorMessage: string;
  readonly lastMutationType: SaleMutationType | null;
  readonly lastMutationSucceeded: boolean;
  readonly selectedSale: SaleDto | null;
  readonly loadingSaleDetail: boolean;
  readonly returnPreview: SaleReturnPreviewDto | null;
  readonly loadingReturnPreview: boolean;
  readonly returnPreviewLocalizedError: LocalizedError | undefined;
  readonly returnPreviewErrorMessage: string;
  readonly profitLossItems: readonly ProfitLossReportItemDto[];
  readonly loadingProfitLossReport: boolean;
  readonly profitLossSummary: ProfitLossSummaryDto | null;
  readonly profitLossAppliedFilters: ProfitLossAppliedFiltersDto | null;
  readonly profitLossTotalCount: number;
  readonly profitLossPageNumber: number;
  readonly profitLossPageSize: number;
  readonly lastRecordedSale: SaleDto | null;
  /** Checkout intent that produced `lastRecordedSale`, so cleanup only touches its own cart. */
  readonly lastRecordedIntentId: string | null;
  readonly totalCount: number;
  readonly pageNumber: number;
  readonly pageSize: number;
  readonly historySummary: SalesHistorySummaryDto | null;
}

const initialState: SalesState = salesAdapter.getInitialState({
  loadingSales: false,
  submitting: false,
  localizedError: undefined,
  errorMessage: '',
  lastMutationType: null,
  lastMutationSucceeded: false,
  selectedSale: null,
  loadingSaleDetail: false,
  returnPreview: null,
  loadingReturnPreview: false,
  returnPreviewLocalizedError: undefined,
  returnPreviewErrorMessage: '',
  profitLossItems: [],
  loadingProfitLossReport: false,
  profitLossSummary: null,
  profitLossAppliedFilters: null,
  profitLossTotalCount: 0,
  profitLossPageNumber: 1,
  profitLossPageSize: 0,
  lastRecordedSale: null,
  lastRecordedIntentId: null,
  totalCount: 0,
  pageNumber: 1,
  pageSize: 20,
  historySummary: null,
});

export const salesReducer = createReducer(
  initialState,
  on(SalesActions.loadSalesRequested, (state) => ({
    ...state,
    loadingSales: true,
    localizedError: undefined,
    errorMessage: '',
  })),
  on(
    SalesActions.loadSalesSucceeded,
    (state, { sales, totalCount, pageNumber, pageSize, summary }) =>
      salesAdapter.setAll([...sales], {
        ...state,
        loadingSales: false,
        localizedError: undefined,
        errorMessage: '',
        totalCount,
        pageNumber,
        pageSize,
        historySummary: summary,
      }),
  ),
  on(SalesActions.loadSalesFailed, (state, { errorMessage, localizedError }) => ({
    ...state,
    loadingSales: false,
    localizedError,
    errorMessage,
  })),

  on(SalesActions.loadProfitLossReportRequested, (state) => ({
    ...state,
    loadingProfitLossReport: true,
    localizedError: undefined,
    errorMessage: '',
  })),
  on(SalesActions.loadProfitLossReportSucceeded, (state, { result }) => ({
    ...state,
    loadingProfitLossReport: false,
    profitLossItems: result.items,
    profitLossSummary: result.summary,
    profitLossAppliedFilters: result.appliedFilters,
    profitLossTotalCount: result.totalCount,
    profitLossPageNumber: result.pageNumber,
    profitLossPageSize: result.pageSize,
    localizedError: undefined,
    errorMessage: '',
  })),
  on(SalesActions.loadProfitLossReportFailed, (state, { errorMessage, localizedError }) => ({
    ...state,
    loadingProfitLossReport: false,
    localizedError,
    errorMessage,
  })),

  on(SalesActions.loadSaleDetailRequested, (state) => ({
    ...state,
    loadingSaleDetail: true,
    localizedError: undefined,
    errorMessage: '',
    selectedSale: null,
    returnPreview: null,
    returnPreviewLocalizedError: undefined,
    returnPreviewErrorMessage: '',
  })),
  on(SalesActions.loadSaleDetailSucceeded, (state, { sale }) => ({
    ...state,
    loadingSaleDetail: false,
    selectedSale: sale,
  })),
  on(SalesActions.loadSaleDetailFailed, (state, { errorMessage, localizedError }) => ({
    ...state,
    loadingSaleDetail: false,
    localizedError,
    errorMessage,
  })),

  on(SalesActions.previewSaleReturnRequested, (state) => ({
    ...state,
    loadingReturnPreview: true,
    returnPreview: null,
    returnPreviewLocalizedError: undefined,
    returnPreviewErrorMessage: '',
  })),
  on(SalesActions.previewSaleReturnSucceeded, (state, { preview }) => ({
    ...state,
    loadingReturnPreview: false,
    returnPreview: preview,
    returnPreviewLocalizedError: undefined,
    returnPreviewErrorMessage: '',
  })),
  on(SalesActions.previewSaleReturnFailed, (state, { errorMessage, localizedError }) => ({
    ...state,
    loadingReturnPreview: false,
    returnPreviewLocalizedError: localizedError,
    returnPreviewErrorMessage: errorMessage,
  })),

  on(SalesActions.recordSaleReturnRequested, (state) => ({
    ...state,
    submitting: true,
    localizedError: undefined,
    errorMessage: '',
    returnPreviewLocalizedError: undefined,
    returnPreviewErrorMessage: '',
    lastMutationType: 'record-return' as SaleMutationType,
    lastMutationSucceeded: false,
  })),
  on(SalesActions.recordSaleReturnSucceeded, (state, { sale }) => ({
    ...state,
    submitting: false,
    selectedSale: sale,
    returnPreview: null,
    returnPreviewLocalizedError: undefined,
    returnPreviewErrorMessage: '',
    lastMutationType: 'record-return' as SaleMutationType,
    lastMutationSucceeded: true,
  })),
  on(SalesActions.recordSaleReturnFailed, (state, { errorMessage, localizedError }) => ({
    ...state,
    submitting: false,
    returnPreviewLocalizedError: localizedError,
    returnPreviewErrorMessage: errorMessage,
    lastMutationType: 'record-return' as SaleMutationType,
    lastMutationSucceeded: false,
  })),

  on(SalesActions.voidSaleReturnRequested, (state) => ({
    ...state,
    submitting: true,
    localizedError: undefined,
    errorMessage: '',
    returnPreviewLocalizedError: undefined,
    returnPreviewErrorMessage: '',
    lastMutationType: 'void-return' as SaleMutationType,
    lastMutationSucceeded: false,
  })),
  on(SalesActions.voidSaleReturnSucceeded, (state, { sale }) => ({
    ...state,
    submitting: false,
    selectedSale: sale,
    returnPreviewLocalizedError: undefined,
    returnPreviewErrorMessage: '',
    lastMutationType: 'void-return' as SaleMutationType,
    lastMutationSucceeded: true,
  })),
  on(SalesActions.voidSaleReturnFailed, (state, { errorMessage, localizedError }) => ({
    ...state,
    submitting: false,
    returnPreviewLocalizedError: localizedError,
    returnPreviewErrorMessage: errorMessage,
    lastMutationType: 'void-return' as SaleMutationType,
    lastMutationSucceeded: false,
  })),

  on(SalesActions.recordSaleRequested, (state) => ({
    ...state,
    submitting: true,
    localizedError: undefined,
    errorMessage: '',
    lastMutationType: 'record-sale' as SaleMutationType,
    lastMutationSucceeded: false,
  })),
  on(SalesActions.recordSaleSucceeded, (state, { sale, intentId }) => ({
    ...state,
    submitting: false,
    localizedError: undefined,
    errorMessage: '',
    lastMutationType: 'record-sale' as SaleMutationType,
    lastMutationSucceeded: true,
    lastRecordedSale: sale,
    lastRecordedIntentId: intentId ?? null,
  })),
  on(SalesActions.recordSaleUncertain, (state, { errorMessage, localizedError }) => ({
    ...state,
    submitting: false,
    localizedError,
    errorMessage,
    lastMutationType: 'record-sale' as SaleMutationType,
    lastMutationSucceeded: false,
  })),
  on(SalesActions.recordSaleFailed, (state, { errorMessage, localizedError }) => ({
    ...state,
    submitting: false,
    localizedError,
    errorMessage,
    lastMutationType: 'record-sale' as SaleMutationType,
    lastMutationSucceeded: false,
  })),

  on(SalesActions.clearError, (state) => ({
    ...state,
    localizedError: undefined,
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
    returnPreviewLocalizedError: undefined,
    returnPreviewErrorMessage: '',
  })),
  on(SalesActions.clearSaleReturnPreview, (state) => ({
    ...state,
    returnPreview: null,
    returnPreviewLocalizedError: undefined,
    returnPreviewErrorMessage: '',
    loadingReturnPreview: false,
  })),
  on(SalesActions.clearLastRecordedSale, (state) => ({
    ...state,
    lastRecordedSale: null,
    lastRecordedIntentId: null,
  })),
);

export const salesFeature = createFeature({
  name: salesFeatureKey,
  reducer: salesReducer,
});
