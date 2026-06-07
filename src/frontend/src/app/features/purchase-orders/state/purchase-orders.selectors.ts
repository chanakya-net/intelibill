import { createSelector } from '@ngrx/store';

import { purchaseOrdersAdapter, purchaseOrdersFeature } from './purchase-orders.reducer';

export const selectPurchaseOrdersState = purchaseOrdersFeature.selectPurchaseOrdersState;
const entitySelectors = purchaseOrdersAdapter.getSelectors(selectPurchaseOrdersState);

export const selectAllPurchaseOrders = entitySelectors.selectAll;
export const selectPurchaseOrdersLoadingList = createSelector(
  selectPurchaseOrdersState,
  (state) => state.loadingList
);
export const selectPurchaseOrdersLoadingDetail = createSelector(
  selectPurchaseOrdersState,
  (state) => state.loadingDetail
);
export const selectPurchaseOrdersSubmitting = createSelector(
  selectPurchaseOrdersState,
  (state) => state.submitting
);
export const selectPurchaseOrdersErrorMessage = createSelector(
  selectPurchaseOrdersState,
  (state) => state.errorMessage
);
export const selectPurchaseOrdersPagination = createSelector(selectPurchaseOrdersState, (state) => ({
  totalCount: state.totalCount,
  pageNumber: state.currentPage,
  pageSize: state.pageSize,
}));
export const selectPurchaseOrdersFilters = createSelector(
  selectPurchaseOrdersState,
  (state) => state.filters
);
export const selectSelectedPurchaseOrder = createSelector(
  selectPurchaseOrdersState,
  (state) => state.selectedOrder
);
export const selectCreateDraftSucceeded = createSelector(
  selectPurchaseOrdersState,
  (state) => state.createSucceeded
);
