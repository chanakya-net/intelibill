import { inject, Injectable } from '@angular/core';
import { Store } from '@ngrx/store';
import { take } from 'rxjs';

import {
  DEFAULT_EXPENSE_QUERY,
  ExpenseListQuery,
  RecordExpenseRequest,
} from '../services/expense.service';
import { ExpensesActions } from './expenses.actions';
import {
  selectAllExpenses,
  selectExpensesQuery,
  selectExpensesListError,
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

  readonly query$ = this.store.select(selectExpensesQuery);
  readonly listError$ = this.store.select(selectExpensesListError);

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

  loadExpenses(query: ExpenseListQuery = DEFAULT_EXPENSE_QUERY): void {
    this.store.dispatch(
      ExpensesActions.loadExpensesRequested({
        query: { ...query },
        requestId: crypto.randomUUID(),
      }),
    );
  }

  refreshExpenses(): void {
    this.query$.pipe(take(1)).subscribe((query) => this.loadExpenses(query));
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
