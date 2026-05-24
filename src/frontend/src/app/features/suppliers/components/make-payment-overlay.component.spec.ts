import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { MakePaymentOverlayComponent } from './make-payment-overlay.component';
import { SuppliersFacade } from '../state/suppliers.facade';
import { Supplier } from '../services/supplier.service';

const mockSupplier: Supplier = {
  supplierId: 's1',
  name: 'Fresh Foods',
  contactPersonName: 'Ramesh',
  contactPersonPhone: '+919999999999',
  address: '42 MG Road',
  city: 'Bengaluru',
  state: 'Karnataka',
  pin: '560001',
  isSystem: false,
  isActive: true,
  isPreferred: false,
  balanceDue: 1500,
};

describe('MakePaymentOverlayComponent', () => {
  const isSubmittingSignal = signal(false);
  const errorSignal = signal('');

  const suppliersFacade = {
    isSubmitting: isSubmittingSignal,
    errorMessage: errorSignal,
    clearError: vi.fn(),
    clearMutationStatus: vi.fn(),
    makePayment: vi.fn(),
  };

  function setup(): { component: MakePaymentOverlayComponent; fixture: ReturnType<typeof TestBed.createComponent<MakePaymentOverlayComponent>> } {
    TestBed.configureTestingModule({
      imports: [
        MakePaymentOverlayComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [{ provide: SuppliersFacade, useValue: suppliersFacade }],
    });

    const fixture = TestBed.createComponent(MakePaymentOverlayComponent);
    fixture.componentRef.setInput('supplier', mockSupplier);
    fixture.detectChanges();
    return { component: fixture.componentInstance, fixture };
  }

  beforeEach(() => {
    suppliersFacade.clearError.mockReset();
    suppliersFacade.clearMutationStatus.mockReset();
    suppliersFacade.makePayment.mockReset();
    isSubmittingSignal.set(false);
    errorSignal.set('');
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('dispatches makePayment with correct payload', () => {
    const { component } = setup();
    component.onPaymentSubmitted({
      amount: 500,
      paymentDate: '2026-04-07',
      notes: 'bank transfer',
    });

    expect(suppliersFacade.clearError).toHaveBeenCalled();
    expect(suppliersFacade.clearMutationStatus).toHaveBeenCalled();
    expect(suppliersFacade.makePayment).toHaveBeenCalledWith('s1', {
      amount: 500,
      paymentDate: '2026-04-07',
      notes: 'bank transfer',
    });
  });

  it('does not emit closeRequested while submitting', () => {
    const { component } = setup();
    isSubmittingSignal.set(true);

    let emitted = false;
    component.closeRequested.subscribe(() => { emitted = true; });
    component.onClose();

    expect(emitted).toBe(false);
  });

  it('emits closeRequested when not submitting', () => {
    const { component } = setup();

    let emitted = false;
    component.closeRequested.subscribe(() => { emitted = true; });
    component.onClose();

    expect(emitted).toBe(true);
  });
});
