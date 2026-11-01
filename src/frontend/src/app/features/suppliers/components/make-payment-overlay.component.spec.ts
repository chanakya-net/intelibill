import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { FinancialOperationIntentService } from '../../../core/services/financial-operation-intent.service';
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

const paymentIntents = {
  revision: signal(0),
  pending: vi.fn((): unknown => null),
  check: vi.fn(),
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
      providers: [
        { provide: SuppliersFacade, useValue: suppliersFacade },
        { provide: AuthService, useValue: { session: () => ({ user: { id: 'user-1' }, activeShopId: 'shop-1' }) } },
        { provide: FinancialOperationIntentService, useValue: paymentIntents },
      ],
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
    paymentIntents.pending.mockReturnValue(null);
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

  it('shows the unconfirmed payment instead of the form and retries it unchanged', () => {
    paymentIntents.pending.mockReturnValue({
      operationKey: 'supplier-payment-1',
      payload: { amount: 500, paymentDate: '2026-11-01', notes: null },
      createdAt: '2026-11-01T10:00:00.000Z',
      status: 'unknown',
    });
    const retryPayment = vi.fn();
    Object.assign(suppliersFacade, { retryPayment });

    const { component, fixture } = setup();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('app-pending-financial-operation')).not.toBeNull();
    expect(element.querySelector('app-supplier-payment-form')).toBeNull();

    component.onRetryPendingPayment();
    expect(retryPayment).toHaveBeenCalledWith('s1');
    expect(suppliersFacade.makePayment).not.toHaveBeenCalled();
  });
});
