import {
  ApiErrorMessageService,
  type LocalizedError,
} from '../../../core/i18n/api-error-message.service';
import { inject, Injectable } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { EMPTY, catchError, map, of, switchMap, takeUntil } from 'rxjs';

import { isScopeChangeError } from '../../../core/context/scoped-request';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { ShopContextActions } from '../../../core/context/shop-context.actions';

import { ExpenseService } from '../services/expense.service';
import { ExpenseCategoryService } from '../services/expense-category.service';
import { ExpensesActions } from './expenses.actions';

@Injectable()
export class ExpensesEffects {
  private readonly apiErrors = inject(ApiErrorMessageService);
  private readonly actions$ = inject(Actions);
  private readonly shopContext = inject(ShopContextService);
  private readonly expenseService = inject(ExpenseService);
  private readonly categoryService = inject(ExpenseCategoryService);

  loadExpenses$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ExpensesActions.loadExpensesRequested),
      switchMap(({ query, requestId }) =>
        this.expenseService.getExpenses(query).pipe(
          map((response) => {
            const lastPage = Math.max(1, Math.ceil(response.totalCount / response.pageSize));
            if (query.page > lastPage) {
              return ExpensesActions.loadExpensesRequested({
                query: { ...query, page: lastPage },
                requestId: crypto.randomUUID(),
              });
            }
            return ExpensesActions.loadExpensesSucceeded({
              requestId,
              expenses: response.items,
              totalCount: response.totalCount,
              page: response.pageNumber,
              pageSize: response.pageSize,
            });
          }),
          catchError((error) =>
            of(
              ExpensesActions.loadExpensesFailed({
                requestId,
                ...this.feedback(error, 'expenses.errors.unexpected'),
              }),
            ),
          ),
          takeUntil(this.actions$.pipe(ofType(ShopContextActions.scopeChanged))),
        ),
      ),
    ),
  );

  loadExpenseDetail$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ExpensesActions.loadExpenseDetailRequested),
      switchMap(({ expenseId }) =>
        this.expenseService.getExpenseById(expenseId).pipe(
          map((expense) => ExpensesActions.loadExpenseDetailSucceeded({ expense })),
          catchError((error) =>
            of(
              ExpensesActions.loadExpenseDetailFailed({
                ...this.feedback(error, 'expenses.errors.unexpected'),
              }),
            ),
          ),
        ),
      ),
    ),
  );

  loadCategories$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ExpensesActions.loadCategoriesRequested),
      switchMap(() =>
        this.categoryService.getCategories().pipe(
          map((categories) => ExpensesActions.loadCategoriesSucceeded({ categories })),
          catchError((error) =>
            of(
              ExpensesActions.loadCategoriesFailed({
                ...this.feedback(error, 'expenses.errors.unexpected'),
              }),
            ),
          ),
        ),
      ),
    ),
  );

  recordExpense$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ExpensesActions.recordExpenseRequested),
      switchMap(({ payload }) =>
        this.expenseService.recordExpense(payload).pipe(
          map((expense) => ExpensesActions.recordExpenseSucceeded({ expense })),
          catchError((error) =>
            isScopeChangeError(error)
              ? EMPTY
              : of(
                  ExpensesActions.recordExpenseFailed({
                    ...this.feedback(error, 'expenses.errors.unexpected'),
                  }),
                ),
          ),
          // Stop UI outcomes immediately; the durable async operation still settles under its owner.
          takeUntil(this.shopContext.changes$),
        ),
      ),
    ),
  );

  correctExpense$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ExpensesActions.correctExpenseRequested),
      switchMap(({ expenseId, payload }) =>
        this.expenseService.correctExpense(expenseId, payload).pipe(
          map((expense) => ExpensesActions.correctExpenseSucceeded({ expense })),
          catchError((error) =>
            isScopeChangeError(error)
              ? EMPTY
              : of(
                  ExpensesActions.correctExpenseFailed({
                    ...this.feedback(error, 'expenses.errors.unexpected'),
                  }),
                ),
          ),
          // Stop UI outcomes immediately; the durable async operation still settles under its owner.
          takeUntil(this.shopContext.changes$),
        ),
      ),
    ),
  );
  private feedback(
    error: unknown,
    fallbackKey: string,
  ): { errorMessage: string; localizedError?: LocalizedError } {
    const localizedError = this.apiErrors.resolve(error, {
      fallbackKey,
      codeMap: {},
      fieldMap: {
        Amount: 'amount',
        CategoryName: 'categoryName',
        PaidTo: 'paidTo',
        ExpenseDate: 'expenseDate',
      },
    });
    return {
      errorMessage: localizedError.messageKey,
      ...(localizedError.correlationId || localizedError.fieldErrors.length
        ? { localizedError }
        : {}),
    };
  }
}
