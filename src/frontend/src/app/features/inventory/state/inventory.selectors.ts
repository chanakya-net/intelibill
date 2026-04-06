import { createSelector } from '@ngrx/store';

import { inventoryAdapter, inventoryFeature } from './inventory.reducer';

export const selectInventoryState = inventoryFeature.selectInventoryState;
const inventoryEntitySelectors = inventoryAdapter.getSelectors(selectInventoryState);

export const selectInventoryItems = inventoryEntitySelectors.selectAll;
export const selectInventoryLoadingItems = createSelector(selectInventoryState, (state) => state.loadingItems);
export const selectInventorySubmitting = createSelector(selectInventoryState, (state) => state.submitting);
export const selectInventoryErrorMessage = createSelector(selectInventoryState, (state) => state.errorMessage);
export const selectInventoryLastMutationType = createSelector(selectInventoryState, (state) => state.lastMutationType);
export const selectInventoryLastMutationSucceeded = createSelector(
  selectInventoryState,
  (state) => state.lastMutationSucceeded
);
