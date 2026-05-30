import { TestBed } from '@angular/core/testing';
import { Actions } from '@ngrx/effects';
import { Action } from '@ngrx/store';
import { Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';
import { take } from 'rxjs/operators';
import { vi } from 'vitest';

import { ExpenseService } from '../services/expense.service';
import { ExpenseCategoryService } from '../services/expense-category.service';
import { ExpensesActions } from './expenses.actions';
import { ExpensesEffects } from './expenses.effects';

describe('ExpensesEffects', () => {
  let actions$: Subject<Action>;
  let effects: ExpensesEffects;

  const expenseService = {
    getExpenses: vi.fn<ExpenseService['getExpenses']>(),
    getExpenseById: vi.fn<ExpenseService['getExpenseById']>(),
    recordExpense: vi.fn<ExpenseService['recordExpense']>(),
    correctExpense: vi.fn<ExpenseService['correctExpense']>(),
  };

  const categoryService = {
    getCategories: vi.fn<ExpenseCategoryService['getCategories']>(),
  };

  const makeExpenseListItem = (id = 'exp-1') => ({
    id,
    amount: 500,
    categoryName: 'Rent',
    paidTo: 'Landlord',
    expenseDate: '2026-04-20T00:00:00Z',
    isVoided: false,
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

  const makeCategory = (id = 'cat-1') => ({ id, name: 'Rent' });

  beforeEach(() => {
    actions$ = new Subject<Action>();
    expenseService.getExpenses.mockReset();
    expenseService.getExpenseById.mockReset();
    expenseService.recordExpense.mockReset();
    expenseService.correctExpense.mockReset();
    categoryService.getCategories.mockReset();

    TestBed.configureTestingModule({
      providers: [
        ExpensesEffects,
        { provide: ExpenseService, useValue: expenseService },
        { provide: ExpenseCategoryService, useValue: categoryService },
        {
          provide: Actions,
          useFactory: (): Observable<Action> => new Actions(actions$),
        },
      ],
    });

    effects = TestBed.inject(ExpensesEffects);
  });

  afterEach(() => {
    actions$.complete();
    TestBed.resetTestingModule();
  });

  it('dispatches loadExpensesSucceeded on load success', async () => {
    const expenses = [makeExpenseListItem()];
    expenseService.getExpenses.mockReturnValue(
      of({ items: expenses, totalCount: 1, pageNumber: 1, pageSize: 20 })
    );

    const output = firstValueFrom(effects.loadExpenses$.pipe(take(1)));
    actions$.next(ExpensesActions.loadExpensesRequested({}));

    await expect(output).resolves.toEqual(
      ExpensesActions.loadExpensesSucceeded({ expenses, totalCount: 1, page: 1, pageSize: 20 })
    );
  });

  it('dispatches loadExpensesFailed on load failure', async () => {
    expenseService.getExpenses.mockReturnValue(throwError(() => ({ message: 'Server error' })));

    const output = firstValueFrom(effects.loadExpenses$.pipe(take(1)));
    actions$.next(ExpensesActions.loadExpensesRequested({}));

    await expect(output).resolves.toEqual(
      ExpensesActions.loadExpensesFailed({ errorMessage: 'Server error' })
    );
  });

  it('dispatches loadExpenseDetailSucceeded on detail load success', async () => {
    const expense = makeExpenseDto();
    expenseService.getExpenseById.mockReturnValue(of(expense));

    const output = firstValueFrom(effects.loadExpenseDetail$.pipe(take(1)));
    actions$.next(ExpensesActions.loadExpenseDetailRequested({ expenseId: 'exp-1' }));

    await expect(output).resolves.toEqual(
      ExpensesActions.loadExpenseDetailSucceeded({ expense })
    );
  });

  it('dispatches loadExpenseDetailFailed on detail load failure', async () => {
    expenseService.getExpenseById.mockReturnValue(throwError(() => ({ message: 'Not found' })));

    const output = firstValueFrom(effects.loadExpenseDetail$.pipe(take(1)));
    actions$.next(ExpensesActions.loadExpenseDetailRequested({ expenseId: 'exp-1' }));

    await expect(output).resolves.toEqual(
      ExpensesActions.loadExpenseDetailFailed({ errorMessage: 'Not found' })
    );
  });

  it('dispatches loadCategoriesSucceeded on categories load success', async () => {
    const categories = [makeCategory()];
    categoryService.getCategories.mockReturnValue(of(categories));

    const output = firstValueFrom(effects.loadCategories$.pipe(take(1)));
    actions$.next(ExpensesActions.loadCategoriesRequested());

    await expect(output).resolves.toEqual(
      ExpensesActions.loadCategoriesSucceeded({ categories })
    );
  });

  it('dispatches loadCategoriesFailed on categories load failure', async () => {
    categoryService.getCategories.mockReturnValue(throwError(() => ({ message: 'Server error' })));

    const output = firstValueFrom(effects.loadCategories$.pipe(take(1)));
    actions$.next(ExpensesActions.loadCategoriesRequested());

    await expect(output).resolves.toEqual(
      ExpensesActions.loadCategoriesFailed({ errorMessage: 'Server error' })
    );
  });

  it('dispatches recordExpenseSucceeded on record success', async () => {
    const expense = makeExpenseDto();
    expenseService.recordExpense.mockReturnValue(of(expense));

    const output = firstValueFrom(effects.recordExpense$.pipe(take(1)));
    actions$.next(
      ExpensesActions.recordExpenseRequested({
        payload: { categoryName: 'Rent', amount: 500, paidTo: 'Landlord', description: null, expenseDate: '2026-04-20' },
      })
    );

    await expect(output).resolves.toEqual(
      ExpensesActions.recordExpenseSucceeded({ expense })
    );
  });

  it('dispatches recordExpenseFailed on record failure', async () => {
    expenseService.recordExpense.mockReturnValue(throwError(() => ({ message: 'Invalid amount' })));

    const output = firstValueFrom(effects.recordExpense$.pipe(take(1)));
    actions$.next(
      ExpensesActions.recordExpenseRequested({
        payload: { categoryName: 'Rent', amount: 500, paidTo: 'Landlord', description: null, expenseDate: '2026-04-20' },
      })
    );

    await expect(output).resolves.toEqual(
      ExpensesActions.recordExpenseFailed({ errorMessage: 'Invalid amount' })
    );
  });

  it('dispatches correctExpenseSucceeded on correct success', async () => {
    const expense = makeExpenseDto();
    expenseService.correctExpense.mockReturnValue(of(expense));

    const output = firstValueFrom(effects.correctExpense$.pipe(take(1)));
    actions$.next(
      ExpensesActions.correctExpenseRequested({
        expenseId: 'exp-1',
        payload: { categoryName: 'Rent', amount: 550, paidTo: 'Landlord', description: null, expenseDate: '2026-04-20' },
      })
    );

    await expect(output).resolves.toEqual(
      ExpensesActions.correctExpenseSucceeded({ expense })
    );
  });

  it('dispatches correctExpenseFailed on correct failure', async () => {
    expenseService.correctExpense.mockReturnValue(throwError(() => ({ message: 'Already voided' })));

    const output = firstValueFrom(effects.correctExpense$.pipe(take(1)));
    actions$.next(
      ExpensesActions.correctExpenseRequested({
        expenseId: 'exp-1',
        payload: { categoryName: 'Rent', amount: 550, paidTo: 'Landlord', description: null, expenseDate: '2026-04-20' },
      })
    );

    await expect(output).resolves.toEqual(
      ExpensesActions.correctExpenseFailed({ errorMessage: 'Already voided' })
    );
  });
});
