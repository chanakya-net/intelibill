import { createSelector } from '@ngrx/store';

import { suppliersFeature } from './suppliers.reducer';

export const selectSuppliersState = suppliersFeature.selectSuppliersState;

export const selectSuppliers = createSelector(selectSuppliersState, (state) => state.suppliers);
export const selectSuppliersLoading = createSelector(selectSuppliersState, (state) => state.loadingSuppliers);
export const selectSuppliersSubmitting = createSelector(selectSuppliersState, (state) => state.submitting);
export const selectSuppliersErrorMessage = createSelector(selectSuppliersState, (state) => state.errorMessage);
export const selectSuppliersLastMutationType = createSelector(selectSuppliersState, (state) => state.lastMutationType);
export const selectSuppliersLastMutationSucceeded = createSelector(
  selectSuppliersState,
  (state) => state.lastMutationSucceeded
);