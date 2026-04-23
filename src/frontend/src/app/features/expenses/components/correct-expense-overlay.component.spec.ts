import { of, Subject } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ExpensesFacade } from '../state/expenses.facade';
import { CorrectExpenseOverlayComponent } from './correct-expense-overlay.component';

describe('CorrectExpenseOverlayComponent', () => {
  const originalExpense = {
    id: 'exp-1',
    shopId: 'shop-1',
    categoryId: 'cat-1',
    categoryName: 'Rent',
    amount: 500,
    paidTo: 'Landlord',
    description: 'Monthly rent',
    expenseDate: '2026-04-20T00:00:00Z',
    actorUserId: 'user-1',
    isVoided: false,
    originalExpenseId: null,
    createdAt: '2026-04-20T10:00:00Z',
  };

  const expensesFacade = {
    categories$: of([]),
    submitting$: of(false),
    error$: of(''),
    loadCategories: vi.fn(),
    clearError: vi.fn(),
    clearMutationStatus: vi.fn(),
    correctExpense: vi.fn(),
    loadExpenses: vi.fn(),
    mutationStatus$: of({ type: null as 'record-expense' | 'correct-expense' | null, succeeded: false }),
  };

  const mutationSubject = new Subject<{ type: 'record-expense' | 'correct-expense' | null; succeeded: boolean }>();

  beforeEach(() => {
    expensesFacade.loadCategories.mockReset();
    expensesFacade.clearError.mockReset();
    expensesFacade.clearMutationStatus.mockReset();
    expensesFacade.correctExpense.mockReset();
    expensesFacade.loadExpenses.mockReset();

    TestBed.configureTestingModule({
      imports: [CorrectExpenseOverlayComponent],
      providers: [
        {
          provide: ExpensesFacade,
          useValue: {
            ...expensesFacade,
            mutationStatus$: mutationSubject.asObservable(),
          },
        },
      ],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('initializes, loads categories, and prepopulates form on ngOnInit', () => {
    const fixture = TestBed.createComponent(CorrectExpenseOverlayComponent);
    const component = fixture.componentInstance;
    component.expenseId = 'exp-1';
    component.originalExpense = originalExpense;

    component.ngOnInit();

    expect(expensesFacade.clearError).toHaveBeenCalled();
    expect(expensesFacade.clearMutationStatus).toHaveBeenCalled();
    expect(expensesFacade.loadCategories).toHaveBeenCalled();

    expect(component.form.value).toEqual({
      categoryName: 'Rent',
      amount: 500,
      paidTo: 'Landlord',
      description: 'Monthly rent',
      expenseDate: '2026-04-20',
    });
  });

  it('prepopulates form with empty description when original is null', () => {
    const fixture = TestBed.createComponent(CorrectExpenseOverlayComponent);
    const component = fixture.componentInstance;
    component.expenseId = 'exp-1';
    component.originalExpense = { ...originalExpense, description: null };

    component.ngOnInit();

    expect(component.form.value.description).toBe('');
  });

  it('marks form touched and does not submit when form is invalid', () => {
    const fixture = TestBed.createComponent(CorrectExpenseOverlayComponent);
    const component = fixture.componentInstance;
    component.expenseId = 'exp-1';
    component.originalExpense = originalExpense;

    component.form.setValue({
      categoryName: '',
      amount: 0,
      paidTo: '',
      description: '',
      expenseDate: '',
    });

    component.onSubmit();

    expect(expensesFacade.correctExpense).not.toHaveBeenCalled();
    expect(component.form.touched).toBe(true);
  });

  it('dispatches correctExpense when form is valid', () => {
    const fixture = TestBed.createComponent(CorrectExpenseOverlayComponent);
    const component = fixture.componentInstance;
    component.expenseId = 'exp-1';
    component.originalExpense = originalExpense;

    component.form.setValue({
      categoryName: 'Updated Rent',
      amount: 550,
      paidTo: 'Updated Landlord',
      description: 'Updated description',
      expenseDate: '2026-04-21',
    });

    component.onSubmit();

    expect(expensesFacade.correctExpense).toHaveBeenCalledWith('exp-1', {
      categoryName: 'Updated Rent',
      amount: 550,
      paidTo: 'Updated Landlord',
      description: 'Updated description',
      expenseDate: '2026-04-21',
    });
  });

  it('trims strings and converts empty description to null on submit', () => {
    const fixture = TestBed.createComponent(CorrectExpenseOverlayComponent);
    const component = fixture.componentInstance;
    component.expenseId = 'exp-1';
    component.originalExpense = originalExpense;

    component.form.setValue({
      categoryName: '  Rent  ',
      amount: 500,
      paidTo: '  Landlord  ',
      description: '   ',
      expenseDate: '2026-04-20',
    });

    component.onSubmit();

    expect(expensesFacade.correctExpense).toHaveBeenCalledWith('exp-1', {
      categoryName: 'Rent',
      amount: 500,
      paidTo: 'Landlord',
      description: null,
      expenseDate: '2026-04-20',
    });
  });

  it('emits close on onClose when not submitting', () => {
    const fixture = TestBed.createComponent(CorrectExpenseOverlayComponent);
    const component = fixture.componentInstance;
    component.expenseId = 'exp-1';
    component.originalExpense = originalExpense;
    let closed = false;
    component.close.subscribe(() => {
      closed = true;
    });

    component.onClose();
    expect(closed).toBe(true);
  });

  it('loads expenses and emits close on successful mutation', () => {
    const fixture = TestBed.createComponent(CorrectExpenseOverlayComponent);
    const component = fixture.componentInstance;
    component.expenseId = 'exp-1';
    component.originalExpense = originalExpense;
    let closed = false;
    component.close.subscribe(() => {
      closed = true;
    });

    mutationSubject.next({ type: 'correct-expense', succeeded: true });

    expect(expensesFacade.loadExpenses).toHaveBeenCalled();
    expect(closed).toBe(true);
    expect(expensesFacade.clearMutationStatus).toHaveBeenCalled();
  });
});
