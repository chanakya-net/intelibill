import { createActionGroup, emptyProps, props } from '@ngrx/store';

import { AddCustomerRequest, Customer, EditCustomerRequest } from '../services/customer.service';

export type CustomerMutationType = 'add-customer' | 'edit-customer';

export const CustomersActions = createActionGroup({
  source: 'Customers',
  events: {
    'Load Customers Requested': emptyProps(),
    'Load Customers Succeeded': props<{ customers: readonly Customer[] }>(),
    'Load Customers Failed': props<{ errorMessage: string }>(),

    'Add Customer Requested': props<{ payload: AddCustomerRequest }>(),
    'Add Customer Succeeded': props<{ customer: Customer }>(),
    'Add Customer Failed': props<{ errorMessage: string }>(),

    'Edit Customer Requested': props<{ customerId: string; payload: EditCustomerRequest }>(),
    'Edit Customer Succeeded': props<{ customer: Customer }>(),
    'Edit Customer Failed': props<{ errorMessage: string }>(),

    'Clear Error': emptyProps(),
    'Clear Mutation Status': emptyProps(),
  },
});
