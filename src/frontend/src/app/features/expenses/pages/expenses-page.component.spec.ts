import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { BehaviorSubject, of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { ExpenseListItemDto } from '../services/expense.service';
import { ExpensesFacade } from '../state/expenses.facade';
import { ExpensesPageComponent } from './expenses-page.component';

describe('ExpensesPageComponent', () => {
  const query$ = new BehaviorSubject({ search: 'rent', status: 'active', page: 3, pageSize: 10 });
  const rows$ = new BehaviorSubject<ExpenseListItemDto[]>([]);
  const loading$ = new BehaviorSubject(false);
  const listError$ = new BehaviorSubject('');
  const expensesFacade = {
    query$,
    listError$,
    expenses$: rows$,
    loading$,
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
      shops: [
        { shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null },
      ],
    }),
  };

  beforeEach(() => {
    rows$.next([]);
    loading$.next(false);
    listError$.next('');
    query$.next({ search: 'rent', status: 'active', page: 3, pageSize: 10 });
    expensesFacade.loadExpenses.mockReset();
    expensesFacade.loadExpenseDetail.mockReset();
    expensesFacade.clearError.mockReset();
    expensesFacade.clearMutationStatus.mockReset();
    expensesFacade.clearExpenseDetail.mockReset();

    TestBed.configureTestingModule({
      imports: [
        ExpensesPageComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
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

  it('triggers search on onSearchValueChange', () => {
    const fixture = TestBed.createComponent(ExpensesPageComponent);
    const component = fixture.componentInstance;

    component.onSearchValueChange('rent');

    expect(expensesFacade.loadExpenses).toHaveBeenCalledWith({
      search: 'rent',
      status: 'active',
      page: 1,
      pageSize: 10,
    });
    expect(component.searchValue()).toBe('rent');
  });

  it('triggers page change on onPageChange', () => {
    const fixture = TestBed.createComponent(ExpensesPageComponent);
    const component = fixture.componentInstance;

    component.onPageChange(2);

    expect(expensesFacade.loadExpenses).toHaveBeenCalledWith({
      search: 'rent',
      status: 'active',
      page: 2,
      pageSize: 10,
    });
  });

  it('renders server rows directly and page summaries exclude voided amounts', () => {
    rows$.next([
      {
        id: 'voided',
        amount: 500,
        categoryName: 'Rent',
        paidTo: 'Vendor',
        expenseDate: '2026-06-01',
        isVoided: true,
      },
    ]);
    const component = TestBed.createComponent(ExpensesPageComponent).componentInstance;
    expect(component.statusFilter()).toBe('active');
    expect(component.filteredExpenses()).toHaveLength(1);
    expect(component.pageAmount()).toBe(0);
    expect(component.activeCount()).toBe(0);
    expect(component.voidedCount()).toBe(1);
  });

  it('keeps filters usable while loading and hides table on transport failure', () => {
    const fixture = TestBed.createComponent(ExpensesPageComponent);
    loading$.next(true);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-expenses-filter-bar')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.summary-grid')).toBeNull();
    loading$.next(false);
    listError$.next('Unavailable');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-expenses-filter-bar')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-expenses-table')).toBeNull();
    expect(fixture.nativeElement.querySelector('.summary-grid')).toBeNull();
  });

  it('computes canManageExpenses for owner role', () => {
    const fixture = TestBed.createComponent(ExpensesPageComponent);
    const component = fixture.componentInstance;

    expect(component.canManageExpenses()).toBe(true);
  });

  it('renders localized expense page labels via transloco pipe', () => {
    const fixture = TestBed.createComponent(ExpensesPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('expenses.title');
    expect(host.textContent).toContain('expenses.expenseLedger');

    const searchInput = host.querySelector('input[pinputtext]') as HTMLInputElement | null;
    expect(searchInput?.getAttribute('placeholder')).toContain('expenses.searchPlaceholder');
  });

  it('status change requests page one preserving search and size', () => {
    const fixture = TestBed.createComponent(ExpensesPageComponent);
    const component = fixture.componentInstance;

    component.onStatusFilterChange('voided');
    expect(expensesFacade.loadExpenses).toHaveBeenCalledWith({
      search: 'rent',
      status: 'voided',
      page: 1,
      pageSize: 10,
    });
  });
});
