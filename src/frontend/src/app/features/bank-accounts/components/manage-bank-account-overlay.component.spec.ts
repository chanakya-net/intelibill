import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { BankAccountsFacade } from '../state/bank-accounts.facade';
import { ManageBankAccountOverlayComponent } from './manage-bank-account-overlay.component';

describe('ManageBankAccountOverlayComponent', () => {
  const submittingSignal = signal(false);
  const errorSignal = signal('');

  const bankAccountsFacade = {
    isSubmitting: submittingSignal,
    errorMessage: errorSignal,
    addBankAccount: vi.fn(),
    updateBankAccount: vi.fn(),
    clearError: vi.fn(),
    clearMutationStatus: vi.fn(),
  };

  beforeEach(() => {
    bankAccountsFacade.addBankAccount.mockReset();
    bankAccountsFacade.updateBankAccount.mockReset();
    bankAccountsFacade.clearError.mockReset();
    bankAccountsFacade.clearMutationStatus.mockReset();
    submittingSignal.set(false);
    errorSignal.set('');

    TestBed.configureTestingModule({
      imports: [ManageBankAccountOverlayComponent, ReactiveFormsModule, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: BankAccountsFacade, useValue: bankAccountsFacade },
        provideNoopAnimations(),
      ],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('initializes form with empty values for adding', () => {
    const fixture = TestBed.createComponent(ManageBankAccountOverlayComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.form.value).toEqual({
      bankName: '',
      accountNumber: '',
      accountType: '',
      ifscCode: '',
      accountHolderName: '',
    });
  });

  it('initializes form with bank account data for editing', () => {
    const fixture = TestBed.createComponent(ManageBankAccountOverlayComponent);
    const component = fixture.componentInstance;
    component.bankAccount = {
      id: 'b1',
      bankName: 'SBI',
      accountNumber: '111',
      accountType: 'Savings',
      ifscCode: 'SBIN0001234',
      accountHolderName: 'Alice',
    };
    component.ngOnInit();
    fixture.detectChanges();

    expect(component.form.value).toEqual({
      bankName: 'SBI',
      accountNumber: '111',
      accountType: 'Savings',
      ifscCode: 'SBIN0001234',
      accountHolderName: 'Alice',
    });
  });

  it('calls addBankAccount on valid form submission (add mode)', () => {
    const fixture = TestBed.createComponent(ManageBankAccountOverlayComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    component.form.patchValue({
      bankName: 'SBI',
      accountNumber: '123',
      accountType: 'Savings',
    });

    component.onSubmit();

    expect(bankAccountsFacade.addBankAccount).toHaveBeenCalledWith({
      bankName: 'SBI',
      accountNumber: '123',
      accountType: 'Savings',
      ifscCode: null,
      accountHolderName: null,
    });
  });

  it('calls updateBankAccount on valid form submission (edit mode)', () => {
    const fixture = TestBed.createComponent(ManageBankAccountOverlayComponent);
    const component = fixture.componentInstance;
    component.bankAccount = {
      id: 'b1',
      bankName: 'Old',
      accountNumber: '000',
      accountType: 'Current',
      ifscCode: null,
      accountHolderName: null,
    };
    component.ngOnInit();
    fixture.detectChanges();

    component.form.patchValue({
      bankName: 'New Bank',
    });

    component.onSubmit();

    expect(bankAccountsFacade.updateBankAccount).toHaveBeenCalledWith('b1', {
      bankName: 'New Bank',
      accountNumber: '000',
      accountType: 'Current',
      ifscCode: null,
      accountHolderName: null,
    });
  });

  it('does not submit if form is invalid', () => {
    const fixture = TestBed.createComponent(ManageBankAccountOverlayComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    component.onSubmit();

    expect(bankAccountsFacade.addBankAccount).not.toHaveBeenCalled();
    expect(component.form.touched).toBe(true);
  });

  it('marks ifscCode as invalid if format is wrong', () => {
    const fixture = TestBed.createComponent(ManageBankAccountOverlayComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    component.form.patchValue({ ifscCode: 'INVALID123' });
    expect(component.form.controls.ifscCode.invalid).toBe(true);
  });

  it('disables submit button while submitting', () => {
    const fixture = TestBed.createComponent(ManageBankAccountOverlayComponent);
    const component = fixture.componentInstance;
    submittingSignal.set(true);
    fixture.detectChanges();

    const submitBtn = fixture.nativeElement.querySelector('button[type="submit"]');
    expect(submitBtn.disabled).toBe(true);
  });

  it('displays server error message', () => {
    const fixture = TestBed.createComponent(ManageBankAccountOverlayComponent);
    const component = fixture.componentInstance;
    errorSignal.set('Error saving account');
    fixture.detectChanges();

    const errorMsg = fixture.nativeElement.querySelector('.error-message');
    expect(errorMsg).toBeTruthy();
  });
});
