import { TestBed } from '@angular/core/testing';
import { Store, provideState, provideStore } from '@ngrx/store';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { metaReducers, rootReducers } from '../state';
import { AppShellActions } from '../state/app-shell.actions';
import { selectSidebarPinned } from '../state/app-shell.selectors';
import { AppReloadService } from '../services/app-reload.service';
import { ShopContextService } from './shop-context.service';
import { CustomersActions } from '../../features/customers/state/customers.actions';
import { customersFeature } from '../../features/customers/state/customers.reducer';
import { selectAllCustomers } from '../../features/customers/state/customers.selectors';
import { ExpensesActions } from '../../features/expenses/state/expenses.actions';
import { expensesFeature } from '../../features/expenses/state/expenses.reducer';
import { selectExpensesState } from '../../features/expenses/state/expenses.selectors';
import { ShopsActions } from '../../features/shops/state/shops.actions';
import { shopsFeature } from '../../features/shops/state/shops.reducer';
import { selectShops } from '../../features/shops/state/shops.selectors';

describe('scope transitions against the real store', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideStore(rootReducers, { metaReducers }),
        provideState(customersFeature),
        provideState(shopsFeature),
        provideState(expensesFeature),
        { provide: AppReloadService, useValue: { restartAtAppRoot: vi.fn() } },
      ],
    });
  });

  it('new_user_has_no_old_detail: signing out drops every row the first user loaded', () => {
    const store = TestBed.inject(Store);
    const context = TestBed.inject(ShopContextService);
    context.initialize({ userId: 'user-1', shopId: 'shop-a' });
    store.dispatch(AppShellActions.toggleSidebarPinned());
    const pinned = store.selectSignal(selectSidebarPinned)();
    store.dispatch(
      CustomersActions.loadCustomersSucceeded({
        customers: [
          {
            customerId: 'customer-of-user-1',
            name: 'Acme Stores',
            phoneNumber: '9999999999',
            address: null,
            isActive: true,
            creditLimit: 0,
            purchaseCount: 3,
            lifetimeRevenue: 1500,
            currentMonthRevenue: 500,
          },
        ],
      }),
    );
    store.dispatch(
      ShopsActions.loadShopsSucceeded({
        shops: [
          { shopId: 'shop-a', shopName: 'A', role: 'Owner', isDefault: true, lastUsedAt: null },
        ],
      }),
    );
    expect(store.selectSignal(selectAllCustomers)()).toHaveLength(1);

    context.transition(null, 'sign-out', () => undefined);
    context.transition({ userId: 'user-2', shopId: 'shop-b' }, 'sign-in', () => undefined);

    expect(store.selectSignal(selectAllCustomers)()).toEqual([]);
    expect(store.selectSignal(selectShops)()).toEqual([]);
    // Layout preferences are not scope data and survive.
    expect(store.selectSignal(selectSidebarPinned)()).toBe(pinned);
  });

  it('resets expense query and identity, rejecting old success and failure', () => {
    const store = TestBed.inject(Store);
    const context = TestBed.inject(ShopContextService);
    context.initialize({ userId: 'user-1', shopId: 'shop-a' });
    store.dispatch(
      ExpensesActions.loadExpensesRequested({
        requestId: 'old',
        query: { status: 'voided', search: 'old', page: 3, pageSize: 10 },
      }),
    );
    context.transition({ userId: 'user-1', shopId: 'shop-b' }, 'scope', () => undefined);
    store.dispatch(
      ExpensesActions.loadExpensesSucceeded({
        requestId: 'old',
        expenses: [],
        totalCount: 50,
        page: 3,
        pageSize: 10,
      }),
    );
    store.dispatch(
      ExpensesActions.loadExpensesFailed({ requestId: 'old', errorMessage: 'old error' }),
    );
    const state = store.selectSignal(selectExpensesState)();
    expect(state.query).toEqual({ status: 'all', page: 1, pageSize: 20 });
    expect(state.requestId).toBeNull();
    expect(state.ids).toEqual([]);
    expect(state.totalCount).toBe(0);
    expect(state.errorMessage).toBe('');
    expect(state.listErrorMessage).toBe('');
  });

  it('keeps feature rows when only the credentials are renewed', () => {
    const store = TestBed.inject(Store);
    const context = TestBed.inject(ShopContextService);
    context.initialize({ userId: 'user-1', shopId: 'shop-a' });
    store.dispatch(
      ShopsActions.loadShopsSucceeded({
        shops: [
          { shopId: 'shop-a', shopName: 'A', role: 'Owner', isDefault: true, lastUsedAt: null },
        ],
      }),
    );

    context.transition({ userId: 'user-1', shopId: 'shop-a' }, 'refresh', () => undefined);

    expect(store.selectSignal(selectShops)()).toHaveLength(1);
  });
});
