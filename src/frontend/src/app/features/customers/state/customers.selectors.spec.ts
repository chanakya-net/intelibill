import { Customer } from '../services/customer.service';
import { CustomersActions } from './customers.actions';
import { customersAdapter, customersReducer } from './customers.reducer';
import {
  selectAllCustomers,
  selectCustomersEntities,
  selectErrorMessage,
  selectLastMutationSucceeded,
  selectLastMutationType,
  selectLoadingCustomers,
  selectSubmitting,
} from './customers.selectors';

const customerA: Customer = { customerId: 'c1', name: 'Alice', phoneNumber: '+9198', address: null, isActive: true };
const customerB: Customer = { customerId: 'c2', name: 'Bob', phoneNumber: '+9199', address: null, isActive: true };

function buildState(customers: Customer[] = [], overrides = {}) {
  const base = customersReducer(undefined, { type: '@@INIT' } as never);
  return customersAdapter.setAll(customers, { ...base, ...overrides });
}

describe('customers selectors', () => {
  it('selectAllCustomers returns sorted customers', () => {
    const state = buildState([customerB, customerA]);
    expect(selectAllCustomers.projector(state)).toEqual([customerA, customerB]);
  });

  it('selectCustomersEntities returns entity map', () => {
    const state = buildState([customerA]);
    const entities = selectCustomersEntities.projector(state);
    expect(entities['c1']).toEqual(customerA);
  });

  it('selectLoadingCustomers reflects state', () => {
    const state = buildState([], { loadingCustomers: true });
    expect(selectLoadingCustomers.projector(state)).toBe(true);
  });

  it('selectSubmitting reflects state', () => {
    const state = buildState([], { submitting: true });
    expect(selectSubmitting.projector(state)).toBe(true);
  });

  it('selectErrorMessage reflects state', () => {
    const state = buildState([], { errorMessage: 'oops' });
    expect(selectErrorMessage.projector(state)).toBe('oops');
  });

  it('selectLastMutationType reflects state', () => {
    const state = buildState([], { lastMutationType: 'add-customer' });
    expect(selectLastMutationType.projector(state)).toBe('add-customer');
  });

  it('selectLastMutationSucceeded reflects state', () => {
    const state = buildState([], { lastMutationSucceeded: true });
    expect(selectLastMutationSucceeded.projector(state)).toBe(true);
  });
});
