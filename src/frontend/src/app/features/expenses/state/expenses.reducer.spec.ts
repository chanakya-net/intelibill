import { ExpensesActions } from './expenses.actions';
import { expensesReducer, ExpensesState } from './expenses.reducer';
import { ExpenseListItemDto } from '../services/expense.service';

const makeExpense = (
  id: string,
  overrides: Partial<ExpenseListItemDto> = {},
): ExpenseListItemDto => ({
  id,
  amount: 500,
  categoryName: 'Rent',
  paidTo: 'Landlord',
  expenseDate: '2026-04-20T00:00:00Z',
  isVoided: false,
  ...overrides,
});

const makeExpenseDto = (id = 'exp-1') => ({
  id,
  shopId: 'shop-1',
  categoryId: 'cat-1',
  categoryName: 'Rent',
  amount: 500,
  paidTo: 'Landlord',
  description: null,
  expenseDate: '2026-04-20T00:00:00Z',
  actorUserId: 'user-1',
  isVoided: false,
  originalExpenseId: null,
  createdAt: '2026-04-20T10:00:00Z',
});

describe('expensesReducer', () => {
  const initialState = expensesReducer(undefined, { type: '@@INIT' } as never);

  it('ignores stale success and failure after a newer query', () => {
    const query = { status: 'voided' as const, search: 'rent', page: 1, pageSize: 20 };
    const pending = expensesReducer(
      initialState,
      ExpensesActions.loadExpensesRequested({ query, requestId: 'new' }),
    );
    const success = ExpensesActions.loadExpensesSucceeded({
      requestId: 'old',
      expenses: [makeExpense('old')],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    });
    expect(expensesReducer(pending, success)).toBe(pending);
    expect(
      expensesReducer(
        pending,
        ExpensesActions.loadExpensesFailed({ requestId: 'old', errorMessage: 'late' }),
      ),
    ).toBe(pending);
  });

  it('sets loading state when load expenses is requested', () => {
    const next = expensesReducer(
      { ...initialState, errorMessage: 'existing error' },
      ExpensesActions.loadExpensesRequested({
        query: { status: 'all', page: 1, pageSize: 20 },
        requestId: 'r1',
      }),
    );

    expect(next.loadingExpenses).toBe(true);
    expect(next.errorMessage).toBe('');
  });

  it('sets expenses when load succeeds', () => {
    const expenses = [makeExpense('e1'), makeExpense('e2')];
    const next = expensesReducer(
      { ...initialState, loadingExpenses: true, requestId: 'r1' },
      ExpensesActions.loadExpensesSucceeded({
        requestId: 'r1',
        expenses,
        totalCount: 2,
        page: 1,
        pageSize: 20,
      }),
    );

    expect(next.loadingExpenses).toBe(false);
    expect(next.ids).toContain('e1');
    expect(next.ids).toContain('e2');
    expect(next.totalCount).toBe(2);
    expect(next.currentPage).toBe(1);
    expect(next.pageSize).toBe(20);
  });

  it('sets error when load fails', () => {
    const next = expensesReducer(
      { ...initialState, loadingExpenses: true, requestId: 'r1' },
      ExpensesActions.loadExpensesFailed({
        requestId: 'r1',
        errorMessage: 'Failed to load expenses',
      }),
    );

    expect(next.loadingExpenses).toBe(false);
    expect(next.errorMessage).toBe('Failed to load expenses');
  });

  it('sets loading detail state when load expense detail is requested', () => {
    const next = expensesReducer(
      {
        ...initialState,
        errorMessage: 'existing error',
        selectedExpense: makeExpenseDto() as never,
      },
      ExpensesActions.loadExpenseDetailRequested({ expenseId: 'exp-1' }),
    );

    expect(next.loadingExpenseDetail).toBe(true);
    expect(next.errorMessage).toBe('');
    expect(next.selectedExpense).toBeNull();
  });

  it('sets selected expense when load detail succeeds', () => {
    const expense = makeExpenseDto();
    const next = expensesReducer(
      { ...initialState, loadingExpenseDetail: true },
      ExpensesActions.loadExpenseDetailSucceeded({ expense }),
    );

    expect(next.loadingExpenseDetail).toBe(false);
    expect(next.selectedExpense).toEqual(expense);
  });

  it('sets error when load detail fails', () => {
    const next = expensesReducer(
      { ...initialState, loadingExpenseDetail: true },
      ExpensesActions.loadExpenseDetailFailed({ errorMessage: 'Not found' }),
    );

    expect(next.loadingExpenseDetail).toBe(false);
    expect(next.errorMessage).toBe('Not found');
  });

  it('sets loading categories state when load categories is requested', () => {
    const next = expensesReducer(
      { ...initialState, errorMessage: 'existing error' },
      ExpensesActions.loadCategoriesRequested(),
    );

    expect(next.loadingCategories).toBe(true);
    expect(next.errorMessage).toBe('');
  });

  it('sets categories when load categories succeeds', () => {
    const categories = [{ id: 'cat-1', name: 'Rent' }];
    const next = expensesReducer(
      { ...initialState, loadingCategories: true },
      ExpensesActions.loadCategoriesSucceeded({ categories }),
    );

    expect(next.loadingCategories).toBe(false);
    expect(next.categories).toEqual(categories);
  });

  it('sets error when load categories fails', () => {
    const next = expensesReducer(
      { ...initialState, loadingCategories: true },
      ExpensesActions.loadCategoriesFailed({ errorMessage: 'Failed to load categories' }),
    );

    expect(next.loadingCategories).toBe(false);
    expect(next.errorMessage).toBe('Failed to load categories');
  });

  it('sets submitting on record expense requested', () => {
    const next = expensesReducer(
      initialState,
      ExpensesActions.recordExpenseRequested({
        payload: {
          categoryName: 'Rent',
          amount: 500,
          paidTo: 'Landlord',
          description: null,
          expenseDate: '2026-04-20',
        },
      }),
    );

    expect(next.submitting).toBe(true);
    expect(next.lastMutationType).toBe('record-expense');
    expect(next.lastMutationSucceeded).toBe(false);
  });

  it('sets lastMutationSucceeded on record expense succeeded', () => {
    const expense = makeExpenseDto();
    const next = expensesReducer(
      { ...initialState, submitting: true },
      ExpensesActions.recordExpenseSucceeded({ expense }),
    );

    expect(next.submitting).toBe(false);
    expect(next.lastMutationType).toBe('record-expense');
    expect(next.lastMutationSucceeded).toBe(true);
    expect(next.selectedExpense).toEqual(expense);
  });

  it('sets error on record expense failed', () => {
    const next = expensesReducer(
      { ...initialState, submitting: true },
      ExpensesActions.recordExpenseFailed({ errorMessage: 'Failed to record expense' }),
    );

    expect(next.submitting).toBe(false);
    expect(next.errorMessage).toBe('Failed to record expense');
    expect(next.lastMutationType).toBe('record-expense');
    expect(next.lastMutationSucceeded).toBe(false);
  });

  it('sets submitting on correct expense requested', () => {
    const next = expensesReducer(
      initialState,
      ExpensesActions.correctExpenseRequested({
        expenseId: 'exp-1',
        payload: {
          categoryName: 'Rent',
          amount: 550,
          paidTo: 'Landlord',
          description: null,
          expenseDate: '2026-04-20',
        },
      }),
    );

    expect(next.submitting).toBe(true);
    expect(next.lastMutationType).toBe('correct-expense');
    expect(next.lastMutationSucceeded).toBe(false);
  });

  it('sets lastMutationSucceeded on correct expense succeeded', () => {
    const expense = makeExpenseDto();
    const next = expensesReducer(
      { ...initialState, submitting: true },
      ExpensesActions.correctExpenseSucceeded({ expense }),
    );

    expect(next.submitting).toBe(false);
    expect(next.lastMutationType).toBe('correct-expense');
    expect(next.lastMutationSucceeded).toBe(true);
    expect(next.selectedExpense).toEqual(expense);
  });

  it('sets error on correct expense failed', () => {
    const next = expensesReducer(
      { ...initialState, submitting: true },
      ExpensesActions.correctExpenseFailed({ errorMessage: 'Failed to correct expense' }),
    );

    expect(next.submitting).toBe(false);
    expect(next.errorMessage).toBe('Failed to correct expense');
    expect(next.lastMutationType).toBe('correct-expense');
    expect(next.lastMutationSucceeded).toBe(false);
  });

  it('clears selected expense on clearExpenseDetail', () => {
    const expense = makeExpenseDto();
    const next = expensesReducer(
      { ...initialState, selectedExpense: expense as never },
      ExpensesActions.clearExpenseDetail(),
    );

    expect(next.selectedExpense).toBeNull();
  });

  it('clears error on clearError', () => {
    const next = expensesReducer(
      { ...initialState, errorMessage: 'some error' },
      ExpensesActions.clearError(),
    );

    expect(next.errorMessage).toBe('');
  });

  it('clears mutation status on clearMutationStatus', () => {
    const next = expensesReducer(
      { ...initialState, lastMutationType: 'record-expense', lastMutationSucceeded: true },
      ExpensesActions.clearMutationStatus(),
    );

    expect(next.lastMutationType).toBeNull();
    expect(next.lastMutationSucceeded).toBe(false);
  });
});
