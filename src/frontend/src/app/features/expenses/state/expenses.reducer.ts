import type { LocalizedError } from '../../../core/i18n/api-error-message.service';
import { createEntityAdapter, EntityState } from '@ngrx/entity';
import { createFeature, createReducer, on } from '@ngrx/store';

import {
  DEFAULT_EXPENSE_QUERY,
  ExpenseDto,
  ExpenseListItemDto,
  ExpenseListQuery,
} from '../services/expense.service';
import { ExpenseCategoryDto } from '../services/expense-category.service';
import { ExpenseMutationType, ExpensesActions } from './expenses.actions';

export const expensesFeatureKey = 'expenses';

export const expensesAdapter = createEntityAdapter<ExpenseListItemDto>({
  selectId: (expense) => expense.id,
  sortComparer: false,
});

export interface ExpensesState extends EntityState<ExpenseListItemDto> {
  readonly query: ExpenseListQuery;
  readonly requestId: string | null;
  readonly listErrorMessage: string;
  readonly loadingExpenses: boolean;
  readonly submitting: boolean;
  readonly localizedError: LocalizedError | undefined;
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
  query: DEFAULT_EXPENSE_QUERY,
  requestId: null,
  listErrorMessage: '',
  loadingExpenses: false,
  submitting: false,
  localizedError: undefined,
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
  on(ExpensesActions.loadExpensesRequested, (state, { query, requestId }) =>
    expensesAdapter.removeAll({
      ...state,
      query: { ...query },
      requestId,
      loadingExpenses: true,
      totalCount: 0,
      currentPage: query.page,
      pageSize: query.pageSize,
      localizedError: undefined,
      errorMessage: '',
      listErrorMessage: '',
    }),
  ),
  on(
    ExpensesActions.loadExpensesSucceeded,
    (state, { expenses, totalCount, page, pageSize, requestId }) =>
      requestId !== state.requestId
        ? state
        : expensesAdapter.setAll([...expenses], {
            ...state,
            loadingExpenses: false,
            localizedError: undefined,
            errorMessage: '',
            listErrorMessage: '',
            totalCount,
            currentPage: page,
            pageSize,
          }),
  ),
  on(ExpensesActions.loadExpensesFailed, (state, { errorMessage, localizedError, requestId }) =>
    requestId !== state.requestId
      ? state
      : {
          ...state,
          loadingExpenses: false,
          localizedError,
          errorMessage,
          listErrorMessage: errorMessage,
        },
  ),

  on(ExpensesActions.loadExpenseDetailRequested, (state) => ({
    ...state,
    loadingExpenseDetail: true,
    localizedError: undefined,
    errorMessage: '',
    selectedExpense: null,
  })),
  on(ExpensesActions.loadExpenseDetailSucceeded, (state, { expense }) => ({
    ...state,
    loadingExpenseDetail: false,
    selectedExpense: expense,
  })),
  on(ExpensesActions.loadExpenseDetailFailed, (state, { errorMessage, localizedError }) => ({
    ...state,
    loadingExpenseDetail: false,
    localizedError,
    errorMessage,
  })),

  on(ExpensesActions.loadCategoriesRequested, (state) => ({
    ...state,
    loadingCategories: true,
    localizedError: undefined,
    errorMessage: '',
  })),
  on(ExpensesActions.loadCategoriesSucceeded, (state, { categories }) => ({
    ...state,
    loadingCategories: false,
    categories,
  })),
  on(ExpensesActions.loadCategoriesFailed, (state, { errorMessage, localizedError }) => ({
    ...state,
    loadingCategories: false,
    localizedError,
    errorMessage,
  })),

  on(ExpensesActions.recordExpenseRequested, (state) => ({
    ...state,
    submitting: true,
    localizedError: undefined,
    errorMessage: '',
    lastMutationType: 'record-expense' as ExpenseMutationType,
    lastMutationSucceeded: false,
  })),
  on(ExpensesActions.recordExpenseSucceeded, (state, { expense }) => ({
    ...state,
    submitting: false,
    localizedError: undefined,
    errorMessage: '',
    lastMutationType: 'record-expense' as ExpenseMutationType,
    lastMutationSucceeded: true,
    selectedExpense: expense,
  })),
  on(ExpensesActions.recordExpenseFailed, (state, { errorMessage, localizedError }) => ({
    ...state,
    submitting: false,
    localizedError,
    errorMessage,
    lastMutationType: 'record-expense' as ExpenseMutationType,
    lastMutationSucceeded: false,
  })),

  on(ExpensesActions.correctExpenseRequested, (state) => ({
    ...state,
    submitting: true,
    localizedError: undefined,
    errorMessage: '',
    lastMutationType: 'correct-expense' as ExpenseMutationType,
    lastMutationSucceeded: false,
  })),
  on(ExpensesActions.correctExpenseSucceeded, (state, { expense }) => ({
    ...state,
    submitting: false,
    localizedError: undefined,
    errorMessage: '',
    lastMutationType: 'correct-expense' as ExpenseMutationType,
    lastMutationSucceeded: true,
    selectedExpense: expense,
  })),
  on(ExpensesActions.correctExpenseFailed, (state, { errorMessage, localizedError }) => ({
    ...state,
    submitting: false,
    localizedError,
    errorMessage,
    lastMutationType: 'correct-expense' as ExpenseMutationType,
    lastMutationSucceeded: false,
  })),

  on(ExpensesActions.clearError, (state) => ({
    ...state,
    localizedError: undefined,
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
  })),
);

export const expensesFeature = createFeature({
  name: expensesFeatureKey,
  reducer: expensesReducer,
});
