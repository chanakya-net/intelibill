import { LocalizationService } from '../../../../core/i18n/localization.service';
import { signal } from '@angular/core';
import { PaymentSettlementPreviewService } from '../../../../core/services/payment-settlement-preview.service';
import { disabledTillProviders } from '../../../../../tests/tills/disabled-till.providers';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { SupplierPaymentFormComponent } from './supplier-payment-form.component';

describe('SupplierPaymentFormComponent', () => {
  const preview = signal({ status: 'ready' as const, limit: null as number | null });
  const providers = [...disabledTillProviders(), { provide: LocalizationService, useValue: { currentLanguage: signal('en-IN') } }, { provide: PaymentSettlementPreviewService, useValue: { watch: () => preview } }];
  afterEach(() => {
    preview.set({ status: 'ready', limit: null });
    TestBed.resetTestingModule();
  });

  it('does not emit when amount is null', () => {
    TestBed.configureTestingModule({
      providers,
      imports: [
        SupplierPaymentFormComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(SupplierPaymentFormComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    let emitted = false;
    component.paymentSubmitted.subscribe(() => {
      emitted = true;
    });

    component.form.controls.amount.setValue(null);
    component.onSubmit();

    expect(component.form.invalid).toBe(true);
    expect(emitted).toBe(false);
  });

  it('trims notes and emits payload', () => {
    TestBed.configureTestingModule({
      providers,
      imports: [
        SupplierPaymentFormComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(SupplierPaymentFormComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    const emitted: any[] = [];
    component.paymentSubmitted.subscribe((payload) => emitted.push(payload));

    component.form.controls.amount.setValue(500);
    component.form.controls.paymentDate.setValue(new Date(2026, 3, 7));
    component.form.controls.notes.setValue('  bank transfer  ');

    component.onSubmit();

    expect(emitted[0]).toEqual({
      tillOwner: { userId: 'user-1', shopId: 'shop-1' },
      amount: 500,
      paymentDate: '2026-04-07',
      notes: 'bank transfer',
    });
  });

  it('refuses amounts above the current supplier settlement limit', () => {
    TestBed.configureTestingModule({
      providers,
      imports: [SupplierPaymentFormComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
    });
    const fixture = TestBed.createComponent(SupplierPaymentFormComponent);
    const component = fixture.componentInstance;
    const submit = vi.fn();
    component.paymentSubmitted.subscribe(submit);
    preview.set({ status: 'ready', limit: 25.50 });
    fixture.detectChanges();

    component.form.controls.amount.setValue(25.51);
    component.onSubmit();
    expect(submit).not.toHaveBeenCalled();

    component.form.controls.amount.setValue(25.50);
    component.onSubmit();
    expect(submit).toHaveBeenCalledWith(expect.objectContaining({ amount: 25.50 }));
  });

  it('sends null notes when notes is blank', () => {
    TestBed.configureTestingModule({
      providers,
      imports: [
        SupplierPaymentFormComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(SupplierPaymentFormComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    const emitted: any[] = [];
    component.paymentSubmitted.subscribe((payload) => emitted.push(payload));

    component.form.controls.amount.setValue(100);
    component.form.controls.paymentDate.setValue(new Date(2026, 3, 7));
    component.form.controls.notes.setValue('   ');

    component.onSubmit();

    expect(emitted[0]).toEqual({
      tillOwner: { userId: 'user-1', shopId: 'shop-1' },
      amount: 100,
      paymentDate: '2026-04-07',
      notes: null,
    });
  });
});
