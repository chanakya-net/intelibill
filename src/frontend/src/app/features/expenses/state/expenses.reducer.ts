import { createEntityAdapter, EntityState } from '@ngrx/entity';
import { createFeature, createReducer, on } from '@ngrx/store';

import { ExpenseDto, ExpenseListItemDto } from '../services/expense.service';
import { ExpenseCategoryDto } from '../services/expense-category.service';
import { ExpenseMutationType, ExpensesActions } from './expenses.actions';

export const expensesFeatureKey = 'expenses';

export const expensesAdapter = createEntityAdapter<ExpenseListItemDto>({
  selectId: (expense) => expense.id,
  sortComparer: (left, right) =>
    new Date(right.expenseDate).getTime() - new Date(left.expenseDate).getTime(),
});

export interface ExpensesState extends EntityState<ExpenseListItemDto> {
  readonly loadingExpenses: boolean;
  readonly submitting: boolean;
  readonly errorMessage: string;
  readonly lastMutationType: ExpenseMutationType | null;
  readonly lastMutationSucceeded: boolean;
  readonly selectedExpense: ExpenseDto | null;
  readonly loadingExpenseDetail: boolean;
  readonly categories: readonly ExpenseCategoryDto[];
  readonly loadingCategories: boolean;
  readonly totalCount: number;
  readonly currentPage: number;
  readonly pageSize: number;
}

const initialState: ExpensesState = expensesAdapter.getInitialState({
  loadingExpenses: false,
  submitting: false,
  errorMessage: '',
  lastMutationType: null,
  lastMutationSucceeded: false,
  selectedExpense: null,
  loadingExpenseDetail: false,
  categories: [],
  loadingCategories: false,
  totalCount: 0,
  currentPage: 1,
  pageSize: 20,
});

export const expensesReducer = createReducer(
  initialState,
  on(ExpensesActions.loadExpensesRequested, (state) => ({
    ...state,
    loadingExpenses: true,
    errorMessage: '',
  })),
  on(ExpensesActions.loadExpensesSucceeded, (state, { expenses, totalCount, page, pageSize }) =>
    expensesAdapter.setAll([...expenses], {
      ...state,
      loadingExpenses: false,
      errorMessage: '',
      totalCount,
      currentPage: page,
      pageSize,
    })
  ),
  on(ExpensesActions.loadExpensesFailed, (state, { errorMessage }) => ({
    ...state,
    loadingExpenses: false,
    errorMessage,
  })),

  on(ExpensesActions.loadExpenseDetailRequested, (state) => ({
    ...state,
    loadingExpenseDetail: true,
    errorMessage: '',
    selectedExpense: null,
  })),
  on(ExpensesActions.loadExpenseDetailSucceeded, (state, { expense }) => ({
    ...state,
    loadingExpenseDetail: false,
    selectedExpense: expense,
  })),
  on(ExpensesActions.loadExpenseDetailFailed, (state, { errorMessage }) => ({
    ...state,
    loadingExpenseDetail: false,
    errorMessage,
  })),

  on(ExpensesActions.loadCategoriesRequested, (state) => ({
    ...state,
    loadingCategories: true,
    errorMessage: '',
  })),
  on(ExpensesActions.loadCategoriesSucceeded, (state, { categories }) => ({
    ...state,
    loadingCategories: false,
    categories,
  })),
  on(ExpensesActions.loadCategoriesFailed, (state, { errorMessage }) => ({
    ...state,
    loadingCategories: false,
    errorMessage,
  })),

  on(ExpensesActions.recordExpenseRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'record-expense' as ExpenseMutationType,
    lastMutationSucceeded: false,
  })),
  on(ExpensesActions.recordExpenseSucceeded, (state, { expense }) => ({
    ...state,
    submitting: false,
    errorMessage: '',
    lastMutationType: 'record-expense' as ExpenseMutationType,
    lastMutationSucceeded: true,
    selectedExpense: expense,
  })),
  on(ExpensesActions.recordExpenseFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'record-expense' as ExpenseMutationType,
    lastMutationSucceeded: false,
  })),

  on(ExpensesActions.correctExpenseRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'correct-expense' as ExpenseMutationType,
    lastMutationSucceeded: false,
  })),
  on(ExpensesActions.correctExpenseSucceeded, (state, { expense }) => ({
    ...state,
    submitting: false,
    errorMessage: '',
    lastMutationType: 'correct-expense' as ExpenseMutationType,
    lastMutationSucceeded: true,
    selectedExpense: expense,
  })),
  on(ExpensesActions.correctExpenseFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'correct-expense' as ExpenseMutationType,
    lastMutationSucceeded: false,
  })),

  on(ExpensesActions.clearError, (state) => ({
    ...state,
    errorMessage: '',
  })),
  on(ExpensesActions.clearMutationStatus, (state) => ({
    ...state,
    lastMutationType: null,
    lastMutationSucceeded: false,
  })),
  on(ExpensesActions.clearExpenseDetail, (state) => ({
    ...state,
    selectedExpense: null,
  }))
);

export const expensesFeature = createFeature({
  name: expensesFeatureKey,
  reducer: expensesReducer,
});
