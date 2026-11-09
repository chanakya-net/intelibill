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
    getDirectory: vi.fn<CustomerService['getDirectory']>(),
    addCustomer: vi.fn<CustomerService['addCustomer']>(),
    editCustomer: vi.fn<CustomerService['editCustomer']>(),
  };

  beforeEach(() => {
    actions$ = new Subject<Action>();
    customerService.getDirectory.mockReset();
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

  it('loads the requested server page and only emits the latest request identity', () => {
    const first = new Subject<any>();
    const second = new Subject<any>();
    customerService.getDirectory.mockReturnValueOnce(first).mockReturnValueOnce(second);
    const values: any[] = [];
    const subscription = effects.directory$.subscribe((value) => values.push(value));
    const query = { search: 'late', status: 'active' as const, pageNumber: 3, pageSize: 20 };
    actions$.next(CustomersActions.directoryRequested({ query, requestId: 'old' }));
    actions$.next(
      CustomersActions.directoryRequested({ query: { ...query, search: 'new' }, requestId: 'new' }),
    );
    const page = {
      items: [customer],
      totalCount: 41,
      pageNumber: 3,
      pageSize: 20,
      summary: { totalCustomers: 500 },
    };
    second.next(page);
    first.next({ ...page, totalCount: 999 });
    expect(values).toEqual([
      CustomersActions.directorySucceeded({ page: page as any, requestId: 'new' }),
    ]);
    expect(customerService.getDirectory).toHaveBeenCalledWith(query);
    subscription.unsubscribe();
  });

  it('corrects an emptied last page once and reloads global summary after a mutation', () => {
    const summary = {
      totalCustomers: 20,
      outstandingBalance: 0,
      outstandingCount: 0,
      totalCreditIssued: 0,
      accountsWithCredit: 0,
      monthlyRevenue: 0,
    };
    customerService.getDirectory
      .mockReturnValueOnce(of({ items: [], totalCount: 20, pageNumber: 2, pageSize: 20, summary }))
      .mockReturnValueOnce(
        of({ items: [customer], totalCount: 20, pageNumber: 1, pageSize: 20, summary }),
      );
    const values: any[] = [];
    const subscription = effects.directory$.subscribe((value) => values.push(value));
    actions$.next(
      CustomersActions.directoryRequested({
        query: { pageNumber: 2, pageSize: 20, status: 'active' },
        requestId: 'edit',
      }),
    );
    expect(values[0]).toMatchObject({
      type: CustomersActions.directoryRequested.type,
      query: { pageNumber: 1, status: 'active' },
    });
    actions$.next(values[0]);
    expect(values[1]).toMatchObject({
      type: CustomersActions.directorySucceeded.type,
      page: { summary, pageNumber: 1 },
    });
    expect(customerService.getDirectory).toHaveBeenCalledTimes(2);
    const refresh: any[] = [];
    const refreshSubscription = effects.refreshDirectory$.subscribe((value) => refresh.push(value));
    actions$.next(CustomersActions.editCustomerSucceeded({ customer }));
    expect(refresh[0].type).toBe(CustomersActions.directoryRequested.type);
    subscription.unsubscribe();
    refreshSubscription.unsubscribe();
  });

  it('prefers stable code and associates only recognized form fields', async () => {
    customerService.addCustomer.mockReturnValue(
      throwError(() => ({
        error: {
          code: 'Customer.GstinInvalid',
          title: 'Customer.StateCodeInvalid',
          errors: [
            { code: 'Gstin', description: '<script>raw</script>' },
            { code: 'Unknown', description: 'raw' },
          ],
          traceId: 'safe-ref',
        },
      })),
    );
    const output = firstValueFrom(effects.addCustomer$.pipe(take(1)));
    actions$.next(
      CustomersActions.addCustomerRequested({
        payload: {
          name: 'A',
          phoneNumber: '+9198',
          address: null,
          isActive: true,
          creditLimit: 0,
          gstin: null,
          stateCode: null,
        },
      }),
    );
    expect(await output).toMatchObject({
      errorMessage: 'errors.customers.gstinInvalid',
      localizedError: {
        correlationId: 'safe-ref',
        fieldErrors: [{ field: 'gstin', messageKey: 'apiErrors.invalidField', params: {} }],
      },
    });
  });

  it('keeps only safe localized feedback and reference metadata from hostile errors', async () => {
    customerService.getDirectory.mockReturnValue(
      throwError(() => ({
        error: {
          code: 'Untrusted.Code',
          title: '<script>title</script>',
          detail: '<img onerror=alert(1)>',
          traceId: 'safe-trace:123',
        },
      })),
    );
    const output = firstValueFrom(effects.loadCustomers$.pipe(take(1)));
    actions$.next(CustomersActions.loadCustomersRequested());
    expect(await output).toMatchObject({
      errorMessage: 'errors.customers.loadFailed',
      localizedError: {
        messageKey: 'errors.customers.loadFailed',
        params: {},
        fieldErrors: [],
        correlationId: 'safe-trace:123',
      },
    });
  });

  it('dispatches loadCustomersSucceeded on load success', async () => {
    customerService.getDirectory.mockReturnValue(
      of({
        items: [customer],
        totalCount: 1,
        pageNumber: 1,
        pageSize: 20,
        summary: {
          totalCustomers: 1,
          outstandingBalance: 0,
          outstandingCount: 0,
          totalCreditIssued: 0,
          accountsWithCredit: 0,
          monthlyRevenue: 0,
        },
      }),
    );

    const output = firstValueFrom(effects.loadCustomers$.pipe(take(1)));
    actions$.next(CustomersActions.loadCustomersRequested());

    await expect(output).resolves.toEqual(
      CustomersActions.loadCustomersSucceeded({ customers: [customer] }),
    );
  });

  it('dispatches loadCustomersFailed on load error', async () => {
    customerService.getDirectory.mockReturnValue(
      throwError(() => ({ error: { detail: 'Load failed' } })),
    );

    const output = firstValueFrom(effects.loadCustomers$.pipe(take(1)));
    actions$.next(CustomersActions.loadCustomersRequested());

    await expect(output).resolves.toEqual(
      CustomersActions.loadCustomersFailed({ errorMessage: 'errors.customers.loadFailed' }),
    );
  });

  it('dispatches addCustomerSucceeded on add success', async () => {
    customerService.addCustomer.mockReturnValue(of(customer));
    const payload = {
      name: 'Alice',
      phoneNumber: '+9198',
      address: null,
      isActive: true,
      creditLimit: 0,
      gstin: null,
      stateCode: null,
    };

    const output = firstValueFrom(effects.addCustomer$.pipe(take(1)));
    actions$.next(CustomersActions.addCustomerRequested({ payload }));

    await expect(output).resolves.toEqual(CustomersActions.addCustomerSucceeded({ customer }));
  });

  it('dispatches addCustomerFailed with fallback message on add error', async () => {
    customerService.addCustomer.mockReturnValue(throwError(() => ({ error: {} })));

    const output = firstValueFrom(effects.addCustomer$.pipe(take(1)));
    actions$.next(
      CustomersActions.addCustomerRequested({
        payload: {
          name: 'A',
          phoneNumber: '+9198',
          address: null,
          isActive: true,
          creditLimit: 0,
          gstin: null,
          stateCode: null,
        },
      }),
    );

    await expect(output).resolves.toEqual(
      CustomersActions.addCustomerFailed({ errorMessage: 'errors.customers.addFailed' }),
    );
  });

  it('dispatches editCustomerSucceeded on edit success', async () => {
    customerService.editCustomer.mockReturnValue(of(customer));
    const payload = {
      name: 'Alice Updated',
      phoneNumber: '+9198',
      address: null,
      isActive: true,
      creditLimit: 100,
      gstin: null,
      stateCode: null,
    };

    const output = firstValueFrom(effects.editCustomer$.pipe(take(1)));
    actions$.next(CustomersActions.editCustomerRequested({ customerId: 'c1', payload }));

    await expect(output).resolves.toEqual(CustomersActions.editCustomerSucceeded({ customer }));
  });

  it.each([
    ['Customer.GstinInvalid', 'errors.customers.gstinInvalid'],
    ['Customer.StateCodeInvalid', 'errors.customers.stateCodeInvalid'],
    ['Customer.GstinStateMismatch', 'errors.customers.gstinStateMismatch'],
  ])('explains a refused customer GST detail (%s)', async (code, key) => {
    customerService.addCustomer.mockReturnValue(
      throwError(() => ({ error: { title: code, detail: 'Server text' } })),
    );
    customerService.editCustomer.mockReturnValue(
      throwError(() => ({ error: { title: code, detail: 'Server text' } })),
    );
    const payload = {
      name: 'A',
      phoneNumber: '+9198',
      address: null,
      isActive: true,
      creditLimit: 0,
      gstin: '29AAGCB7383J1Z4',
      stateCode: '27',
    };

    const added = firstValueFrom(effects.addCustomer$.pipe(take(1)));
    actions$.next(CustomersActions.addCustomerRequested({ payload }));
    await expect(added).resolves.toEqual(CustomersActions.addCustomerFailed({ errorMessage: key }));

    const edited = firstValueFrom(effects.editCustomer$.pipe(take(1)));
    actions$.next(CustomersActions.editCustomerRequested({ customerId: 'c1', payload }));
    await expect(edited).resolves.toEqual(
      CustomersActions.editCustomerFailed({ errorMessage: key }),
    );
  });

  it('dispatches editCustomerFailed on edit error', async () => {
    customerService.editCustomer.mockReturnValue(
      throwError(() => ({ error: { detail: 'Edit failed' } })),
    );

    const output = firstValueFrom(effects.editCustomer$.pipe(take(1)));
    actions$.next(
      CustomersActions.editCustomerRequested({
        customerId: 'c1',
        payload: {
          name: 'A',
          phoneNumber: '+9198',
          address: null,
          isActive: true,
          creditLimit: 0,
          gstin: null,
          stateCode: null,
        },
      }),
    );

    await expect(output).resolves.toEqual(
      CustomersActions.editCustomerFailed({ errorMessage: 'errors.customers.editFailed' }),
    );
  });
});
