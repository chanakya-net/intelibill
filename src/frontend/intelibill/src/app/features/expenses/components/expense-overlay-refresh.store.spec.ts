import { disabledTillProviders } from '../../../../tests/tills/disabled-till.providers';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { Store, provideState, provideStore } from '@ngrx/store';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ExpenseDto, ExpenseListQuery } from '../services/expense.service';
import { ExpensesActions } from '../state/expenses.actions';
import { ExpensesFacade } from '../state/expenses.facade';
import { expensesFeature } from '../state/expenses.reducer';
import { selectExpensesQuery } from '../state/expenses.selectors';
import { CorrectExpenseOverlayComponent } from './correct-expense-overlay.component';
import { RecordExpenseOverlayComponent } from './record-expense-overlay.component';

const expense: ExpenseDto = {
  id: 'expense-original',
  shopId: 'shop-1',
  categoryId: 'rent',
  categoryName: 'Rent',
  amount: 100,
  paidTo: 'Landlord',
  description: null,
  expenseDate: '2026-06-01',
  actorUserId: 'owner',
  isVoided: false,
  originalExpenseId: null,
  createdAt: '2026-06-01T00:00:00Z',
};

describe('expense overlay refresh against the real store', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [
        RecordExpenseOverlayComponent,
        CorrectExpenseOverlayComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [...disabledTillProviders(), provideStore(), provideState(expensesFeature)],
    });
  });

  afterEach(() => TestBed.resetTestingModule());

  it.each(['record', 'correct'] as const)(
    '%s consumes each success once and preserves the current query',
    (kind) => {
      const store = TestBed.inject(Store);
      const facade = TestBed.inject(ExpensesFacade);
      const refresh = vi.spyOn(facade, 'refreshExpenses');
      const initialQuery: ExpenseListQuery = {
        status: 'active',
        search: 'rent',
        page: 2,
        pageSize: 10,
      };
      facade.loadExpenses(initialQuery);

      const openOverlay = () => {
        if (kind === 'record') {
          const fixture = TestBed.createComponent(RecordExpenseOverlayComponent);
          fixture.detectChanges();
          return fixture;
        }
        const fixture = TestBed.createComponent(CorrectExpenseOverlayComponent);
        fixture.componentInstance.expenseId = expense.id;
        fixture.componentInstance.originalExpense = expense;
        fixture.detectChanges();
        return fixture;
      };
      const succeed = () =>
        store.dispatch(
          kind === 'record'
            ? ExpensesActions.recordExpenseSucceeded({ expense })
            : ExpensesActions.correctExpenseSucceeded({ expense }),
        );

      const first = openOverlay();
      const close = vi.fn();
      first.componentInstance.closeRequested.subscribe(close);
      succeed();
      expect(refresh).toHaveBeenCalledTimes(1);
      expect(close).toHaveBeenCalledTimes(1);
      expect(store.selectSignal(selectExpensesQuery)()).toEqual(initialQuery);
      first.destroy();

      const latestQuery: ExpenseListQuery = {
        status: 'voided',
        search: 'latest',
        page: 1,
        pageSize: 20,
      };
      facade.loadExpenses(latestQuery);
      const second = openOverlay();
      const closeAgain = vi.fn();
      second.componentInstance.closeRequested.subscribe(closeAgain);
      succeed();
      expect(refresh).toHaveBeenCalledTimes(2);
      expect(closeAgain).toHaveBeenCalledTimes(1);
      expect(store.selectSignal(selectExpensesQuery)()).toEqual(latestQuery);
      second.destroy();
    },
  );
});
