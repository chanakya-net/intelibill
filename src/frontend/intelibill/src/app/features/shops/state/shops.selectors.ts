import { createSelector } from '@ngrx/store';

import { shopsFeature } from './shops.reducer';

export const selectShopsState = shopsFeature.selectShopsState;

export const selectShops = createSelector(selectShopsState, (state) => state.shops);
export const selectSelectedShopId = createSelector(selectShopsState, (state) => state.selectedShopId);
export const selectShopDetailsEntities = createSelector(selectShopsState, (state) => state.detailsById);

export const selectSelectedShopDetails = createSelector(
  selectSelectedShopId,
  selectShopDetailsEntities,
  (selectedShopId, detailsById) => (selectedShopId ? detailsById[selectedShopId] ?? null : null)
);

export const selectShopsLoadingDetails = createSelector(selectShopsState, (state) => state.loadingDetails);
export const selectShopsSubmitting = createSelector(selectShopsState, (state) => state.submitting);
export const selectShopsErrorMessage = createSelector(selectShopsState, (state) => state.errorMessage);
export const selectShopsLastMutationType = createSelector(selectShopsState, (state) => state.lastMutationType);
export const selectShopsLastMutationSucceeded = createSelector(selectShopsState, (state) => state.lastMutationSucceeded);
