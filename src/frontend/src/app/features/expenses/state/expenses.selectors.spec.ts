import { expensesAdapter, expensesReducer } from './expenses.reducer';
import { ExpenseListItemDto } from '../services/expense.service';
import {
  selectAllExpenses,
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

const makeExpense = (id: string): ExpenseListItemDto => ({
  id,
  amount: 500,
  categoryName: 'Rent',
  paidTo: 'Landlord',
  expenseDate: '2026-04-20T00:00:00Z',
  isVoided: false,
});

function buildState(expenses: ExpenseListItemDto[] = [], overrides = {}) {
  const base = expensesReducer(undefined, { type: '@@INIT' } as never);
  return expensesAdapter.setAll(expenses, { ...base, ...overrides });
}

describe('expenses selectors', () => {
  it('selectAllExpenses returns all expenses sorted by expenseDate desc', () => {
    const older = makeExpense('e1');
    const newer = { ...makeExpense('e2'), expenseDate: '2026-04-21T00:00:00Z' };
    const state = buildState([older, newer]);
    const all = selectAllExpenses.projector(state);
    expect(all[0].id).toBe('e2');
    expect(all[1].id).toBe('e1');
  });

  it('selectExpensesLoading reflects state', () => {
    const state = buildState([], { loadingExpenses: true });
    expect(selectExpensesLoading.projector(state)).toBe(true);
  });

  it('selectSubmitting reflects state', () => {
    const state = buildState([], { submitting: true });
    expect(selectSubmitting.projector(state)).toBe(true);
  });

  it('selectExpensesError reflects state', () => {
    const state = buildState([], { errorMessage: 'fail' });
    expect(selectExpensesError.projector(state)).toBe('fail');
  });

  it('selectSelectedExpense reflects state', () => {
    const expense = makeExpense('e1');
    const state = buildState([], { selectedExpense: expense });
    expect(selectSelectedExpense.projector(state)).toEqual(expense);
  });

  it('selectSelectedExpense returns null by default', () => {
    const state = buildState();
    expect(selectSelectedExpense.projector(state)).toBeNull();
  });

  it('selectExpenseDetailLoading reflects state', () => {
    const state = buildState([], { loadingExpenseDetail: true });
    expect(selectExpenseDetailLoading.projector(state)).toBe(true);
  });

  it('selectCategories reflects state', () => {
    const categories = [{ id: 'cat-1', name: 'Rent' }];
    const state = buildState([], { categories });
    expect(selectCategories.projector(state)).toEqual(categories);
  });

  it('selectCategoriesLoading reflects state', () => {
    const state = buildState([], { loadingCategories: true });
    expect(selectCategoriesLoading.projector(state)).toBe(true);
  });

  it('selectLastMutationStatus reflects state', () => {
    const state = buildState([], { lastMutationType: 'record-expense', lastMutationSucceeded: true });
    expect(selectLastMutationStatus.projector(state)).toEqual({
      type: 'record-expense',
      succeeded: true,
    });
  });

  it('selectPagination reflects state', () => {
    const state = buildState([], { totalCount: 100, currentPage: 2, pageSize: 10 });
    expect(selectPagination.projector(state)).toEqual({
      totalCount: 100,
      currentPage: 2,
      pageSize: 10,
    });
  });
});
