import { of, Subject } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TranslocoTestingModule } from '@ngneat/transloco';

import { ExpensesFacade } from '../state/expenses.facade';
import { RecordExpenseOverlayComponent } from './record-expense-overlay.component';

describe('RecordExpenseOverlayComponent', () => {
  const mockCategories = [
    { id: 'cat-1', name: 'Rent' },
    { id: 'cat-2', name: 'Supplier Payments' },
  ];

  const expensesFacade = {
    categories$: of(mockCategories),
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
      imports: [
        RecordExpenseOverlayComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
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
      expenseDate: new Date('2026-04-20T00:00:00.000Z'),
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
      expenseDate: new Date('2026-04-20T00:00:00.000Z'),
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

  it('hides Supplier Payments from selectable categories', () => {
    const fixture = TestBed.createComponent(RecordExpenseOverlayComponent);
    const component = fixture.componentInstance;

    const names = component.selectableCategories().map((item) => item.name);
    expect(names).toEqual(['Rent']);
  });

  it('emits closeRequested on onClose when not submitting', () => {
    const fixture = TestBed.createComponent(RecordExpenseOverlayComponent);
    const component = fixture.componentInstance;
    let closed = false;
    component.closeRequested.subscribe(() => {
      closed = true;
    });

    component.onClose();
    expect(closed).toBe(true);
  });

  it('loads expenses and emits closeRequested on successful mutation', () => {
    const fixture = TestBed.createComponent(RecordExpenseOverlayComponent);
    const component = fixture.componentInstance;
    let closed = false;
    component.closeRequested.subscribe(() => {
      closed = true;
    });

    mutationSubject.next({ type: 'record-expense', succeeded: true });

    expect(expensesFacade.loadExpenses).toHaveBeenCalled();
    expect(closed).toBe(true);
    expect(expensesFacade.clearMutationStatus).toHaveBeenCalled();
  });
});
