import { createEntityAdapter, EntityState } from '@ngrx/entity';
import { createFeature, createReducer, on } from '@ngrx/store';

import { SaleDto, SaleListItemDto } from '../services/sale.service';
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
}

const initialState: SalesState = salesAdapter.getInitialState({
  loadingSales: false,
  submitting: false,
  errorMessage: '',
  lastMutationType: null,
  lastMutationSucceeded: false,
  selectedSale: null,
  loadingSaleDetail: false,
});

export const salesReducer = createReducer(
  initialState,
  on(SalesActions.loadSalesRequested, (state) => ({
    ...state,
    loadingSales: true,
    errorMessage: '',
  })),
  on(SalesActions.loadSalesSucceeded, (state, { sales }) =>
    salesAdapter.setAll([...sales], {
      ...state,
      loadingSales: false,
      errorMessage: '',
    })
  ),
  on(SalesActions.loadSalesFailed, (state, { errorMessage }) => ({
    ...state,
    loadingSales: false,
    errorMessage,
  })),

  on(SalesActions.loadSaleDetailRequested, (state) => ({
    ...state,
    loadingSaleDetail: true,
    errorMessage: '',
    selectedSale: null,
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

  on(SalesActions.recordSaleRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'record-sale' as SaleMutationType,
    lastMutationSucceeded: false,
  })),
  on(SalesActions.recordSaleSucceeded, (state) => ({
    ...state,
    submitting: false,
    errorMessage: '',
    lastMutationType: 'record-sale' as SaleMutationType,
    lastMutationSucceeded: true,
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
  }))
);

export const salesFeature = createFeature({
  name: salesFeatureKey,
  reducer: salesReducer,
});
