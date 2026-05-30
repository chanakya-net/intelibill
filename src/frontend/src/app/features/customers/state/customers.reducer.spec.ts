import { Customer } from '../services/customer.service';
import { CustomersActions } from './customers.actions';
import { customersAdapter, customersReducer } from './customers.reducer';

const customerA: Customer = {
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
const customerB: Customer = {
  customerId: 'c2',
  name: 'Bob',
  phoneNumber: '+9199',
  address: '1 Main St',
  isActive: true,
  creditLimit: 100,
  purchaseCount: 2,
  lifetimeRevenue: 500,
  currentMonthRevenue: 200,
};

describe('customersReducer', () => {
  const initial = customersReducer(undefined, { type: '@@INIT' } as never);

  it('sets loadingCustomers on loadCustomersRequested', () => {
    const next = customersReducer(initial, CustomersActions.loadCustomersRequested());
    expect(next.loadingCustomers).toBe(true);
    expect(next.errorMessage).toBe('');
  });

  it('sets customers and clears loading on loadCustomersSucceeded', () => {
    const loading = customersReducer(initial, CustomersActions.loadCustomersRequested());
    const next = customersReducer(loading, CustomersActions.loadCustomersSucceeded({ customers: [customerA, customerB] }));
    expect(next.loadingCustomers).toBe(false);
    const all = customersAdapter.getSelectors().selectAll(next);
    expect(all).toHaveLength(2);
    expect(all[0].customerId).toBe('c1');
  });

  it('sets errorMessage on loadCustomersFailed', () => {
    const next = customersReducer(initial, CustomersActions.loadCustomersFailed({ errorMessage: 'err' }));
    expect(next.loadingCustomers).toBe(false);
    expect(next.errorMessage).toBe('err');
  });

  it('sets submitting and lastMutationType on addCustomerRequested', () => {
    const next = customersReducer(initial, CustomersActions.addCustomerRequested({ payload: { name: 'A', phoneNumber: '+9198', address: null, isActive: true, creditLimit: 0 } }));
    expect(next.submitting).toBe(true);
    expect(next.lastMutationType).toBe('add-customer');
    expect(next.lastMutationSucceeded).toBe(false);
  });

  it('adds customer and marks success on addCustomerSucceeded', () => {
    const next = customersReducer(initial, CustomersActions.addCustomerSucceeded({ customer: customerA }));
    expect(next.submitting).toBe(false);
    expect(next.lastMutationSucceeded).toBe(true);
    const all = customersAdapter.getSelectors().selectAll(next);
    expect(all).toHaveLength(1);
  });

  it('sets errorMessage on addCustomerFailed', () => {
    const next = customersReducer(initial, CustomersActions.addCustomerFailed({ errorMessage: 'add fail' }));
    expect(next.submitting).toBe(false);
    expect(next.errorMessage).toBe('add fail');
  });

  it('updates customer on editCustomerSucceeded', () => {
    const withCustomer = customersReducer(initial, CustomersActions.addCustomerSucceeded({ customer: customerA }));
    const updated = { ...customerA, name: 'Alice Edited' };
    const next = customersReducer(withCustomer, CustomersActions.editCustomerSucceeded({ customer: updated }));
    const all = customersAdapter.getSelectors().selectAll(next);
    expect(all[0].name).toBe('Alice Edited');
    expect(next.lastMutationType).toBe('edit-customer');
  });

  it('clears errorMessage on clearError', () => {
    const withError = customersReducer(initial, CustomersActions.loadCustomersFailed({ errorMessage: 'err' }));
    const next = customersReducer(withError, CustomersActions.clearError());
    expect(next.errorMessage).toBe('');
  });

  it('clears mutation status on clearMutationStatus', () => {
    const withMutation = customersReducer(initial, CustomersActions.addCustomerSucceeded({ customer: customerA }));
    const next = customersReducer(withMutation, CustomersActions.clearMutationStatus());
    expect(next.lastMutationType).toBeNull();
    expect(next.lastMutationSucceeded).toBe(false);
  });
});
