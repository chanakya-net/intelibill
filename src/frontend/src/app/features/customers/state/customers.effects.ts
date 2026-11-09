import { Store } from '@ngrx/store';
import { ShopContextActions } from '../../../core/context/shop-context.actions';
import { selectDirectoryQuery } from './customers.selectors';
import { DEFAULT_DIRECTORY_QUERY } from '../../../shared/models/party-directory.models';
import { takeUntil, mergeMap as directoryMergeMap } from 'rxjs';
import {
  ApiErrorMessageService,
  type LocalizedError,
} from '../../../core/i18n/api-error-message.service';
import { inject, Injectable } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, map, of, switchMap } from 'rxjs';

import { CustomerService } from '../services/customer.service';
import { CustomersActions } from './customers.actions';

@Injectable()
export class CustomersEffects {
  private readonly apiErrors = inject(ApiErrorMessageService);
  private readonly actions$ = inject(Actions);
  private readonly directoryStore = inject(Store, { optional: true });
  private readonly customerService = inject(CustomerService);

  readonly directory$ = createEffect(() =>
    this.actions$.pipe(
      ofType(CustomersActions.directoryRequested),
      switchMap(({ query, requestId }) =>
        this.customerService.getDirectory(query).pipe(
          directoryMergeMap((page) => {
            const lastPage = Math.max(1, Math.ceil(page.totalCount / page.pageSize));
            if (page.pageNumber > lastPage)
              return of(
                CustomersActions.directoryRequested({
                  query: { ...query, pageNumber: lastPage },
                  requestId: crypto.randomUUID(),
                }),
              );
            return of(CustomersActions.directorySucceeded({ page, requestId }));
          }),
          catchError(() =>
            of(
              CustomersActions.directoryFailed({
                requestId,
                errorMessage: 'errors.customers.loadFailed',
              }),
            ),
          ),
          takeUntil(this.actions$.pipe(ofType(ShopContextActions.scopeChanged))),
        ),
      ),
    ),
  );
  readonly refreshDirectory$ = createEffect(() =>
    this.actions$.pipe(
      ofType(CustomersActions.addCustomerSucceeded, CustomersActions.editCustomerSucceeded),
      map(() =>
        CustomersActions.directoryRequested({
          query:
            this.directoryStore?.selectSignal(selectDirectoryQuery)() ?? DEFAULT_DIRECTORY_QUERY,
          requestId: crypto.randomUUID(),
        }),
      ),
    ),
  );

  loadCustomers$ = createEffect(() =>
    this.actions$.pipe(
      ofType(CustomersActions.loadCustomersRequested),
      switchMap(() =>
        this.customerService
          .getDirectory()
          .pipe(map((page) => page.items))
          .pipe(
            map((customers) => CustomersActions.loadCustomersSucceeded({ customers })),
            catchError((error) =>
              of(
                CustomersActions.loadCustomersFailed({
                  ...this.feedback(error, 'errors.customers.loadFailed'),
                }),
              ),
            ),
          ),
      ),
    ),
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
                ...this.feedback(error, 'errors.customers.addFailed'),
              }),
            ),
          ),
        ),
      ),
    ),
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
                ...this.feedback(error, 'errors.customers.editFailed'),
              }),
            ),
          ),
        ),
      ),
    ),
  );
  private feedback(
    error: unknown,
    fallbackKey: string,
  ): { errorMessage: string; localizedError?: LocalizedError } {
    const localizedError = this.apiErrors.resolve(error, {
      fallbackKey,
      codeMap: CUSTOMER_ERROR_KEYS,
      fieldMap: {
        Name: 'name',
        PhoneNumber: 'phoneNumber',
        Gstin: 'gstin',
        StateCode: 'stateCode',
        CreditLimit: 'creditLimit',
        CreditTermsDays: 'creditTermsDays',
        CreditPolicyMode: 'creditPolicyMode',
      },
    });
    return {
      errorMessage: localizedError.messageKey,
      ...(localizedError.correlationId || localizedError.fieldErrors.length
        ? { localizedError }
        : {}),
    };
  }
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
