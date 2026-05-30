import { TestBed } from '@angular/core/testing';
import { Action } from '@ngrx/store';
import { Actions } from '@ngrx/effects';
import { Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';
import { take } from 'rxjs/operators';
import { vi } from 'vitest';

import { CustomerService } from '../services/customer.service';
import { CustomersActions } from './customers.actions';
import { CustomersEffects } from './customers.effects';

describe('CustomersEffects', () => {
  let actions$: Subject<Action>;
  let effects: CustomersEffects;

  const customerService = {
    getCustomers: vi.fn<CustomerService['getCustomers']>(),
    addCustomer: vi.fn<CustomerService['addCustomer']>(),
    editCustomer: vi.fn<CustomerService['editCustomer']>(),
  };

  beforeEach(() => {
    actions$ = new Subject<Action>();
    customerService.getCustomers.mockReset();
    customerService.addCustomer.mockReset();
    customerService.editCustomer.mockReset();

    TestBed.configureTestingModule({
      providers: [
        CustomersEffects,
        { provide: CustomerService, useValue: customerService },
        {
          provide: Actions,
          useFactory: (): Observable<Action> => new Actions(actions$),
        },
      ],
    });

    effects = TestBed.inject(CustomersEffects);
  });

  afterEach(() => {
    actions$.complete();
    TestBed.resetTestingModule();
  });

  const customer = {
    customerId: 'c1',
    name: 'Alice',
    phoneNumber: '+9198',
    address: null,
    isActive: true,
    creditLimit: 0,
    purchaseCount: 0,
    lifetimeRevenue: 0,
    currentMonthRevenue: 0,
  };

  it('dispatches loadCustomersSucceeded on load success', async () => {
    customerService.getCustomers.mockReturnValue(of([customer]));

    const output = firstValueFrom(effects.loadCustomers$.pipe(take(1)));
    actions$.next(CustomersActions.loadCustomersRequested());

    await expect(output).resolves.toEqual(
      CustomersActions.loadCustomersSucceeded({ customers: [customer] })
    );
  });

  it('dispatches loadCustomersFailed on load error', async () => {
    customerService.getCustomers.mockReturnValue(
      throwError(() => ({ error: { detail: 'Load failed' } }))
    );

    const output = firstValueFrom(effects.loadCustomers$.pipe(take(1)));
    actions$.next(CustomersActions.loadCustomersRequested());

    await expect(output).resolves.toEqual(
      CustomersActions.loadCustomersFailed({ errorMessage: 'Load failed' })
    );
  });

  it('dispatches addCustomerSucceeded on add success', async () => {
    customerService.addCustomer.mockReturnValue(of(customer));
    const payload = { name: 'Alice', phoneNumber: '+9198', address: null, isActive: true, creditLimit: 0 };

    const output = firstValueFrom(effects.addCustomer$.pipe(take(1)));
    actions$.next(CustomersActions.addCustomerRequested({ payload }));

    await expect(output).resolves.toEqual(
      CustomersActions.addCustomerSucceeded({ customer })
    );
  });

  it('dispatches addCustomerFailed with fallback message on add error', async () => {
    customerService.addCustomer.mockReturnValue(
      throwError(() => ({ error: {} }))
    );

    const output = firstValueFrom(effects.addCustomer$.pipe(take(1)));
    actions$.next(CustomersActions.addCustomerRequested({ payload: { name: 'A', phoneNumber: '+9198', address: null, isActive: true, creditLimit: 0 } }));

    await expect(output).resolves.toEqual(
      CustomersActions.addCustomerFailed({ errorMessage: 'Failed to add customer' })
    );
  });

  it('dispatches editCustomerSucceeded on edit success', async () => {
    customerService.editCustomer.mockReturnValue(of(customer));
    const payload = { name: 'Alice Updated', phoneNumber: '+9198', address: null, isActive: true, creditLimit: 100 };

    const output = firstValueFrom(effects.editCustomer$.pipe(take(1)));
    actions$.next(CustomersActions.editCustomerRequested({ customerId: 'c1', payload }));

    await expect(output).resolves.toEqual(
      CustomersActions.editCustomerSucceeded({ customer })
    );
  });

  it('dispatches editCustomerFailed on edit error', async () => {
    customerService.editCustomer.mockReturnValue(
      throwError(() => ({ error: { detail: 'Edit failed' } }))
    );

    const output = firstValueFrom(effects.editCustomer$.pipe(take(1)));
    actions$.next(CustomersActions.editCustomerRequested({ customerId: 'c1', payload: { name: 'A', phoneNumber: '+9198', address: null, isActive: true, creditLimit: 0 } }));

    await expect(output).resolves.toEqual(
      CustomersActions.editCustomerFailed({ errorMessage: 'Edit failed' })
    );
  });
});
