import { inject, Injectable } from '@angular/core';
import { Store } from '@ngrx/store';

import { RecordExpenseRequest } from '../services/expense.service';
import { ExpensesActions } from './expenses.actions';
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

@Injectable({ providedIn: 'root' })
export class ExpensesFacade {
  private readonly store = inject(Store);

  readonly expenses$ = this.store.select(selectAllExpenses);
  readonly loading$ = this.store.select(selectExpensesLoading);
  readonly error$ = this.store.select(selectExpensesError);
  readonly selectedExpense$ = this.store.select(selectSelectedExpense);
  readonly loadingDetail$ = this.store.select(selectExpenseDetailLoading);
  readonly categories$ = this.store.select(selectCategories);
  readonly loadingCategories$ = this.store.select(selectCategoriesLoading);
  readonly submitting$ = this.store.select(selectSubmitting);
  readonly mutationStatus$ = this.store.select(selectLastMutationStatus);
  readonly pagination$ = this.store.select(selectPagination);

  loadExpenses(search?: string, page?: number, pageSize?: number): void {
    this.store.dispatch(ExpensesActions.loadExpensesRequested({ search, page, pageSize }));
  }

  loadExpenseDetail(expenseId: string): void {
    this.store.dispatch(ExpensesActions.loadExpenseDetailRequested({ expenseId }));
  }

  loadCategories(): void {
    this.store.dispatch(ExpensesActions.loadCategoriesRequested());
  }

  recordExpense(payload: RecordExpenseRequest): void {
    this.store.dispatch(ExpensesActions.recordExpenseRequested({ payload }));
  }

  correctExpense(expenseId: string, payload: RecordExpenseRequest): void {
    this.store.dispatch(ExpensesActions.correctExpenseRequested({ expenseId, payload }));
  }

  clearError(): void {
    this.store.dispatch(ExpensesActions.clearError());
  }

  clearMutationStatus(): void {
    this.store.dispatch(ExpensesActions.clearMutationStatus());
  }

  clearExpenseDetail(): void {
    this.store.dispatch(ExpensesActions.clearExpenseDetail());
  }
}
