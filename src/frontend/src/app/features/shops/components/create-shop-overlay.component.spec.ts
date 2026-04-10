import { signal, Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { vi } from 'vitest';

import { CreateShopOverlayComponent } from './create-shop-overlay.component';
import { ShopsActions } from '../state/shops.actions';
import {
  selectShopsErrorMessage,
  selectShopsLastMutationSucceeded,
  selectShopsLastMutationType,
  selectShopsSubmitting,
} from '../state/shops.selectors';
import { AuthService } from '../../../core/auth/auth.service';

describe('CreateShopOverlayComponent', () => {
  const dispatch = vi.fn();
  const isSubmittingSignal = signal(false);
  const errorSignal = signal('');
  const lastMutationTypeSignal = signal<'create' | 'update' | 'update-bank-details' | 'set-default' | null>(null);
  const lastMutationSucceededSignal = signal(false);
  const sessionSignal = signal<{ activeShopId: string } | null>(null);

  const store = {
    dispatch,
    selectSignal: vi.fn((selector: unknown): Signal<unknown> => {
      if (selector === selectShopsSubmitting) {
        return isSubmittingSignal;
      }

      if (selector === selectShopsErrorMessage) {
        return errorSignal;
      }

      if (selector === selectShopsLastMutationType) {
        return lastMutationTypeSignal;
      }

      if (selector === selectShopsLastMutationSucceeded) {
        return lastMutationSucceededSignal;
      }

      return signal(undefined);
    }),
  };

  const authService = {
    session: sessionSignal,
  };

  function setup(): { component: CreateShopOverlayComponent; fixture: ReturnType<typeof TestBed.createComponent<CreateShopOverlayComponent>> } {
    TestBed.configureTestingModule({
      imports: [CreateShopOverlayComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: Store, useValue: store },
        { provide: AuthService, useValue: authService },
      ],
    });

    const fixture = TestBed.createComponent(CreateShopOverlayComponent);
    fixture.detectChanges();
    return { component: fixture.componentInstance, fixture };
  }

  beforeEach(() => {
    dispatch.mockReset();
    store.selectSignal.mockClear();
    isSubmittingSignal.set(false);
    errorSignal.set('');
    lastMutationTypeSignal.set(null);
    lastMutationSucceededSignal.set(false);
    sessionSignal.set(null);
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  // --- Step 1: Shop Info ---

  it('does not submit when required fields are missing', () => {
    const { component } = setup();

    component.shopForm.controls.name.setValue('');
    component.shopForm.controls.address.setValue('');
    component.shopForm.controls.city.setValue('');
    component.shopForm.controls.state.setValue('');
    component.shopForm.controls.pincode.setValue('');

    component.onNextFromStep1();

    expect(component.shopForm.touched).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: ShopsActions.createShopRequested.type })
    );
  });

  it('dispatches create action with trimmed values and blank optionals omitted', () => {
    const { component } = setup();

    component.shopForm.controls.name.setValue('  Main Shop  ');
    component.shopForm.controls.address.setValue('  42 MG Road  ');
    component.shopForm.controls.city.setValue('  Bengaluru  ');
    component.shopForm.controls.state.setValue('  Karnataka  ');
    component.shopForm.controls.pincode.setValue('  560001  ');
    component.shopForm.controls.contactPerson.setValue('   ');
    component.shopForm.controls.mobileNumber.setValue('  ');
    component.shopForm.controls.gstNumber.setValue('');

    component.onNextFromStep1();

    expect(dispatch).toHaveBeenCalledWith(ShopsActions.clearError());
    expect(dispatch).toHaveBeenCalledWith(ShopsActions.clearMutationStatus());
    expect(dispatch).toHaveBeenCalledWith(
      ShopsActions.createShopRequested({
        payload: {
          name: 'Main Shop',
          address: '42 MG Road',
          city: 'Bengaluru',
          state: 'Karnataka',
          pincode: '560001',
          contactPerson: undefined,
          mobileNumber: undefined,
          gstNumber: undefined,
        },
      })
    );
  });

  it('does not submit when gstNumber is present but invalid', () => {
    const { component } = setup();

    component.shopForm.controls.name.setValue('Main Shop');
    component.shopForm.controls.address.setValue('42 MG Road');
    component.shopForm.controls.city.setValue('Bengaluru');
    component.shopForm.controls.state.setValue('Karnataka');
    component.shopForm.controls.pincode.setValue('560001');
    component.shopForm.controls.gstNumber.setValue('123');

    component.onNextFromStep1();

    expect(component.shopForm.controls.gstNumber.invalid).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: ShopsActions.createShopRequested.type })
    );
  });

  it('advances to step 2 after create mutation succeeds', () => {
    const { component, fixture } = setup();

    component.shopForm.controls.name.setValue('Main Shop');
    component.shopForm.controls.address.setValue('42 MG Road');
    component.shopForm.controls.city.setValue('Bengaluru');
    component.shopForm.controls.state.setValue('Karnataka');
    component.shopForm.controls.pincode.setValue('560001');

    component.onNextFromStep1();

    lastMutationTypeSignal.set('create');
    lastMutationSucceededSignal.set(true);
    fixture.detectChanges();

    expect(component.activeStep()).toBe(2);
  });

  // --- Step 2: Bank Details ---

  it('dispatches bank details action with correct payload', () => {
    const { component } = setup();
    sessionSignal.set({ activeShopId: 'shop-1' });

    component.bankForm.controls.bankName.setValue('SBI');
    component.bankForm.controls.accountNumber.setValue('123456789012');
    component.bankForm.controls.accountType.setValue('Savings');
    component.bankForm.controls.ifscCode.setValue('SBIN0001234');
    component.bankForm.controls.accountHolderName.setValue('Chandra Kumar');

    component.onSaveFromStep2();

    expect(dispatch).toHaveBeenCalledWith(
      ShopsActions.updateShopBankDetailsRequested({
        shopId: 'shop-1',
        payload: {
          bankName: 'SBI',
          accountNumber: '123456789012',
          accountType: 'Savings',
          ifscCode: 'SBIN0001234',
          accountHolderName: 'Chandra Kumar',
        },
      })
    );
  });

  it('advances to step 3 after bank details mutation succeeds', () => {
    const { component, fixture } = setup();
    sessionSignal.set({ activeShopId: 'shop-1' });

    component.bankForm.controls.bankName.setValue('SBI');
    component.bankForm.controls.accountNumber.setValue('123456789012');

    component.onSaveFromStep2();

    lastMutationTypeSignal.set('update-bank-details');
    lastMutationSucceededSignal.set(true);
    fixture.detectChanges();

    expect(component.activeStep()).toBe(3);
  });

  it('skips to step 3 when no active shop is set', () => {
    const { component } = setup();
    sessionSignal.set(null);

    component.onSaveFromStep2();

    expect(component.activeStep()).toBe(3);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: ShopsActions.updateShopBankDetailsRequested.type })
    );
  });

  it('does not submit bank details when ifsc code is invalid', () => {
    const { component } = setup();
    sessionSignal.set({ activeShopId: 'shop-1' });

    component.bankForm.controls.ifscCode.setValue('INVALID');

    component.onSaveFromStep2();

    expect(component.bankForm.controls.ifscCode.invalid).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: ShopsActions.updateShopBankDetailsRequested.type })
    );
  });

  it('skips bank details and advances to step 3 on skip', () => {
    const { component } = setup();

    component.onSkipStep2();

    expect(component.activeStep()).toBe(3);
    expect(dispatch).toHaveBeenCalledWith(ShopsActions.clearMutationStatus());
  });

  // --- Step 3: Done ---

  it('emits closeRequested when done is clicked', () => {
    const { component } = setup();
    const closeSpy = vi.fn();

    component.closeRequested.subscribe(closeSpy);
    component.onDone();

    expect(closeSpy).toHaveBeenCalledTimes(1);
  });

  it('emits closeRequested when close button is clicked', () => {
    const { component } = setup();
    const closeSpy = vi.fn();

    component.closeRequested.subscribe(closeSpy);
    component.onClose();

    expect(closeSpy).toHaveBeenCalledTimes(1);
  });
});
