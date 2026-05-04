import { createSelector } from '@ngrx/store';

import { salesAdapter, salesFeature } from './sales.reducer';

const { selectAll, selectEntities } = salesAdapter.getSelectors();

export const selectAllSales = createSelector(
  salesFeature.selectSalesState,
  selectAll
);

export const selectSalesEntities = createSelector(
  salesFeature.selectSalesState,
  selectEntities
);

export const selectLoadingSales = salesFeature.selectLoadingSales;
export const selectSubmitting = salesFeature.selectSubmitting;
export const selectErrorMessage = salesFeature.selectErrorMessage;
export const selectLastMutationType = salesFeature.selectLastMutationType;
export const selectLastMutationSucceeded = salesFeature.selectLastMutationSucceeded;
export const selectSelectedSale = salesFeature.selectSelectedSale;
export const selectLoadingSaleDetail = salesFeature.selectLoadingSaleDetail;
export const selectReturnPreview = salesFeature.selectReturnPreview;
export const selectLoadingReturnPreview = salesFeature.selectLoadingReturnPreview;
export const selectReturnPreviewErrorMessage = salesFeature.selectReturnPreviewErrorMessage;
export const selectProfitLossReport = salesFeature.selectProfitLossReport;
export const selectLoadingProfitLossReport = salesFeature.selectLoadingProfitLossReport;
