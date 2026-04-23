import { createFeatureSelector, createSelector } from '@ngrx/store';

import { expensesAdapter, ExpensesState, expensesFeatureKey } from './expenses.reducer';

export const selectExpensesState = createFeatureSelector<ExpensesState>(expensesFeatureKey);

export const { selectAll: selectAllExpenses } = expensesAdapter.getSelectors(selectExpensesState);

export const selectExpensesLoading = createSelector(
  selectExpensesState,
  (state) => state.loadingExpenses
);

export const selectExpensesError = createSelector(
  selectExpensesState,
  (state) => state.errorMessage
);

export const selectSelectedExpense = createSelector(
  selectExpensesState,
  (state) => state.selectedExpense
);

export const selectExpenseDetailLoading = createSelector(
  selectExpensesState,
  (state) => state.loadingExpenseDetail
);

export const selectCategories = createSelector(
  selectExpensesState,
  (state) => state.categories
);

export const selectCategoriesLoading = createSelector(
  selectExpensesState,
  (state) => state.loadingCategories
);

export const selectSubmitting = createSelector(
  selectExpensesState,
  (state) => state.submitting
);

export const selectLastMutationStatus = createSelector(
  selectExpensesState,
  (state) => ({
    type: state.lastMutationType,
    succeeded: state.lastMutationSucceeded,
  })
);

export const selectPagination = createSelector(
  selectExpensesState,
  (state) => ({
    totalCount: state.totalCount,
    currentPage: state.currentPage,
    pageSize: state.pageSize,
  })
);
