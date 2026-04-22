import { of, Subject } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ExpensesFacade } from '../state/expenses.facade';
import { RecordExpenseOverlayComponent } from './record-expense-overlay.component';

describe('RecordExpenseOverlayComponent', () => {
  const expensesFacade = {
    categories$: of([]),
    submitting$: of(false),
    error$: of(''),
    loadCategories: vi.fn(),
    clearError: vi.fn(),
    clearMutationStatus: vi.fn(),
    recordExpense: vi.fn(),
    loadExpenses: vi.fn(),
    mutationStatus$: of({ type: null as 'record-expense' | 'correct-expense' | null, succeeded: false }),
  };

  const mutationSubject = new Subject<{ type: 'record-expense' | 'correct-expense' | null; succeeded: boolean }>();

  beforeEach(() => {
    expensesFacade.loadCategories.mockReset();
    expensesFacade.clearError.mockReset();
    expensesFacade.clearMutationStatus.mockReset();
    expensesFacade.recordExpense.mockReset();
    expensesFacade.loadExpenses.mockReset();

    TestBed.configureTestingModule({
      imports: [RecordExpenseOverlayComponent],
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

  it('initializes and loads categories on ngOnInit', () => {
    const fixture = TestBed.createComponent(RecordExpenseOverlayComponent);
    const component = fixture.componentInstance;

    component.ngOnInit();

    expect(expensesFacade.clearError).toHaveBeenCalled();
    expect(expensesFacade.clearMutationStatus).toHaveBeenCalled();
    expect(expensesFacade.loadCategories).toHaveBeenCalled();
  });

  it('marks form touched and does not submit when form is invalid', () => {
    const fixture = TestBed.createComponent(RecordExpenseOverlayComponent);
    const component = fixture.componentInstance;

    component.onSubmit();

    expect(expensesFacade.recordExpense).not.toHaveBeenCalled();
    expect(component.form.touched).toBe(true);
  });

  it('dispatches recordExpense when form is valid', () => {
    const fixture = TestBed.createComponent(RecordExpenseOverlayComponent);
    const component = fixture.componentInstance;

    component.form.setValue({
      categoryName: 'Rent',
      amount: 500,
      paidTo: 'Landlord',
      description: 'Monthly rent',
      expenseDate: '2026-04-20',
    });

    component.onSubmit();

    expect(expensesFacade.recordExpense).toHaveBeenCalledWith({
      categoryName: 'Rent',
      amount: 500,
      paidTo: 'Landlord',
      description: 'Monthly rent',
      expenseDate: '2026-04-20',
    });
  });

  it('trims strings and converts empty description to null', () => {
    const fixture = TestBed.createComponent(RecordExpenseOverlayComponent);
    const component = fixture.componentInstance;

    component.form.setValue({
      categoryName: '  Rent  ',
      amount: 500,
      paidTo: '  Landlord  ',
      description: '   ',
      expenseDate: '2026-04-20',
    });

    component.onSubmit();

    expect(expensesFacade.recordExpense).toHaveBeenCalledWith({
      categoryName: 'Rent',
      amount: 500,
      paidTo: 'Landlord',
      description: null,
      expenseDate: '2026-04-20',
    });
  });

  it('emits close on onClose when not submitting', () => {
    const fixture = TestBed.createComponent(RecordExpenseOverlayComponent);
    const component = fixture.componentInstance;
    let closed = false;
    component.close.subscribe(() => {
      closed = true;
    });

    component.onClose();
    expect(closed).toBe(true);
  });

  it('loads expenses and emits close on successful mutation', () => {
    const fixture = TestBed.createComponent(RecordExpenseOverlayComponent);
    const component = fixture.componentInstance;
    let closed = false;
    component.close.subscribe(() => {
      closed = true;
    });

    mutationSubject.next({ type: 'record-expense', succeeded: true });

    expect(expensesFacade.loadExpenses).toHaveBeenCalled();
    expect(closed).toBe(true);
    expect(expensesFacade.clearMutationStatus).toHaveBeenCalled();
  });
});
