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
    const submitSpy = vi.fn();

    component.paidAmountChanged.subscribe(paidSpy);
    component.dueAmountChanged.subscribe(dueSpy);
    component.submitRequested.subscribe(submitSpy);

    component.onPaidAmountChange(55);
    component.onDueAmountChange(11);
    component.onSubmit();

    expect(paidSpy).toHaveBeenCalledWith(55);
    expect(dueSpy).toHaveBeenCalledWith(11);
    expect(submitSpy).toHaveBeenCalled();
  });

  it('disables due amount input when requested', () => {
    TestBed.configureTestingModule({
      imports: [SalePaymentSectionComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(SalePaymentSectionComponent);
    const component = fixture.componentInstance;
    component.dueAmountDisabled = true;
    component.canUseCredit = false;
    component.paymentMethods = methods;
    fixture.detectChanges();

    const inputGroups = fixture.nativeElement.querySelectorAll('.p-inputgroup');
    const dueInput = inputGroups[1].querySelector('input.p-inputnumber-input') as HTMLInputElement;

    expect(inputGroups.length).toBe(2);
    expect(dueInput.disabled).toBe(true);
  });
});
