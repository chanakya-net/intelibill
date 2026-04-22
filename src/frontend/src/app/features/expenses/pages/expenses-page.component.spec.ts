import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { ExpensesFacade } from '../state/expenses.facade';
import { ExpensesPageComponent } from './expenses-page.component';

describe('ExpensesPageComponent', () => {
  const expensesFacade = {
    expenses$: of([]),
    loading$: of(false),
    error$: of(''),
    pagination$: of({ totalCount: 0, currentPage: 1, pageSize: 20 }),
    selectedExpense$: of(null),
    loadExpenses: vi.fn(),
    loadExpenseDetail: vi.fn(),
    clearError: vi.fn(),
    clearMutationStatus: vi.fn(),
    clearExpenseDetail: vi.fn(),
  };

  const authService = {
    session: signal({
      accessToken: 'access-token',
      refreshToken: 'refresh-token',
      accessTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
      refreshTokenExpiresAt: new Date(Date.now() + 120_000).toISOString(),
      rememberMe: true,
      user: {
        id: 'owner-1',
        email: 'owner@test.com',
        phoneNumber: null,
        firstName: 'Owner',
        lastName: 'One',
      },
      activeShopId: 'shop-1',
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null }],
    }),
  };

  beforeEach(() => {
    expensesFacade.loadExpenses.mockReset();
    expensesFacade.loadExpenseDetail.mockReset();
    expensesFacade.clearError.mockReset();
    expensesFacade.clearMutationStatus.mockReset();
    expensesFacade.clearExpenseDetail.mockReset();

    TestBed.configureTestingModule({
      imports: [ExpensesPageComponent],
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: ExpensesFacade, useValue: expensesFacade },
      ],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('dispatches loadExpenses on construction', () => {
    TestBed.createComponent(ExpensesPageComponent);
    expect(expensesFacade.loadExpenses).toHaveBeenCalled();
  });

  it('opens and closes record overlay', () => {
    const fixture = TestBed.createComponent(ExpensesPageComponent);
    const component = fixture.componentInstance;

    expect(component.showRecordOverlay()).toBe(false);

    component.openRecordOverlay();
    expect(component.showRecordOverlay()).toBe(true);
    expect(expensesFacade.clearError).toHaveBeenCalled();
    expect(expensesFacade.clearMutationStatus).toHaveBeenCalled();

    component.closeRecordOverlay();
    expect(component.showRecordOverlay()).toBe(false);
  });

  it('opens and closes correct overlay', () => {
    const fixture = TestBed.createComponent(ExpensesPageComponent);
    const component = fixture.componentInstance;

    expect(component.showCorrectOverlay()).toBe(false);
    expect(component.selectedExpenseId()).toBeNull();

    component.openCorrectOverlay('exp-1');
    expect(component.showCorrectOverlay()).toBe(true);
    expect(component.selectedExpenseId()).toBe('exp-1');
    expect(expensesFacade.loadExpenseDetail).toHaveBeenCalledWith('exp-1');
    expect(expensesFacade.clearError).toHaveBeenCalled();
    expect(expensesFacade.clearMutationStatus).toHaveBeenCalled();

    component.closeCorrectOverlay();
    expect(component.showCorrectOverlay()).toBe(false);
    expect(component.selectedExpenseId()).toBeNull();
    expect(expensesFacade.clearExpenseDetail).toHaveBeenCalled();
  });

  it('triggers search on onSearch', () => {
    const fixture = TestBed.createComponent(ExpensesPageComponent);
    const component = fixture.componentInstance;

    component.searchValue.set('rent');
    component.onSearch();

    expect(expensesFacade.loadExpenses).toHaveBeenCalledWith('rent', 1);
  });

  it('triggers page change on onPageChange', () => {
    const fixture = TestBed.createComponent(ExpensesPageComponent);
    const component = fixture.componentInstance;

    component.searchValue.set('rent');
    component.onPageChange(2);

    expect(expensesFacade.loadExpenses).toHaveBeenCalledWith('rent', 2);
  });

  it('computes canManageExpenses for owner role', () => {
    const fixture = TestBed.createComponent(ExpensesPageComponent);
    const component = fixture.componentInstance;

    expect(component.canManageExpenses()).toBe(true);
  });
});
