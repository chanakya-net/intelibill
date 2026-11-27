import {
  EMPTY_CUSTOMER_SUMMARY,
  DEFAULT_DIRECTORY_QUERY,
} from '../../../shared/models/party-directory.models';
import { createSelector } from '@ngrx/store';

import { customersAdapter, customersFeature } from './customers.reducer';

const { selectAll, selectEntities } = customersAdapter.getSelectors();

export const selectAllCustomers = createSelector(customersFeature.selectCustomersState, selectAll);

export const selectCustomersEntities = createSelector(
  customersFeature.selectCustomersState,
  selectEntities,
);

export const selectLoadingCustomers = customersFeature.selectLoadingCustomers;
export const selectSubmitting = customersFeature.selectSubmitting;
export const selectErrorMessage = customersFeature.selectErrorMessage;
export const selectLastMutationType = customersFeature.selectLastMutationType;
export const selectLastMutationSucceeded = customersFeature.selectLastMutationSucceeded;

export const selectLocalizedError = createSelector(
  customersFeature.selectCustomersState,
  (state) => state.localizedError,
);

export const selectDirectoryQuery = createSelector(
  customersFeature.selectCustomersState,
  (state) => state.query ?? DEFAULT_DIRECTORY_QUERY,
);
export const selectDirectoryTotalCount = createSelector(
  customersFeature.selectCustomersState,
  (state) => state.totalCount ?? 0,
);
export const selectDirectorySummary = createSelector(
  customersFeature.selectCustomersState,
  (state) => state.summary ?? EMPTY_CUSTOMER_SUMMARY,
);
