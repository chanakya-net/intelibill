import { disabledTillProviders } from '../../../../../tests/tills/disabled-till.providers';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, vi } from 'vitest';

import type { AppliedCreditNote } from '../../services/sale.models';
import { PaymentMethodOption, SalePaymentSectionComponent } from './sale-payment-section.component';

const methods: PaymentMethodOption[] = [
  { value: 1, label: 'Cash' },
  { value: 2, label: 'UPI' },
  { value: 3, label: 'Card' },
  { value: 4, label: 'Credit' },
];

const notes: AppliedCreditNote[] = [
  {
    creditNoteId: 'cn-1',
    code: 'CN-001',
    availableBalance: 100,
    expiresAt: null,
    status: 'Active',
    amount: 40,
  },
  {
    creditNoteId: 'cn-2',
    code: 'CN-002',
    availableBalance: 80,
    expiresAt: null,
    status: 'Active',
    amount: 20,
  },
];

describe('SalePaymentSectionComponent', () => {
  it('payment_controls_have_associated_names', async () => {
    TestBed.configureTestingModule({
      providers: disabledTillProviders(),
      imports: [SalePaymentSectionComponent, TranslocoTestingModule.forRoot({
        langs: { en: { sales: { newSale: {
          paymentMethod: 'Payment method', paidAmount: 'Paid amount', dueAmount: 'Due amount',
          creditNote: { codeLabel: 'Credit note code', amountLabel: 'Credit note {{code}} amount' },
        } } } }, preloadLangs: true, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
      })],
    });

    const first = TestBed.createComponent(SalePaymentSectionComponent);
    for (const fixture of [first]) {
      fixture.componentInstance.paymentMethods = methods;
      fixture.componentInstance.showDueAmount = true;
      fixture.componentInstance.paymentSplitError = 'sales.newSale.invalidSplit';
      fixture.componentInstance.creditNoteError = 'sales.newSale.invalidNote';
      fixture.componentInstance.appliedCreditNotes = notes;
      fixture.detectChanges();
    }
    const second = TestBed.createComponent(SalePaymentSectionComponent);
    second.componentInstance.paymentMethods = methods;
    second.componentInstance.showDueAmount = true;
    second.componentInstance.paymentSplitError = 'sales.newSale.invalidSplit';
    second.componentInstance.creditNoteError = 'sales.newSale.invalidNote';
    second.componentInstance.appliedCreditNotes = notes;
    second.detectChanges();
    await Promise.all([first.whenStable(), second.whenStable()]);

    first.detectChanges();
    second.detectChanges();
    const controls = [first, second].map(fixture => {
      const root = fixture.nativeElement as HTMLElement;
      const inputs = [...root.querySelectorAll<HTMLInputElement>('input.p-inputnumber-input')];
      const method = root.querySelector<HTMLElement>('[role="combobox"]');
      expect(method).toBeTruthy();
      expect(method?.id).toBeTruthy();
      expect(method?.getAttribute('aria-labelledby')).toBeTruthy();
      expect(root.querySelector(`#${method?.getAttribute('aria-labelledby')}`)).toBeTruthy();
      expect(root.querySelector<HTMLLabelElement>(`label[for="${method?.id}"]`)?.textContent).toContain('Payment method');
      expect(inputs.length).toBe(4);
      for (const input of inputs) {
        expect(input.id).toBeTruthy();
        expect(root.querySelector<HTMLLabelElement>(`label[for="${input.id}"]`)).toBeTruthy();
      }
      expect(inputs[0].getAttribute('aria-describedby')).toBeTruthy();
      expect(root.querySelector(`#${inputs[0].getAttribute('aria-describedby')}`)?.textContent).toContain('invalidSplit');
      expect(inputs[0].getAttribute('aria-invalid')).toBe('true');
      expect(inputs[1].getAttribute('aria-invalid')).toBe('true');
      return inputs.map(input => input.id);
    });
    expect(new Set(controls.flat()).size).toBe(8);
    second.componentRef.setInput('isSubmitting', true);
    second.detectChanges();
    expect((second.nativeElement.querySelector('input.p-inputnumber-input') as HTMLInputElement).disabled).toBe(true);
  });

  it('emits payment method updates', () => {
    TestBed.configureTestingModule({
      providers: disabledTillProviders(),
      imports: [
        SalePaymentSectionComponent,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
      ],
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
      providers: disabledTillProviders(),
      imports: [
        SalePaymentSectionComponent,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
      ],
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

  it('forwards applied credit note amount changes', () => {
    TestBed.configureTestingModule({
      providers: disabledTillProviders(),
      imports: [
        SalePaymentSectionComponent,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(SalePaymentSectionComponent);
    const component = fixture.componentInstance;
    component.appliedCreditNotes = [
      {
        creditNoteId: 'cn-1',
        code: 'CN-001',
        availableBalance: 120,
        status: 'Active',
        expiresAt: null,
        amount: 40,
      },
    ];

    const spy = vi.fn();
    component.appliedCreditNoteAmountChanged.subscribe(spy);

    component.onAppliedCreditNoteAmountChange('cn-1', 44);

    expect(spy).toHaveBeenCalledWith({ creditNoteId: 'cn-1', amount: 44 });
  });

  it('forwards applied credit note remove action', () => {
    TestBed.configureTestingModule({
      providers: disabledTillProviders(),
      imports: [
        SalePaymentSectionComponent,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(SalePaymentSectionComponent);
    const component = fixture.componentInstance;

    const spy = vi.fn();
    component.appliedCreditNoteRemoved.subscribe(spy);

    component.onAppliedCreditNoteRemoveClick('cn-1');

    expect(spy).toHaveBeenCalledWith('cn-1');
  });

  it('hides the due amount input until a customer can use credit', () => {
    TestBed.configureTestingModule({
      providers: disabledTillProviders(),
      imports: [
        SalePaymentSectionComponent,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(SalePaymentSectionComponent);
    const component = fixture.componentInstance;
    component.paymentMethods = methods;
    fixture.detectChanges();

    const inputGroups = fixture.nativeElement.querySelectorAll('.p-inputgroup');

    expect(inputGroups.length).toBe(1);
    expect(fixture.nativeElement.textContent as string).not.toContain('sales.newSale.dueAmount');
  });

  it('disables due amount input when requested', () => {
    TestBed.configureTestingModule({
      providers: disabledTillProviders(),
      imports: [
        SalePaymentSectionComponent,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
      ],
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

  it('renders applied notes and emits add, amount change, and remove actions', () => {
    TestBed.configureTestingModule({
      providers: disabledTillProviders(),
      imports: [
        SalePaymentSectionComponent,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(SalePaymentSectionComponent);
    const component = fixture.componentInstance;
    component.appliedCreditNotes = notes;
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('CN-001');
    expect(fixture.nativeElement.textContent).toContain('CN-002');

    const applySpy = vi.fn();
    const amountSpy = vi.fn();
    const removeSpy = vi.fn();
    component.creditNoteApplyRequested.subscribe(applySpy);
    component.appliedCreditNoteAmountChanged.subscribe(amountSpy);
    component.appliedCreditNoteRemoved.subscribe(removeSpy);

    component.onCreditNoteApplyClick();
    component.onAppliedCreditNoteAmountChange('cn-1', 55);
    component.onAppliedCreditNoteRemoveClick('cn-2');

    expect(applySpy).toHaveBeenCalled();
    expect(amountSpy).toHaveBeenCalledWith({ creditNoteId: 'cn-1', amount: 55 });
    expect(removeSpy).toHaveBeenCalledWith('cn-2');
  });
});
