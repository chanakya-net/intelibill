import { createSelector } from '@ngrx/store';

import { customersAdapter, customersFeature } from './customers.reducer';

const { selectAll, selectEntities } = customersAdapter.getSelectors();

export const selectAllCustomers = createSelector(
  customersFeature.selectCustomersState,
  selectAll
);

export const selectCustomersEntities = createSelector(
  customersFeature.selectCustomersState,
  selectEntities
);

export const selectLoadingCustomers = customersFeature.selectLoadingCustomers;
export const selectSubmitting = customersFeature.selectSubmitting;
export const selectErrorMessage = customersFeature.selectErrorMessage;
export const selectLastMutationType = customersFeature.selectLastMutationType;
export const selectLastMutationSucceeded = customersFeature.selectLastMutationSucceeded;
