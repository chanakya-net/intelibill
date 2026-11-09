import {
  EMPTY_SUPPLIER_SUMMARY,
  DEFAULT_DIRECTORY_QUERY,
} from '../../../shared/models/party-directory.models';
import { createSelector } from '@ngrx/store';

import { suppliersAdapter, suppliersFeature } from './suppliers.reducer';

export const selectSuppliersState = suppliersFeature.selectSuppliersState;
const supplierEntitySelectors = suppliersAdapter.getSelectors(selectSuppliersState);

export const selectSuppliers = supplierEntitySelectors.selectAll;
export const selectSupplierEntities = supplierEntitySelectors.selectEntities;
export const selectSuppliersLoading = createSelector(
  selectSuppliersState,
  (state) => state.loadingSuppliers,
);
export const selectSuppliersSubmitting = createSelector(
  selectSuppliersState,
  (state) => state.submitting,
);
export const selectSuppliersErrorMessage = createSelector(
  selectSuppliersState,
  (state) => state.errorMessage,
);
export const selectSuppliersLastMutationType = createSelector(
  selectSuppliersState,
  (state) => state.lastMutationType,
);
export const selectSuppliersLastMutationSucceeded = createSelector(
  selectSuppliersState,
  (state) => state.lastMutationSucceeded,
);

export const selectLedgerEntries = createSelector(
  selectSuppliersState,
  (state) => state.ledgerEntries,
);
export const selectLedgerLoading = createSelector(
  selectSuppliersState,
  (state) => state.loadingLedger,
);
export const selectLedgerErrorMessage = createSelector(
  selectSuppliersState,
  (state) => state.ledgerErrorMessage,
);
export const selectDirectoryQuery = createSelector(
  suppliersFeature.selectSuppliersState,
  (state) => state.query ?? DEFAULT_DIRECTORY_QUERY,
);
export const selectDirectoryTotalCount = createSelector(
  suppliersFeature.selectSuppliersState,
  (state) => state.totalCount ?? 0,
);
export const selectDirectorySummary = createSelector(
  suppliersFeature.selectSuppliersState,
  (state) => state.summary ?? EMPTY_SUPPLIER_SUMMARY,
);
