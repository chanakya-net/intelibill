import {
  PartyDirectoryQuery,
  DEFAULT_DIRECTORY_QUERY,
} from '../../../shared/models/party-directory.models';
import {
  selectDirectoryQuery,
  selectDirectoryTotalCount,
  selectDirectorySummary,
} from './customers.selectors';
import { selectLocalizedError } from './customers.selectors';
import { inject, Injectable, Signal } from '@angular/core';
import { Store } from '@ngrx/store';

import { AddCustomerRequest, Customer, EditCustomerRequest } from '../services/customer.service';
import { CustomersActions } from './customers.actions';
import * as CustomersSelectors from './customers.selectors';

@Injectable({
  providedIn: 'root',
})
export class CustomersFacade {
  private readonly store = inject(Store);
  readonly query = this.store.selectSignal(selectDirectoryQuery);
  readonly totalCount = this.store.selectSignal(selectDirectoryTotalCount);
  readonly summary = this.store.selectSignal(selectDirectorySummary);
  loadDirectory(query: PartyDirectoryQuery = this.query() ?? DEFAULT_DIRECTORY_QUERY): void {
    this.store.dispatch(
      CustomersActions.directoryRequested({ query, requestId: crypto.randomUUID() }),
    );
  }

  readonly localizedError = this.store.selectSignal(selectLocalizedError);

  readonly allCustomers: Signal<readonly Customer[]> = this.store.selectSignal(
    CustomersSelectors.selectAllCustomers,
  );
  readonly loadingCustomers: Signal<boolean> = this.store.selectSignal(
    CustomersSelectors.selectLoadingCustomers,
  );
  readonly submitting: Signal<boolean> = this.store.selectSignal(
    CustomersSelectors.selectSubmitting,
  );
  readonly errorMessage: Signal<string> = this.store.selectSignal(
    CustomersSelectors.selectErrorMessage,
  );
  readonly lastMutationType: Signal<'add-customer' | 'edit-customer' | null> =
    this.store.selectSignal(CustomersSelectors.selectLastMutationType);
  readonly lastMutationSucceeded: Signal<boolean> = this.store.selectSignal(
    CustomersSelectors.selectLastMutationSucceeded,
  );

  loadCustomers(): void {
    this.loadDirectory();
  }

  addCustomer(payload: AddCustomerRequest): void {
    this.store.dispatch(CustomersActions.addCustomerRequested({ payload }));
  }

  editCustomer(customerId: string, payload: EditCustomerRequest): void {
    this.store.dispatch(CustomersActions.editCustomerRequested({ customerId, payload }));
  }

  clearError(): void {
    this.store.dispatch(CustomersActions.clearError());
  }

  clearMutationStatus(): void {
    this.store.dispatch(CustomersActions.clearMutationStatus());
  }
}
