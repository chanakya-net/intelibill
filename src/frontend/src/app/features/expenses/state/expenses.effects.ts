import { inject, Injectable } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, map, of, switchMap } from 'rxjs';

import { ExpenseService } from '../services/expense.service';
import { ExpenseCategoryService } from '../services/expense-category.service';
import { ExpensesActions } from './expenses.actions';

@Injectable()
export class ExpensesEffects {
  private readonly actions$ = inject(Actions);
  private readonly expenseService = inject(ExpenseService);
  private readonly categoryService = inject(ExpenseCategoryService);

  loadExpenses$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ExpensesActions.loadExpensesRequested),
      switchMap(({ search, page = 1, pageSize = 20 }) =>
        this.expenseService.getExpenses(search, page, pageSize).pipe(
          map((response) =>
            ExpensesActions.loadExpensesSucceeded({
              expenses: response.items,
              totalCount: response.totalCount,
              page: response.pageNumber,
              pageSize: response.pageSize,
            })
          ),
          catchError((error) => of(ExpensesActions.loadExpensesFailed({ errorMessage: error.message })))
        )
      )
    )
  );

  loadExpenseDetail$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ExpensesActions.loadExpenseDetailRequested),
      switchMap(({ expenseId }) =>
        this.expenseService.getExpenseById(expenseId).pipe(
          map((expense) => ExpensesActions.loadExpenseDetailSucceeded({ expense })),
          catchError((error) => of(ExpensesActions.loadExpenseDetailFailed({ errorMessage: error.message })))
        )
      )
    )
  );

  loadCategories$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ExpensesActions.loadCategoriesRequested),
      switchMap(() =>
        this.categoryService.getCategories().pipe(
          map((categories) => ExpensesActions.loadCategoriesSucceeded({ categories })),
          catchError((error) => of(ExpensesActions.loadCategoriesFailed({ errorMessage: error.message })))
        )
      )
    )
  );

  recordExpense$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ExpensesActions.recordExpenseRequested),
      switchMap(({ payload }) =>
        this.expenseService.recordExpense(payload).pipe(
          map((expense) => ExpensesActions.recordExpenseSucceeded({ expense })),
          catchError((error) => of(ExpensesActions.recordExpenseFailed({ errorMessage: error.message })))
        )
      )
    )
  );

  correctExpense$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ExpensesActions.correctExpenseRequested),
      switchMap(({ expenseId, payload }) =>
        this.expenseService.correctExpense(expenseId, payload).pipe(
          map((expense) => ExpensesActions.correctExpenseSucceeded({ expense })),
          catchError((error) => of(ExpensesActions.correctExpenseFailed({ errorMessage: error.message })))
        )
      )
    )
  );
}
