import { ShopContextActions } from '../../core/context/shop-context.actions';
import { Subject } from 'rxjs';
import { vi } from 'vitest';
import { RemotePartySearch } from './remote-party-search';
import { CustomersActions } from '../../features/customers/state/customers.actions';
import { customersReducer } from '../../features/customers/state/customers.reducer';

describe('bounded party requests', () => {
  it('debounces, cancels obsolete searches and rejects an old A answer after A → B → A', () => {
    vi.useFakeTimers();
    let epoch = 1;
    const first = new Subject<{ items: { name: string }[] }>();
    const second = new Subject<{ items: { name: string }[] }>();
    const third = new Subject<{ items: { name: string }[] }>();
    const fetch = vi
      .fn()
      .mockReturnValueOnce(first)
      .mockReturnValueOnce(second)
      .mockReturnValue(third);
    const search = new RemotePartySearch<{ name: string }>(fetch, () => epoch);
    search.search('late');
    vi.advanceTimersByTime(250);
    search.search('other');
    vi.advanceTimersByTime(250);
    search.search('late');
    vi.advanceTimersByTime(250);
    third.next({ items: [{ name: 'current' }] });
    first.next({ items: [{ name: 'old' }] });
    second.next({ items: [{ name: 'other' }] });
    expect(search.items()).toEqual([{ name: 'current' }]);
    search.search('scope');
    vi.advanceTimersByTime(250);
    epoch++;
    third.next({ items: [{ name: 'foreign' }] });
    expect(search.items()).toEqual([]);
    search.destroy();
    vi.useRealTimers();
  });
  it('clears old safe error metadata on a directory request and clears the directory on scope change', () => {
    let state = customersReducer(
      undefined,
      CustomersActions.addCustomerFailed({
        errorMessage: 'old',
        localizedError: {
          messageKey: 'old',
          params: {},
          fieldErrors: [],
          correlationId: 'old-ref',
        },
      }),
    );
    state = customersReducer(
      state,
      CustomersActions.directoryRequested({ query: { pageNumber: 3 }, requestId: 'A' }),
    );
    expect(state.localizedError).toBeUndefined();
    state = customersReducer(state, ShopContextActions.scopeChanged({ epoch: 3, reason: 'scope' }));
    expect(state.requestId).toBeUndefined();
    expect(state.ids).toEqual([]);
    expect(state.totalCount).toBe(0);
  });

  it('clears the displayed suggestion labels immediately when another search starts', () => {
    vi.useFakeTimers();
    const response = new Subject<{ items: { name: string }[] }>();
    const display = vi.fn();
    const search = new RemotePartySearch<{ name: string }>(
      () => response,
      () => 1,
      display,
    );
    search.search('old');
    vi.advanceTimersByTime(250);
    response.next({ items: [{ name: 'Old' }] });
    search.search('new');
    expect(display).toHaveBeenLastCalledWith([]);
    search.destroy();
    vi.useRealTimers();
  });

  it('keeps global summary with a one-row page and refuses an obsolete directory response', () => {
    const query = { search: 'late', status: 'all' as const, pageNumber: 3, pageSize: 20 };
    let state = customersReducer(
      undefined,
      CustomersActions.directoryRequested({ query, requestId: 'A' }),
    );
    state = customersReducer(state, CustomersActions.directoryRequested({ query, requestId: 'B' }));
    const summary = {
      totalCustomers: 500,
      outstandingBalance: 100,
      outstandingCount: 4,
      totalCreditIssued: 5000,
      accountsWithCredit: 20,
      monthlyRevenue: 900,
    };
    const page = {
      items: [
        {
          customerId: 'late',
          name: 'Late',
          phoneNumber: '123',
          address: null,
          isActive: true,
          creditLimit: 0,
          purchaseCount: 0,
          lifetimeRevenue: 0,
          currentMonthRevenue: 0,
        },
      ],
      totalCount: 41,
      pageNumber: 3,
      pageSize: 20,
      summary,
    };
    state = customersReducer(state, CustomersActions.directorySucceeded({ page, requestId: 'B' }));
    expect(state.summary).toEqual(summary);
    expect(state.totalCount).toBe(41);
    const obsolete = customersReducer(
      state,
      CustomersActions.directorySucceeded({ page: { ...page, totalCount: 1 }, requestId: 'A' }),
    );
    expect(obsolete).toBe(state);
  });
});
