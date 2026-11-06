import { inject, Injectable } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, map, of, switchMap } from 'rxjs';

import { CustomerService } from '../services/customer.service';
import { CustomersActions } from './customers.actions';

@Injectable()
export class CustomersEffects {
  private readonly actions$ = inject(Actions);
  private readonly customerService = inject(CustomerService);

  loadCustomers$ = createEffect(() =>
    this.actions$.pipe(
      ofType(CustomersActions.loadCustomersRequested),
      switchMap(() =>
        this.customerService.getCustomers().pipe(
          map((customers) => CustomersActions.loadCustomersSucceeded({ customers })),
          catchError((error) =>
            of(
              CustomersActions.loadCustomersFailed({
                errorMessage: error.error?.detail || 'errors.customers.loadFailed',
              })
            )
          )
        )
      )
    )
  );

  addCustomer$ = createEffect(() =>
    this.actions$.pipe(
      ofType(CustomersActions.addCustomerRequested),
      switchMap(({ payload }) =>
        this.customerService.addCustomer(payload).pipe(
          map((customer) => CustomersActions.addCustomerSucceeded({ customer })),
          catchError((error) =>
            of(
              CustomersActions.addCustomerFailed({
                errorMessage: customerMutationErrorMessage(error, 'errors.customers.addFailed'),
              })
            )
          )
        )
      )
    )
  );

  editCustomer$ = createEffect(() =>
    this.actions$.pipe(
      ofType(CustomersActions.editCustomerRequested),
      switchMap(({ customerId, payload }) =>
        this.customerService.editCustomer(customerId, payload).pipe(
          map((customer) => CustomersActions.editCustomerSucceeded({ customer })),
          catchError((error) =>
            of(
              CustomersActions.editCustomerFailed({
                errorMessage: customerMutationErrorMessage(error, 'errors.customers.editFailed'),
              })
            )
          )
        )
      )
    )
  );
}

// GST details are refused for the rule they break; each refusal says what to correct.
const CUSTOMER_ERROR_KEYS: Readonly<Record<string, string>> = {
  'Customer.GstinInvalid': 'errors.customers.gstinInvalid',
  'Customer.StateCodeInvalid': 'errors.customers.stateCodeInvalid',
  'Customer.GstinStateMismatch': 'errors.customers.gstinStateMismatch',
  'Customer.CreditSettingsChangeNotPermitted': 'errors.customers.creditSettingsChangeNotPermitted',
  'Customer.EnforcedCreditRequiresLimit': 'errors.customers.enforcedCreditRequiresLimit',
  'Customer.CreditTermsDaysInvalid': 'errors.customers.creditTermsDaysInvalid',
  'Customer.CreditPolicyModeInvalid': 'errors.customers.creditPolicyModeInvalid',
};

function customerMutationErrorMessage(error: { error?: { title?: string; detail?: string } }, fallback: string): string {
  const title = error.error?.title;
  return (title && CUSTOMER_ERROR_KEYS[title]) || error.error?.detail || fallback;
}
