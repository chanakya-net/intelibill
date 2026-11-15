import { signal, Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { vi } from 'vitest';

import { CreateShopOverlayComponent } from './create-shop-overlay.component';
import { ShopsActions } from '../state/shops.actions';
import {
  selectShopsErrorMessage,
  selectShopsSubmitting,
} from '../state/shops.selectors';

describe('CreateShopOverlayComponent', () => {
  const dispatch = vi.fn();
  const isSubmittingSignal = signal(false);
  const errorSignal = signal('');
  const store = {
    dispatch,
    selectSignal: vi.fn((selector: unknown): Signal<unknown> => {
      if (selector === selectShopsSubmitting) {
        return isSubmittingSignal;
      }

      if (selector === selectShopsErrorMessage) {
        return errorSignal;
      }

      return signal(undefined);
    }),
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
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('does not submit when required fields are missing', () => {
    const { component } = setup();

    component.shopForm.controls.name.setValue('');
    component.shopForm.controls.address.setValue('');
    component.shopForm.controls.city.setValue('');
    component.shopForm.controls.state.setValue('');
    component.shopForm.controls.pincode.setValue('');

    component.submit();

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

    component.submit();

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

    component.submit();

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

    component.submit();

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
    component.submit();
    expect(component.shopForm.hasError('eInvoiceStatusRequired')).toBe(true);
    expect(createDispatched()).toBe(false);

    component.shopForm.patchValue({ taxRegime: 'Composition' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#create-shop-einvoice')).toBeNull();
    component.submit();
    expect(createDispatched()).toBe(true);
  });

  it('refuses a GSTIN with a wrong check digit or from another state', () => {
    const { component } = setup();
    fillShopDetails(component);
    component.shopForm.patchValue({ stateCode: '27', taxRegime: 'Composition', gstNumber: '27AAPFU0939F1ZW' });

    component.submit();
    expect(component.shopForm.controls.gstNumber.hasError('gstinChecksum')).toBe(true);

    component.shopForm.controls.gstNumber.setValue('29AAGCB7383J1Z4');
    component.submit();
    expect(component.shopForm.hasError('gstinStateMismatch')).toBe(true);

    component.shopForm.controls.gstNumber.setValue('');
    component.submit();
    expect(component.shopForm.hasError('gstinRequired')).toBe(true);
    expect(createDispatched()).toBe(false);
  });

  it('refuses a GSTIN for a shop not registered under GST', () => {
    const { component } = setup();
    fillShopDetails(component);
    component.shopForm.patchValue({ stateCode: '27', taxRegime: 'Unregistered', gstNumber: '27AAPFU0939F1ZV' });

    component.submit();

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

    component.submit();

    expect(component.shopForm.controls.gstNumber.invalid).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: ShopsActions.createShopRequested.type }),
    );
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
