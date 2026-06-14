import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, vi } from 'vitest';

import { PaymentMethodOption, SalePaymentSectionComponent } from './sale-payment-section.component';

const methods: PaymentMethodOption[] = [
  { value: 1, label: 'Cash' },
  { value: 2, label: 'UPI' },
  { value: 3, label: 'Card' },
  { value: 4, label: 'Credit' },
];

describe('SalePaymentSectionComponent', () => {
  it('emits payment method updates', () => {
    TestBed.configureTestingModule({
      imports: [SalePaymentSectionComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(SalePaymentSectionComponent);
    const component = fixture.componentInstance;
    component.paymentMethods = methods;

    const spy = vi.fn();
    component.methodChanged.subscribe(spy);

    component.onMethodChange('UPI');

    expect(spy).toHaveBeenCalledWith('UPI');
  });

  it('forwards paid and due amount changes', () => {
    TestBed.configureTestingModule({
      imports: [SalePaymentSectionComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(SalePaymentSectionComponent);
    const component = fixture.componentInstance;

    const paidSpy = vi.fn();
    const dueSpy = vi.fn();
    component.paidAmountChanged.subscribe(paidSpy);
    component.dueAmountChanged.subscribe(dueSpy);

    component.onPaidAmountChange(55);
    component.onDueAmountChange(11);

    expect(paidSpy).toHaveBeenCalledWith(55);
    expect(dueSpy).toHaveBeenCalledWith(11);
  });

  it('hides the due amount input until a customer can use credit', () => {
    TestBed.configureTestingModule({
      imports: [SalePaymentSectionComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(SalePaymentSectionComponent);
    const component = fixture.componentInstance;
    component.paymentMethods = methods;
    fixture.detectChanges();

    const inputGroups = fixture.nativeElement.querySelectorAll('.p-inputgroup');

    expect(inputGroups.length).toBe(1);
    expect((fixture.nativeElement.textContent as string)).not.toContain('sales.newSale.dueAmount');
  });

  it('disables due amount input when requested', () => {
    TestBed.configureTestingModule({
      imports: [SalePaymentSectionComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(SalePaymentSectionComponent);
    const component = fixture.componentInstance;
    component.showDueAmount = true;
    component.dueAmountDisabled = true;
    component.canUseCredit = false;
    component.paymentMethods = methods;
    fixture.detectChanges();

    const inputGroups = fixture.nativeElement.querySelectorAll('.p-inputgroup');
    const dueInput = inputGroups[1].querySelector('input.p-inputnumber-input') as HTMLInputElement;

    expect(inputGroups.length).toBe(2);
    expect(dueInput.disabled).toBe(true);
  });

  it('disables credit note controls in offline mode', () => {
    TestBed.configureTestingModule({
      imports: [SalePaymentSectionComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(SalePaymentSectionComponent);
    const component = fixture.componentInstance;
    component.isOfflineMode = true;
    component.creditNoteCode = 'CN-123';
    fixture.detectChanges();

    const input = fixture.nativeElement.querySelector('input[aria-label="Credit note code"]') as HTMLInputElement;
    const button = fixture.nativeElement.querySelector('p-button button') as HTMLButtonElement;

    expect(input.disabled).toBe(true);
    expect(button.disabled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('sales.newSale.creditNote.offlineDisabled');
  });

  it('shows mismatch warning and emits confirm or cancel actions', () => {
    TestBed.configureTestingModule({
      imports: [SalePaymentSectionComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(SalePaymentSectionComponent);
    const component = fixture.componentInstance;
    component.verifiedCreditNote = { creditNoteId: 'cn-1', code: 'CN-123', availableBalance: 10, expiresAt: null, status: 'Active', customerName: 'Other customer' };
    component.creditNoteCustomerMismatchWarning = true;
    fixture.detectChanges();

    const confirmSpy = vi.fn();
    const cancelSpy = vi.fn();
    component.creditNoteCustomerMismatchConfirmedChanged.subscribe(confirmSpy);
    component.creditNoteCustomerMismatchCancelled.subscribe(cancelSpy);

    expect(fixture.nativeElement.querySelector('.bg-amber-50')).toBeTruthy();

    component.onCreditNoteCustomerMismatchConfirmedChange(true);
    component.onCreditNoteCustomerMismatchCancelClick();

    expect(confirmSpy).toHaveBeenCalledWith(true);
    expect(cancelSpy).toHaveBeenCalled();
  });

  it('hides mismatch warning when mismatch has already been confirmed', () => {
    TestBed.configureTestingModule({
      imports: [SalePaymentSectionComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(SalePaymentSectionComponent);
    const component = fixture.componentInstance;
    component.verifiedCreditNote = { creditNoteId: 'cn-1', code: 'CN-123', availableBalance: 10, expiresAt: null, status: 'Active', customerName: 'Other customer' };
    component.creditNoteCustomerMismatchWarning = true;
    component.creditNoteCustomerMismatchConfirmed = true;
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('sales.newSale.creditNote.customerMismatchConfirmed');
    expect(fixture.nativeElement.querySelector('.bg-amber-50')).toBeNull();
  });
});
