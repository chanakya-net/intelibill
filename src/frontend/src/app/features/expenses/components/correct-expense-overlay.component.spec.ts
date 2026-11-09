import { signal } from '@angular/core';
import type { LocalizedError } from '../../../core/i18n/api-error-message.service';
import { disabledTillProviders } from '../../../../tests/tills/disabled-till.providers';
import { of, Subject } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { Select } from 'primeng/select';

import { ExpensesFacade } from '../state/expenses.facade';
import { CorrectExpenseOverlayComponent } from './correct-expense-overlay.component';

describe('CorrectExpenseOverlayComponent', () => {
  const mockCategories = [
    { id: 'cat-1', name: 'Rent' },
    { id: 'cat-2', name: 'Supplier Payments' },
  ];

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

  const localizedError = signal<LocalizedError | undefined>(undefined);
  const expensesFacade = {
    localizedError,
    categories$: of(mockCategories),
    submitting$: of(false),
    error$: of(''),
    loadCategories: vi.fn(),
    clearError: vi.fn(),
    clearMutationStatus: vi.fn(),
    correctExpense: vi.fn(),
    loadExpenses: vi.fn(),
    refreshExpenses: vi.fn(),
    mutationStatus$: of({
      type: null as 'record-expense' | 'correct-expense' | null,
      succeeded: false,
    }),
  };

  const mutationSubject = new Subject<{
    type: 'record-expense' | 'correct-expense' | null;
    succeeded: boolean;
  }>();

  beforeEach(() => {
    localizedError.set(undefined);
    expensesFacade.loadCategories.mockReset();
    expensesFacade.clearError.mockReset();
    expensesFacade.clearMutationStatus.mockReset();
    expensesFacade.correctExpense.mockReset();
    expensesFacade.loadExpenses.mockReset();
    expensesFacade.refreshExpenses.mockReset();

    TestBed.configureTestingModule({
      imports: [
        CorrectExpenseOverlayComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [
        ...disabledTillProviders(),
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

  it.each(['local', 'server', 'both'] as const)('retains native local and server descriptions: %s', (mode) => {
    const fixture = TestBed.createComponent(CorrectExpenseOverlayComponent);
    fixture.componentInstance.originalExpense = originalExpense; fixture.componentInstance.expenseId = 'exp-1';
    fixture.detectChanges();
    const form = fixture.componentInstance.form;
    form.patchValue({ paidTo: mode === 'server' ? 'Payee' : '', amount: mode === 'server' ? 1 : 0 });
    if (mode !== 'server') { form.controls.paidTo.markAsTouched(); form.controls.amount.markAsTouched(); }
    if (mode !== 'local') localizedError.set({ messageKey: 'apiErrors.validationFailed', params: {}, fieldErrors: ['paidTo','amount'].map(field => ({field, messageKey:'apiErrors.invalidField', params: {}})) });
    fixture.detectChanges();
    for (const [field, selector, localId] of [['paidTo','input[formcontrolname="paidTo"]','correct-paid-to-error'],['amount','p-inputnumber[formcontrolname="amount"] input','correct-amount-error']]) {
      const input = fixture.nativeElement.querySelector(selector) as HTMLInputElement;
      expect(input.getAttribute('aria-invalid')).toBe('true');
      const ids = input.getAttribute('aria-describedby')?.split(' ') ?? [];
      expect(ids).toHaveLength(mode === 'both' ? 2 : 1);
      if (mode !== 'server') expect(ids).toContain(localId);
      if (mode !== 'local') expect(ids).toContain('correct-expense-overlay-' + field + '-error');
      expect(ids.every(id => fixture.nativeElement.querySelector('#' + id))).toBe(true);
    }
  });

  it('associates the mapped expenseDate error with the datepicker native input', () => {
    const fixture = TestBed.createComponent(CorrectExpenseOverlayComponent);
    fixture.componentInstance.originalExpense = originalExpense; fixture.componentInstance.expenseId = 'exp-1';
    localizedError.set({messageKey:'apiErrors.validationFailed', params:{}, fieldErrors:[{field:'expenseDate',messageKey:'apiErrors.invalidField',params:{}}]});
    fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('p-datepicker[formcontrolname="expenseDate"] input') as HTMLInputElement;
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.getAttribute('aria-describedby')).toBe('correct-expense-overlay-expenseDate-error');
    expect(fixture.nativeElement.querySelector('#' + input.getAttribute('aria-describedby'))).toBeTruthy();
  });

  it('preserves supplier settlement provenance as read-only', () => {
    const fixture = TestBed.createComponent(CorrectExpenseOverlayComponent);
    const component = fixture.componentInstance;
    component.expenseId = 'exp-1';
    component.originalExpense = {
      ...originalExpense,
      treatment: 'SupplierSettlement',
      supplierLedgerEntryId: 'ledger-1',
    };
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="expense-treatment"]')).toBeNull();
    expect(
      fixture.nativeElement.querySelector('[data-testid="settlement-provenance"]'),
    ).not.toBeNull();
    fixture.detectChanges();
    component.onSubmit();
    expect(expensesFacade.correctExpense).toHaveBeenCalledWith(
      'exp-1',
      expect.not.objectContaining({ treatment: 'Operating' }),
    );
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

    const value = component.form.getRawValue();
    expect(value.categoryName).toBe('Rent');
    expect(value.amount).toBe(500);
    expect(value.paidTo).toBe('Landlord');
    expect(value.description).toBe('Monthly rent');
    expect(value.expenseDate.toISOString().slice(0, 10)).toBe('2026-04-20');
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

    fixture.detectChanges();
    component.form.setValue({
      categoryName: '',
      amount: 0,
      paidTo: '',
      description: '',
      expenseDate: new Date('2026-04-20T00:00:00.000Z'),
    });

    fixture.detectChanges();
    component.onSubmit();

    expect(expensesFacade.correctExpense).not.toHaveBeenCalled();
    expect(component.form.touched).toBe(true);
  });

  it('rejects whitespace-only required correction text', () => {
    const fixture = TestBed.createComponent(CorrectExpenseOverlayComponent);
    const component = fixture.componentInstance;

    component.form.patchValue({ categoryName: '   ', paidTo: '   ', amount: 500 });

    expect(component.form.controls.categoryName.hasError('required')).toBe(true);
    expect(component.form.controls.paidTo.hasError('required')).toBe(true);
  });

  it('dispatches correctExpense when form is valid', () => {
    const fixture = TestBed.createComponent(CorrectExpenseOverlayComponent);
    const component = fixture.componentInstance;
    component.expenseId = 'exp-1';
    component.originalExpense = originalExpense;

    fixture.detectChanges();
    component.form.setValue({
      categoryName: 'Updated Rent',
      amount: 550,
      paidTo: 'Updated Landlord',
      description: 'Updated description',
      expenseDate: new Date('2026-04-21T00:00:00.000Z'),
    });

    fixture.detectChanges();
    component.onSubmit();

    expect(expensesFacade.correctExpense).toHaveBeenCalledWith('exp-1', {
      tillOwner: { userId: 'user-1', shopId: 'shop-1' },
      treatment: 'Unclassified',
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

    fixture.detectChanges();
    component.form.setValue({
      categoryName: '  Rent  ',
      amount: 500,
      paidTo: '  Landlord  ',
      description: '   ',
      expenseDate: new Date('2026-04-20T00:00:00.000Z'),
    });

    fixture.detectChanges();
    component.onSubmit();

    expect(expensesFacade.correctExpense).toHaveBeenCalledWith('exp-1', {
      tillOwner: { userId: 'user-1', shopId: 'shop-1' },
      treatment: 'Unclassified',
      categoryName: 'Rent',
      amount: 500,
      paidTo: 'Landlord',
      description: null,
      expenseDate: '2026-04-20',
    });
  });

  it('hides Supplier Payments from selectable categories', () => {
    const fixture = TestBed.createComponent(CorrectExpenseOverlayComponent);
    const component = fixture.componentInstance;
    component.expenseId = 'exp-1';
    component.originalExpense = originalExpense;

    const names = component.selectableCategories().map((item) => item.name);
    expect(names).toEqual(['Rent']);
  });

  it('allows entering a new category from the category select', () => {
    const fixture = TestBed.createComponent(CorrectExpenseOverlayComponent);
    const component = fixture.componentInstance;
    component.expenseId = 'exp-1';
    component.originalExpense = originalExpense;
    fixture.detectChanges();

    const categorySelect = fixture.debugElement.query(By.directive(Select))
      .componentInstance as Select;

    expect(categorySelect.editable).toBe(true);
  });

  it('emits closeRequested on onClose when not submitting', () => {
    const fixture = TestBed.createComponent(CorrectExpenseOverlayComponent);
    const component = fixture.componentInstance;
    component.expenseId = 'exp-1';
    component.originalExpense = originalExpense;
    let closed = false;
    component.closeRequested.subscribe(() => {
      closed = true;
    });

    component.onClose();
    expect(closed).toBe(true);
  });

  it('loads expenses and emits closeRequested on successful mutation', () => {
    const fixture = TestBed.createComponent(CorrectExpenseOverlayComponent);
    const component = fixture.componentInstance;
    component.expenseId = 'exp-1';
    component.originalExpense = originalExpense;
    let closed = false;
    component.closeRequested.subscribe(() => {
      closed = true;
    });

    mutationSubject.next({ type: 'correct-expense', succeeded: true });

    expect(expensesFacade.refreshExpenses).toHaveBeenCalledTimes(1);
    expect(expensesFacade.loadExpenses).not.toHaveBeenCalled();
    expect(closed).toBe(true);
    expect(expensesFacade.clearMutationStatus).toHaveBeenCalled();
  });
});
