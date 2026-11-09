import { TestBed } from '@angular/core/testing';
import { Actions } from '@ngrx/effects';
import { Action } from '@ngrx/store';
import { Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';
import { take } from 'rxjs/operators';
import { vi } from 'vitest';

import { ShopContextActions } from '../../../core/context/shop-context.actions';
import { ExpenseService, PaginatedExpenses } from '../services/expense.service';
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

  it('keeps only safe localized feedback and reference metadata from hostile errors', async () => {
    expenseService.getExpenseById.mockReturnValue(
      throwError(() => ({
        error: {
          code: 'Untrusted.Code',
          title: '<script>title</script>',
          detail: '<img onerror=alert(1)>',
          traceId: 'safe-trace:123',
        },
      })),
    );
    const output = firstValueFrom(effects.loadExpenseDetail$.pipe(take(1)));
    actions$.next(ExpensesActions.loadExpenseDetailRequested({ expenseId: 'exp-1' }));
    expect(await output).toMatchObject({
      errorMessage: 'expenses.errors.unexpected',
      localizedError: {
        messageKey: 'expenses.errors.unexpected',
        params: {},
        fieldErrors: [],
        correlationId: 'safe-trace:123',
      },
    });
  });

  it('dispatches loadExpensesSucceeded on load success', async () => {
    const expenses = [makeExpenseListItem()];
    expenseService.getExpenses.mockReturnValue(
      of({ items: expenses, totalCount: 1, pageNumber: 1, pageSize: 20 }),
    );

    const output = firstValueFrom(effects.loadExpenses$.pipe(take(1)));
    actions$.next(
      ExpensesActions.loadExpensesRequested({
        query: { status: 'all', page: 1, pageSize: 20 },
        requestId: 'r1',
      }),
    );

    await expect(output).resolves.toEqual(
      ExpensesActions.loadExpensesSucceeded({
        requestId: 'r1',
        expenses,
        totalCount: 1,
        page: 1,
        pageSize: 20,
      }),
    );
  });

  it('dispatches loadExpensesFailed on load failure', async () => {
    expenseService.getExpenses.mockReturnValue(throwError(() => ({ message: 'Server error' })));

    const output = firstValueFrom(effects.loadExpenses$.pipe(take(1)));
    actions$.next(
      ExpensesActions.loadExpensesRequested({
        query: { status: 'all', page: 1, pageSize: 20 },
        requestId: 'r1',
      }),
    );

    await expect(output).resolves.toEqual(
      ExpensesActions.loadExpensesFailed({
        requestId: 'r1',
        errorMessage: 'expenses.errors.unexpected',
      }),
    );
  });

  it.each(['success', 'failure'])('rapid changes accept latest response despite old %s', (late) => {
    const first = new Subject<PaginatedExpenses>();
    const second = new Subject<PaginatedExpenses>();
    expenseService.getExpenses.mockReturnValueOnce(first).mockReturnValueOnce(second);
    const outputs: Action[] = [];
    const subscription = effects.loadExpenses$.subscribe((action) => outputs.push(action));
    actions$.next(
      ExpensesActions.loadExpensesRequested({
        query: { status: 'active', page: 3, pageSize: 20 },
        requestId: 'a',
      }),
    );
    actions$.next(
      ExpensesActions.loadExpensesRequested({
        query: { status: 'voided', search: 'rent', page: 1, pageSize: 20 },
        requestId: 'b',
      }),
    );
    second.next({ items: [makeExpenseListItem('b')], totalCount: 1, pageNumber: 1, pageSize: 20 });
    if (late === 'success')
      first.next({
        items: [makeExpenseListItem('a')],
        totalCount: 100,
        pageNumber: 3,
        pageSize: 20,
      });
    else first.error({ message: 'late error' });
    expect(outputs).toEqual([
      ExpensesActions.loadExpensesSucceeded({
        requestId: 'b',
        expenses: [makeExpenseListItem('b')],
        totalCount: 1,
        page: 1,
        pageSize: 20,
      }),
    ]);
    expect(expenseService.getExpenses).toHaveBeenLastCalledWith({
      status: 'voided',
      search: 'rent',
      page: 1,
      pageSize: 20,
    });
    subscription.unsubscribe();
  });

  it('scope change cancels a pending out-of-range response before recovery can reload', () => {
    const pending = new Subject<PaginatedExpenses>();
    expenseService.getExpenses.mockReturnValue(pending);
    const outputs: Action[] = [];
    const subscription = effects.loadExpenses$.subscribe((action) => outputs.push(action));
    actions$.next(
      ExpensesActions.loadExpensesRequested({
        query: { status: 'active', search: 'old shop', page: 2, pageSize: 20 },
        requestId: 'old',
      }),
    );
    actions$.next(ShopContextActions.scopeChanged({ epoch: 2, reason: 'scope' }));
    pending.next({ items: [], totalCount: 0, pageNumber: 2, pageSize: 20 });
    expect(outputs).toEqual([]);
    expect(expenseService.getExpenses).toHaveBeenCalledTimes(1);
    subscription.unsubscribe();
  });

  it.each([
    ['active', 2, 21, 2],
    ['active', 2, 20, 1],
    ['voided', 2, 21, 2],
    ['all', 3, 41, 3],
    ['all', 3, 40, 2],
    ['active', 1, 0, 1],
  ] as const)(
    'refresh %s page %i with count %i selects valid page %i',
    async (status, page, totalCount, expectedPage) => {
      expenseService.getExpenses.mockReturnValue(
        of({ items: [], totalCount, pageNumber: page, pageSize: 20 }),
      );
      const output = firstValueFrom(effects.loadExpenses$.pipe(take(1)));
      const query = { status, page, pageSize: 20, search: 'rent' };
      actions$.next(ExpensesActions.loadExpensesRequested({ query, requestId: 'original' }));
      const action = await output;
      if (expectedPage === page) {
        expect(action).toEqual(
          ExpensesActions.loadExpensesSucceeded({
            requestId: 'original',
            expenses: [],
            totalCount,
            page,
            pageSize: 20,
          }),
        );
      } else {
        expect(action).toEqual(
          ExpensesActions.loadExpensesRequested({
            query: { ...query, page: expectedPage },
            requestId: expect.any(String),
          }),
        );
        expect((action as { requestId: string }).requestId).not.toBe('original');
      }
    },
  );

  it('does not publish or recover an old out-of-range page after a newer filter', () => {
    const pending = new Subject<PaginatedExpenses>();
    expenseService.getExpenses
      .mockReturnValueOnce(pending)
      .mockReturnValueOnce(of({ items: [], totalCount: 0, pageNumber: 1, pageSize: 20 }));
    const outputs: Action[] = [];
    const subscription = effects.loadExpenses$.subscribe((action) => outputs.push(action));
    actions$.next(
      ExpensesActions.loadExpensesRequested({
        query: { status: 'active', search: 'rent', page: 2, pageSize: 20 },
        requestId: 'old',
      }),
    );
    actions$.next(
      ExpensesActions.loadExpensesRequested({
        query: { status: 'voided', page: 1, pageSize: 20 },
        requestId: 'new',
      }),
    );
    pending.next({ items: [], totalCount: 20, pageNumber: 2, pageSize: 20 });
    expect(outputs).toEqual([
      ExpensesActions.loadExpensesSucceeded({
        requestId: 'new',
        expenses: [],
        totalCount: 0,
        page: 1,
        pageSize: 20,
      }),
    ]);
    subscription.unsubscribe();
  });

  it('dispatches loadExpenseDetailSucceeded on detail load success', async () => {
    const expense = makeExpenseDto();
    expenseService.getExpenseById.mockReturnValue(of(expense));

    const output = firstValueFrom(effects.loadExpenseDetail$.pipe(take(1)));
    actions$.next(ExpensesActions.loadExpenseDetailRequested({ expenseId: 'exp-1' }));

    await expect(output).resolves.toEqual(ExpensesActions.loadExpenseDetailSucceeded({ expense }));
  });

  it('dispatches loadExpenseDetailFailed on detail load failure', async () => {
    expenseService.getExpenseById.mockReturnValue(throwError(() => ({ message: 'Not found' })));

    const output = firstValueFrom(effects.loadExpenseDetail$.pipe(take(1)));
    actions$.next(ExpensesActions.loadExpenseDetailRequested({ expenseId: 'exp-1' }));

    await expect(output).resolves.toEqual(
      ExpensesActions.loadExpenseDetailFailed({ errorMessage: 'expenses.errors.unexpected' }),
    );
  });

  it('dispatches loadCategoriesSucceeded on categories load success', async () => {
    const categories = [makeCategory()];
    categoryService.getCategories.mockReturnValue(of(categories));

    const output = firstValueFrom(effects.loadCategories$.pipe(take(1)));
    actions$.next(ExpensesActions.loadCategoriesRequested());

    await expect(output).resolves.toEqual(ExpensesActions.loadCategoriesSucceeded({ categories }));
  });

  it('dispatches loadCategoriesFailed on categories load failure', async () => {
    categoryService.getCategories.mockReturnValue(throwError(() => ({ message: 'Server error' })));

    const output = firstValueFrom(effects.loadCategories$.pipe(take(1)));
    actions$.next(ExpensesActions.loadCategoriesRequested());

    await expect(output).resolves.toEqual(
      ExpensesActions.loadCategoriesFailed({ errorMessage: 'expenses.errors.unexpected' }),
    );
  });

  it('dispatches recordExpenseSucceeded on record success', async () => {
    const expense = makeExpenseDto();
    expenseService.recordExpense.mockReturnValue(of(expense));

    const output = firstValueFrom(effects.recordExpense$.pipe(take(1)));
    actions$.next(
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

    await expect(output).resolves.toEqual(ExpensesActions.recordExpenseSucceeded({ expense }));
  });

  it('dispatches recordExpenseFailed on record failure', async () => {
    expenseService.recordExpense.mockReturnValue(throwError(() => ({ message: 'Invalid amount' })));

    const output = firstValueFrom(effects.recordExpense$.pipe(take(1)));
    actions$.next(
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

    await expect(output).resolves.toEqual(
      ExpensesActions.recordExpenseFailed({ errorMessage: 'expenses.errors.unexpected' }),
    );
  });

  it('dispatches correctExpenseSucceeded on correct success', async () => {
    const expense = makeExpenseDto();
    expenseService.correctExpense.mockReturnValue(of(expense));

    const output = firstValueFrom(effects.correctExpense$.pipe(take(1)));
    actions$.next(
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

    await expect(output).resolves.toEqual(ExpensesActions.correctExpenseSucceeded({ expense }));
  });

  it('dispatches correctExpenseFailed on correct failure', async () => {
    expenseService.correctExpense.mockReturnValue(
      throwError(() => ({ message: 'Already voided' })),
    );

    const output = firstValueFrom(effects.correctExpense$.pipe(take(1)));
    actions$.next(
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

    await expect(output).resolves.toEqual(
      ExpensesActions.correctExpenseFailed({ errorMessage: 'expenses.errors.unexpected' }),
    );
  });
});
