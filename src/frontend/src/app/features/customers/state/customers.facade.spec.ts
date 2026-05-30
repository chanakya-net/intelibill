import { signal, Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { vi } from 'vitest';

import { Customer } from '../services/customer.service';
import { CustomersActions } from './customers.actions';
import { CustomersFacade } from './customers.facade';
import {
  selectAllCustomers,
  selectErrorMessage,
  selectLastMutationSucceeded,
  selectLastMutationType,
  selectLoadingCustomers,
  selectSubmitting,
} from './customers.selectors';

describe('CustomersFacade', () => {
  const dispatch = vi.fn();
  const customersSignal = signal<Customer[]>([
    {
      customerId: 'c1',
      name: 'Alice',
      phoneNumber: '+9198',
      address: null,
      isActive: true,
      creditLimit: 0,
      purchaseCount: 0,
      lifetimeRevenue: 0,
      currentMonthRevenue: 0,
    },
  ]);
  const boolSignal = signal(false);
  const errorSignal = signal('');
  const mutationTypeSignal = signal<'add-customer' | 'edit-customer' | null>(null);

  const store = {
    dispatch,
    selectSignal: vi.fn((selector: unknown): Signal<unknown> => {
      if (selector === selectAllCustomers) return customersSignal;
      if (selector === selectLoadingCustomers) return boolSignal;
      if (selector === selectSubmitting) return boolSignal;
      if (selector === selectErrorMessage) return errorSignal;
      if (selector === selectLastMutationType) return mutationTypeSignal;
      if (selector === selectLastMutationSucceeded) return boolSignal;
      return signal(null);
    }),
  };

  let facade: CustomersFacade;

  beforeEach(() => {
    dispatch.mockReset();
    TestBed.configureTestingModule({
      providers: [CustomersFacade, { provide: Store, useValue: store }],
    });
    facade = TestBed.inject(CustomersFacade);
  });

  it('exposes allCustomers from store', () => {
    expect(facade.allCustomers()).toEqual(customersSignal());
  });

  it('loadCustomers dispatches loadCustomersRequested', () => {
    facade.loadCustomers();
    expect(dispatch).toHaveBeenCalledWith(CustomersActions.loadCustomersRequested());
  });

  it('addCustomer dispatches addCustomerRequested', () => {
    const payload = { name: 'Bob', phoneNumber: '+9199', address: null, isActive: true, creditLimit: 0 };
    facade.addCustomer(payload);
    expect(dispatch).toHaveBeenCalledWith(CustomersActions.addCustomerRequested({ payload }));
  });

  it('editCustomer dispatches editCustomerRequested', () => {
    const payload = { name: 'Bob Updated', phoneNumber: '+9199', address: null, isActive: true, creditLimit: 100 };
    facade.editCustomer('c1', payload);
    expect(dispatch).toHaveBeenCalledWith(CustomersActions.editCustomerRequested({ customerId: 'c1', payload }));
  });

  it('clearError dispatches clearError', () => {
    facade.clearError();
    expect(dispatch).toHaveBeenCalledWith(CustomersActions.clearError());
  });

  it('clearMutationStatus dispatches clearMutationStatus', () => {
    facade.clearMutationStatus();
    expect(dispatch).toHaveBeenCalledWith(CustomersActions.clearMutationStatus());
  });
});
