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
  readonly loadingCustomers: boolean;
  readonly submitting: boolean;
  readonly errorMessage: string;
  readonly lastMutationType: CustomerMutationType | null;
  readonly lastMutationSucceeded: boolean;
}

const initialState: CustomersState = customersAdapter.getInitialState({
  loadingCustomers: false,
  submitting: false,
  errorMessage: '',
  lastMutationType: null,
  lastMutationSucceeded: false,
});

export const customersReducer = createReducer(
  initialState,
  on(CustomersActions.loadCustomersRequested, (state) => ({
    ...state,
    loadingCustomers: true,
    errorMessage: '',
  })),
  on(CustomersActions.loadCustomersSucceeded, (state, { customers }) =>
    customersAdapter.setAll([...customers], {
      ...state,
      loadingCustomers: false,
      errorMessage: '',
    })
  ),
  on(CustomersActions.loadCustomersFailed, (state, { errorMessage }) => ({
    ...state,
    loadingCustomers: false,
    errorMessage,
  })),

  on(CustomersActions.addCustomerRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'add-customer',
    lastMutationSucceeded: false,
  })),
  on(CustomersActions.addCustomerSucceeded, (state, { customer }) =>
    customersAdapter.addOne(customer, {
      ...state,
      submitting: false,
      errorMessage: '',
      lastMutationType: 'add-customer',
      lastMutationSucceeded: true,
    })
  ),
  on(CustomersActions.addCustomerFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'add-customer',
    lastMutationSucceeded: false,
  })),

  on(CustomersActions.editCustomerRequested, (state) => ({
    ...state,
    submitting: true,
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
        errorMessage: '',
        lastMutationType: 'edit-customer',
        lastMutationSucceeded: true,
      }
    )
  ),
  on(CustomersActions.editCustomerFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'edit-customer',
    lastMutationSucceeded: false,
  })),

  on(CustomersActions.clearError, (state) => ({
    ...state,
    errorMessage: '',
  })),
  on(CustomersActions.clearMutationStatus, (state) => ({
    ...state,
    lastMutationType: null,
    lastMutationSucceeded: false,
  }))
);

export const customersFeature = createFeature({
  name: customersFeatureKey,
  reducer: customersReducer,
});
