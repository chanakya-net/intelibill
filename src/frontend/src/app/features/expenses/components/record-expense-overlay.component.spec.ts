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
import { RecordExpenseOverlayComponent } from './record-expense-overlay.component';

describe('RecordExpenseOverlayComponent', () => {
  const mockCategories = [
    { id: 'cat-1', name: 'Rent' },
    { id: 'cat-2', name: 'Supplier Payments' },
  ];

  const localizedError = signal<LocalizedError | undefined>(undefined);
  const expensesFacade = {
    localizedError,
    categories$: of(mockCategories),
    submitting$: of(false),
    error$: of(''),
    loadCategories: vi.fn(),
    clearError: vi.fn(),
    clearMutationStatus: vi.fn(),
    recordExpense: vi.fn(),
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
    expensesFacade.recordExpense.mockReset();
    expensesFacade.loadExpenses.mockReset();
    expensesFacade.refreshExpenses.mockReset();

    TestBed.configureTestingModule({
      imports: [
        RecordExpenseOverlayComponent,
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
    const fixture = TestBed.createComponent(RecordExpenseOverlayComponent);

    fixture.detectChanges();
    const form = fixture.componentInstance.form;
    form.patchValue({ paidTo: mode === 'server' ? 'Payee' : '', amount: mode === 'server' ? 1 : 0 });
    if (mode !== 'server') { form.controls.paidTo.markAsTouched(); form.controls.amount.markAsTouched(); }
    if (mode !== 'local') localizedError.set({ messageKey: 'apiErrors.validationFailed', params: {}, fieldErrors: ['paidTo','amount'].map(field => ({field, messageKey:'apiErrors.invalidField', params: {}})) });
    fixture.detectChanges();
    for (const [field, selector, localId] of [['paidTo','input[formcontrolname="paidTo"]','record-paid-to-error'],['amount','p-inputnumber[formcontrolname="amount"] input','record-amount-error']]) {
      const input = fixture.nativeElement.querySelector(selector) as HTMLInputElement;
      expect(input.getAttribute('aria-invalid')).toBe('true');
      const ids = input.getAttribute('aria-describedby')?.split(' ') ?? [];
      expect(ids).toHaveLength(mode === 'both' ? 2 : 1);
      if (mode !== 'server') expect(ids).toContain(localId);
      if (mode !== 'local') expect(ids).toContain('record-expense-overlay-' + field + '-error');
      expect(ids.every(id => fixture.nativeElement.querySelector('#' + id))).toBe(true);
    }
  });

  it('associates the mapped expenseDate error with the datepicker native input', () => {
    const fixture = TestBed.createComponent(RecordExpenseOverlayComponent);

    localizedError.set({messageKey:'apiErrors.validationFailed', params:{}, fieldErrors:[{field:'expenseDate',messageKey:'apiErrors.invalidField',params:{}}]});
    fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('p-datepicker[formcontrolname="expenseDate"] input') as HTMLInputElement;
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.getAttribute('aria-describedby')).toBe('record-expense-overlay-expenseDate-error');
    expect(fixture.nativeElement.querySelector('#' + input.getAttribute('aria-describedby'))).toBeTruthy();
  });

  it('records an explicitly selected operating treatment', () => {
    const fixture = TestBed.createComponent(RecordExpenseOverlayComponent);
    fixture.detectChanges();
    const select = fixture.nativeElement.querySelector(
      '[data-testid="expense-treatment"]',
    ) as HTMLSelectElement | null;
    expect(select).not.toBeNull();
    select!.value = 'Operating';
    select!.dispatchEvent(new Event('change'));
    fixture.componentInstance.form.patchValue({
      categoryName: 'Rent',
      amount: 20,
      paidTo: 'Landlord',
    });
    fixture.componentInstance.onSubmit();
    expect(expensesFacade.recordExpense).toHaveBeenCalledWith(
      expect.objectContaining({ treatment: 'Operating' }),
    );
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

    fixture.detectChanges();
    component.onSubmit();

    expect(expensesFacade.recordExpense).not.toHaveBeenCalled();
    expect(component.form.touched).toBe(true);
  });

  it('rejects whitespace-only required expense text', () => {
    const fixture = TestBed.createComponent(RecordExpenseOverlayComponent);
    const component = fixture.componentInstance;

    component.form.patchValue({ categoryName: '   ', paidTo: '   ', amount: 500 });

    expect(component.form.controls.categoryName.hasError('required')).toBe(true);
    expect(component.form.controls.paidTo.hasError('required')).toBe(true);
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

    fixture.detectChanges();
    component.onSubmit();

    expect(expensesFacade.recordExpense).toHaveBeenCalledWith({
      tillOwner: { userId: 'user-1', shopId: 'shop-1' },
      treatment: 'Unclassified',
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

    fixture.detectChanges();
    component.onSubmit();

    expect(expensesFacade.recordExpense).toHaveBeenCalledWith({
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
    const fixture = TestBed.createComponent(RecordExpenseOverlayComponent);
    const component = fixture.componentInstance;

    const names = component.selectableCategories().map((item) => item.name);
    expect(names).toEqual(['Rent']);
  });

  it('allows entering a new category from the category select', () => {
    const fixture = TestBed.createComponent(RecordExpenseOverlayComponent);
    fixture.detectChanges();

    const categorySelect = fixture.debugElement.query(By.directive(Select))
      .componentInstance as Select;

    expect(categorySelect.editable).toBe(true);
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

    expect(expensesFacade.refreshExpenses).toHaveBeenCalledTimes(1);
    expect(expensesFacade.loadExpenses).not.toHaveBeenCalled();
    expect(closed).toBe(true);
    expect(expensesFacade.clearMutationStatus).toHaveBeenCalled();
  });
});
