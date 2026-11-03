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
  const lastMutationTypeSignal = signal<
    'create' | 'update' | 'update-bank-details' | 'update-tax-profile' | 'set-default' | null
  >(null);
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

  function setup(): {
    component: CreateShopOverlayComponent;
    fixture: ReturnType<typeof TestBed.createComponent<CreateShopOverlayComponent>>;
  } {
    TestBed.configureTestingModule({
      imports: [
        CreateShopOverlayComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
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
      expect.objectContaining({ type: ShopsActions.createShopRequested.type }),
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
    component.shopForm.controls.stateCode.setValue('29');
    component.shopForm.controls.taxRegime.setValue('Unregistered');

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
          taxProfile: {
            legalName: null,
            stateCode: '29',
            taxRegime: 'Unregistered',
            invoiceSeriesCode: 'A',
            eInvoiceStatus: null,
          },
        },
      }),
    );
  });

  // --- Step 1: GST registration ---

  function fillShopDetails(component: CreateShopOverlayComponent): void {
    component.shopForm.controls.name.setValue('Main Shop');
    component.shopForm.controls.address.setValue('42 MG Road');
    component.shopForm.controls.city.setValue('Pune');
    component.shopForm.controls.state.setValue('Maharashtra');
    component.shopForm.controls.pincode.setValue('411001');
  }

  function createDispatched(): boolean {
    return dispatch.mock.calls.some(([action]) => action.type === ShopsActions.createShopRequested.type);
  }

  it('does not create a shop until the GST state and registration type are chosen', () => {
    const { component } = setup();
    fillShopDetails(component);

    component.onNextFromStep1();

    expect(component.shopForm.controls.stateCode.hasError('required')).toBe(true);
    expect(component.shopForm.controls.taxRegime.hasError('required')).toBe(true);
    expect(createDispatched()).toBe(false);
  });

  it('sends a regular registration with its e-invoicing answer and the GSTIN at the top level', () => {
    const { component } = setup();
    fillShopDetails(component);
    component.shopForm.patchValue({
      legalName: '  Main Traders LLP  ',
      stateCode: '27',
      taxRegime: 'Regular',
      gstNumber: ' 27aapfu0939f1zv ',
      eInvoiceStatus: 'BelowThreshold',
      invoiceSeriesCode: 'b',
    });

    component.onNextFromStep1();

    expect(dispatch).toHaveBeenCalledWith(
      ShopsActions.createShopRequested({
        payload: {
          name: 'Main Shop',
          address: '42 MG Road',
          city: 'Pune',
          state: 'Maharashtra',
          pincode: '411001',
          contactPerson: undefined,
          mobileNumber: undefined,
          gstNumber: '27AAPFU0939F1ZV',
          taxProfile: {
            legalName: 'Main Traders LLP',
            stateCode: '27',
            taxRegime: 'Regular',
            invoiceSeriesCode: 'B',
            eInvoiceStatus: 'BelowThreshold',
          },
        },
      }),
    );
  });

  it('asks whether e-invoicing applies only for a regular registration', () => {
    const { component, fixture } = setup();
    fillShopDetails(component);
    component.shopForm.patchValue({ stateCode: '27', taxRegime: 'Regular', gstNumber: '27AAPFU0939F1ZV' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#create-shop-einvoice')).not.toBeNull();
    component.onNextFromStep1();
    expect(component.shopForm.hasError('eInvoiceStatusRequired')).toBe(true);
    expect(createDispatched()).toBe(false);

    component.shopForm.patchValue({ taxRegime: 'Composition' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#create-shop-einvoice')).toBeNull();
    component.onNextFromStep1();
    expect(createDispatched()).toBe(true);
  });

  it('refuses a GSTIN with a wrong check digit or from another state', () => {
    const { component } = setup();
    fillShopDetails(component);
    component.shopForm.patchValue({ stateCode: '27', taxRegime: 'Composition', gstNumber: '27AAPFU0939F1ZW' });

    component.onNextFromStep1();
    expect(component.shopForm.controls.gstNumber.hasError('gstinChecksum')).toBe(true);

    component.shopForm.controls.gstNumber.setValue('29AAGCB7383J1Z4');
    component.onNextFromStep1();
    expect(component.shopForm.hasError('gstinStateMismatch')).toBe(true);

    component.shopForm.controls.gstNumber.setValue('');
    component.onNextFromStep1();
    expect(component.shopForm.hasError('gstinRequired')).toBe(true);
    expect(createDispatched()).toBe(false);
  });

  it('refuses a GSTIN for a shop not registered under GST', () => {
    const { component } = setup();
    fillShopDetails(component);
    component.shopForm.patchValue({ stateCode: '27', taxRegime: 'Unregistered', gstNumber: '27AAPFU0939F1ZV' });

    component.onNextFromStep1();

    expect(component.shopForm.hasError('gstinNotAllowed')).toBe(true);
    expect(createDispatched()).toBe(false);
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
      expect.objectContaining({ type: ShopsActions.createShopRequested.type }),
    );
  });

  it('advances to step 2 after create mutation succeeds', () => {
    const { component, fixture } = setup();

    component.shopForm.controls.name.setValue('Main Shop');
    component.shopForm.controls.address.setValue('42 MG Road');
    component.shopForm.controls.city.setValue('Bengaluru');
    component.shopForm.controls.state.setValue('Karnataka');
    component.shopForm.controls.pincode.setValue('560001');
    component.shopForm.controls.stateCode.setValue('29');
    component.shopForm.controls.taxRegime.setValue('Unregistered');

    component.onNextFromStep1();
    expect(component.isCreatePending()).toBe(true);

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
      }),
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
      expect.objectContaining({ type: ShopsActions.updateShopBankDetailsRequested.type }),
    );
  });

  it('does not submit bank details when ifsc code is invalid', () => {
    const { component } = setup();
    sessionSignal.set({ activeShopId: 'shop-1' });

    component.bankForm.controls.ifscCode.setValue('INVALID');

    component.onSaveFromStep2();

    expect(component.bankForm.controls.ifscCode.invalid).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: ShopsActions.updateShopBankDetailsRequested.type }),
    );
  });

  it('skips bank details and advances to step 3 on skip', () => {
    const { component } = setup();

    component.onSkipStep2();

    expect(component.activeStep()).toBe(3);
    expect(dispatch).toHaveBeenCalledWith(ShopsActions.clearMutationStatus());
  });

  it('moves one step back when previous is triggered', () => {
    const { component } = setup();

    component.activeStep.set(3);
    component.onPreviousStep();

    expect(component.activeStep()).toBe(2);
  });

  it('moves to clicked previous icon step only', () => {
    const { component } = setup();

    component.activeStep.set(3);
    component.onStepIconClick(2);
    expect(component.activeStep()).toBe(2);

    component.onStepIconClick(3);
    expect(component.activeStep()).toBe(2);
  });

  it('does not move back while submitting', () => {
    const { component } = setup();

    component.activeStep.set(2);
    isSubmittingSignal.set(true);

    component.onPreviousStep();
    component.onStepIconClick(1);

    expect(component.activeStep()).toBe(2);
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

  it('rejects whitespace-only required shop fields', () => {
    const { component } = setup();
    component.shopForm.patchValue({
      name: '   ',
      address: '   ',
      city: '   ',
      state: '   ',
      pincode: '   ',
    });

    expect(component.shopForm.invalid).toBe(true);
    expect(component.shopForm.controls.name.hasError('required')).toBe(true);
  });
});
