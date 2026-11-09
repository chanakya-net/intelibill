import { ShopContextActions } from '../../../core/context/shop-context.actions';
import {
  PartyDirectoryQuery,
  CustomerDirectorySummary,
  EMPTY_CUSTOMER_SUMMARY,
  DEFAULT_DIRECTORY_QUERY,
} from '../../../shared/models/party-directory.models';
import type { LocalizedError } from '../../../core/i18n/api-error-message.service';
import { createEntityAdapter, EntityState } from '@ngrx/entity';
import { createFeature, createReducer, on } from '@ngrx/store';

import { Customer } from '../services/customer.service';
import { CustomerMutationType, CustomersActions } from './customers.actions';

export const customersFeatureKey = 'customers';

export const customersAdapter = createEntityAdapter<Customer>({
  selectId: (customer) => customer.customerId,
  sortComparer: (left, right) => left.name.localeCompare(right.name),
});

export interface CustomersState extends EntityState<Customer> {
  readonly query: PartyDirectoryQuery;
  readonly requestId: string | undefined;
  readonly totalCount: number;
  readonly summary: CustomerDirectorySummary;
  readonly loadingCustomers: boolean;
  readonly submitting: boolean;
  readonly localizedError: LocalizedError | undefined;
  readonly errorMessage: string;
  readonly lastMutationType: CustomerMutationType | null;
  readonly lastMutationSucceeded: boolean;
}

const initialState: CustomersState = customersAdapter.getInitialState({
  query: DEFAULT_DIRECTORY_QUERY,
  requestId: undefined,
  totalCount: 0,
  summary: EMPTY_CUSTOMER_SUMMARY,
  loadingCustomers: false,
  submitting: false,
  localizedError: undefined,
  errorMessage: '',
  lastMutationType: null,
  lastMutationSucceeded: false,
});

export const customersReducer = createReducer(
  initialState,
  on(ShopContextActions.scopeChanged, () => initialState),
  on(CustomersActions.directoryRequested, (state, { query, requestId }) => ({
    ...state,
    query,
    requestId,
    localizedError: undefined,
    loadingCustomers: true,
    errorMessage: '',
  })),
  on(CustomersActions.directorySucceeded, (state, { page, requestId }) =>
    requestId !== state.requestId
      ? state
      : customersAdapter.setAll([...page.items], {
          ...state,
          totalCount: page.totalCount,
          summary: page.summary,
          query: { ...state.query, pageNumber: page.pageNumber, pageSize: page.pageSize },
          loadingCustomers: false,
          errorMessage: '',
        }),
  ),
  on(CustomersActions.directoryFailed, (state, { errorMessage, requestId }) =>
    requestId !== state.requestId ? state : { ...state, loadingCustomers: false, errorMessage },
  ),
  on(CustomersActions.loadCustomersRequested, (state) => ({
    ...state,
    loadingCustomers: true,
    localizedError: undefined,
    errorMessage: '',
  })),
  on(CustomersActions.loadCustomersSucceeded, (state, { customers }) =>
    customersAdapter.setAll([...customers], {
      ...state,
      loadingCustomers: false,
      localizedError: undefined,
      errorMessage: '',
    }),
  ),
  on(CustomersActions.loadCustomersFailed, (state, { errorMessage, localizedError }) => ({
    ...state,
    loadingCustomers: false,
    localizedError,
    errorMessage,
  })),

  on(CustomersActions.addCustomerRequested, (state) => ({
    ...state,
    submitting: true,
    localizedError: undefined,
    errorMessage: '',
    lastMutationType: 'add-customer',
    lastMutationSucceeded: false,
  })),
  on(CustomersActions.addCustomerSucceeded, (state, { customer }) =>
    customersAdapter.addOne(customer, {
      ...state,
      submitting: false,
      localizedError: undefined,
      errorMessage: '',
      lastMutationType: 'add-customer',
      lastMutationSucceeded: true,
    }),
  ),
  on(CustomersActions.addCustomerFailed, (state, { errorMessage, localizedError }) => ({
    ...state,
    submitting: false,
    localizedError,
    errorMessage,
    lastMutationType: 'add-customer',
    lastMutationSucceeded: false,
  })),

  on(CustomersActions.editCustomerRequested, (state) => ({
    ...state,
    submitting: true,
    localizedError: undefined,
    errorMessage: '',
    lastMutationType: 'edit-customer',
    lastMutationSucceeded: false,
  })),
  on(CustomersActions.editCustomerSucceeded, (state, { customer }) =>
    customersAdapter.updateOne(
      {
        id: customer.customerId,
        changes: customer,
      },
      {
        ...state,
        submitting: false,
        localizedError: undefined,
        errorMessage: '',
        lastMutationType: 'edit-customer',
        lastMutationSucceeded: true,
      },
    ),
  ),
  on(CustomersActions.editCustomerFailed, (state, { errorMessage, localizedError }) => ({
    ...state,
    submitting: false,
    localizedError,
    errorMessage,
    lastMutationType: 'edit-customer',
    lastMutationSucceeded: false,
  })),

  on(CustomersActions.clearError, (state) => ({
    ...state,
    localizedError: undefined,
    errorMessage: '',
  })),
  on(CustomersActions.clearMutationStatus, (state) => ({
    ...state,
    lastMutationType: null,
    lastMutationSucceeded: false,
  })),
);

export const customersFeature = createFeature({
  name: customersFeatureKey,
  reducer: customersReducer,
});
