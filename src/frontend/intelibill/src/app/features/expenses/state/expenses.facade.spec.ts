import { BehaviorSubject, of } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { vi } from 'vitest';

import { ExpenseListItemDto } from '../services/expense.service';
import { ExpenseCategoryDto } from '../services/expense-category.service';
import { ExpensesActions } from './expenses.actions';
import { ExpensesFacade } from './expenses.facade';
import {
  selectAllExpenses,
  selectExpensesQuery,
  selectCategories,
  selectCategoriesLoading,
  selectExpenseDetailLoading,
  selectExpensesError,
  selectExpensesLoading,
  selectLastMutationStatus,
  selectPagination,
  selectSelectedExpense,
  selectSubmitting,
} from './expenses.selectors';

describe('ExpensesFacade', () => {
  const dispatch = vi.fn();
  const query$ = new BehaviorSubject({
    status: 'active' as 'active' | 'voided',
    search: 'rent',
    page: 2,
    pageSize: 20,
  });

  const store = {
    selectSignal: vi.fn(() => () => undefined),
    dispatch,
    select: vi.fn((selector: unknown) => {
      if (selector === selectExpensesQuery) return query$;
      if (selector === selectAllExpenses) return of([] as ExpenseListItemDto[]);
      if (selector === selectExpensesLoading) return of(false);
      if (selector === selectSubmitting) return of(false);
      if (selector === selectExpensesError) return of('');
      if (selector === selectLastMutationStatus) return of({ type: null, succeeded: false });
      if (selector === selectSelectedExpense) return of(null);
      if (selector === selectExpenseDetailLoading) return of(false);
      if (selector === selectCategories) return of([] as ExpenseCategoryDto[]);
      if (selector === selectCategoriesLoading) return of(false);
      if (selector === selectPagination) return of({ totalCount: 0, currentPage: 1, pageSize: 20 });
      return of(null);
    }),
  };

  let facade: ExpensesFacade;

  beforeEach(() => {
    query$.next({ status: 'active', search: 'rent', page: 2, pageSize: 20 });
    dispatch.mockReset();
    store.select.mockClear();
    TestBed.configureTestingModule({
      providers: [ExpensesFacade, { provide: Store, useValue: store }],
    });
    facade = TestBed.inject(ExpensesFacade);
  });

  it('loadExpenses dispatches loadExpensesRequested', () => {
    facade.loadExpenses({ search: 'search', status: 'active', page: 2, pageSize: 10 });
    expect(dispatch).toHaveBeenCalledWith(
      ExpensesActions.loadExpensesRequested({
        query: { search: 'search', status: 'active', page: 2, pageSize: 10 },
        requestId: expect.any(String),
      }),
    );
  });

  it('refresh snapshots the current query, even after a newer filter, with fresh identities', () => {
    facade.refreshExpenses();
    query$.next({ status: 'voided', search: 'latest', page: 1, pageSize: 10 });
    facade.refreshExpenses();
    const [first, second] = dispatch.mock.calls.map((call) => call[0]);
    expect(first.query).toEqual({ status: 'active', search: 'rent', page: 2, pageSize: 20 });
    expect(second.query).toEqual({ status: 'voided', search: 'latest', page: 1, pageSize: 10 });
    expect(first.requestId).not.toBe(second.requestId);
  });

  it('loadExpenseDetail dispatches loadExpenseDetailRequested', () => {
    facade.loadExpenseDetail('exp-1');
    expect(dispatch).toHaveBeenCalledWith(
      ExpensesActions.loadExpenseDetailRequested({ expenseId: 'exp-1' }),
    );
  });

  it('loadCategories dispatches loadCategoriesRequested', () => {
    facade.loadCategories();
    expect(dispatch).toHaveBeenCalledWith(ExpensesActions.loadCategoriesRequested());
  });

  it('recordExpense dispatches recordExpenseRequested', () => {
    const payload = {
      categoryName: 'Rent',
      amount: 500,
      paidTo: 'Landlord',
      description: null,
      expenseDate: '2026-04-20',
    };
    facade.recordExpense(payload);
    expect(dispatch).toHaveBeenCalledWith(ExpensesActions.recordExpenseRequested({ payload }));
  });

  it('correctExpense dispatches correctExpenseRequested', () => {
    const payload = {
      categoryName: 'Rent',
      amount: 550,
      paidTo: 'Landlord',
      description: null,
      expenseDate: '2026-04-20',
    };
    facade.correctExpense('exp-1', payload);
    expect(dispatch).toHaveBeenCalledWith(
      ExpensesActions.correctExpenseRequested({ expenseId: 'exp-1', payload }),
    );
  });

  it('clearError dispatches clearError', () => {
    facade.clearError();
    expect(dispatch).toHaveBeenCalledWith(ExpensesActions.clearError());
  });

  it('clearMutationStatus dispatches clearMutationStatus', () => {
    facade.clearMutationStatus();
    expect(dispatch).toHaveBeenCalledWith(ExpensesActions.clearMutationStatus());
  });

  it('clearExpenseDetail dispatches clearExpenseDetail', () => {
    facade.clearExpenseDetail();
    expect(dispatch).toHaveBeenCalledWith(ExpensesActions.clearExpenseDetail());
  });
});
