import { disabledTillProviders } from '../../../../../tests/tills/disabled-till.providers';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, describe, expect, it } from 'vitest';

import { SupplierPaymentFormComponent } from './supplier-payment-form.component';

describe('SupplierPaymentFormComponent', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('does not emit when amount is null', () => {
    TestBed.configureTestingModule({
      providers: disabledTillProviders(),
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
      providers: disabledTillProviders(),
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

  it('sends null notes when notes is blank', () => {
    TestBed.configureTestingModule({
      providers: disabledTillProviders(),
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
