import { signal, Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { vi } from 'vitest';

import { AddSupplierOverlayComponent } from './add-supplier-overlay.component';
import { SuppliersActions } from '../state/suppliers.actions';
import {
  selectSuppliersErrorMessage,
  selectSuppliersLastMutationSucceeded,
  selectSuppliersLastMutationType,
  selectSuppliersSubmitting,
} from '../state/suppliers.selectors';

describe('AddSupplierOverlayComponent', () => {
  const dispatch = vi.fn();
  const isSubmittingSignal = signal(false);
  const errorSignal = signal('');
  const lastMutationTypeSignal = signal<'add-supplier' | 'edit-supplier' | null>(null);
  const lastMutationSucceededSignal = signal(false);

  const store = {
    dispatch,
    selectSignal: vi.fn((selector: unknown): Signal<unknown> => {
      if (selector === selectSuppliersSubmitting) {
        return isSubmittingSignal;
      }

      if (selector === selectSuppliersErrorMessage) {
        return errorSignal;
      }

      if (selector === selectSuppliersLastMutationType) {
        return lastMutationTypeSignal;
      }

      if (selector === selectSuppliersLastMutationSucceeded) {
        return lastMutationSucceededSignal;
      }

      return signal(undefined);
    }),
  };

  function setup(): AddSupplierOverlayComponent {
    TestBed.configureTestingModule({
      imports: [AddSupplierOverlayComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [{ provide: Store, useValue: store }],
    });

    const fixture = TestBed.createComponent(AddSupplierOverlayComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    dispatch.mockReset();
    store.selectSignal.mockClear();
    isSubmittingSignal.set(false);
    errorSignal.set('');
    lastMutationTypeSignal.set(null);
    lastMutationSucceededSignal.set(false);
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('does not submit when contact phone is invalid', () => {
    const component = setup();

    component.form.controls.name.setValue('Supplier');
    component.form.controls.contactPersonPhone.setValue('123');
    component.form.controls.address.setValue('Address');
    component.form.controls.city.setValue('City');
    component.form.controls.state.setValue('State');
    component.form.controls.pin.setValue('560001');

    component.onSubmit();

    expect(component.form.controls.contactPersonPhone.invalid).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: SuppliersActions.addSupplierRequested.type })
    );
  });

  it('dispatches add supplier request with trimmed payload', () => {
    const component = setup();

    component.form.controls.name.setValue('  Fresh Foods  ');
    component.form.controls.contactPersonName.setValue('  Ramesh  ');
    component.form.controls.contactPersonPhone.setValue('+919999999999');
    component.form.controls.address.setValue(' 42 MG Road ');
    component.form.controls.city.setValue(' Bengaluru ');
    component.form.controls.state.setValue(' Karnataka ');
    component.form.controls.pin.setValue(' 560001 ');
    component.form.controls.isActive.setValue(true);
    component.form.controls.isPreferred.setValue(true);

    component.onSubmit();

    expect(dispatch).toHaveBeenCalledWith(SuppliersActions.clearError());
    expect(dispatch).toHaveBeenCalledWith(SuppliersActions.clearMutationStatus());
    expect(dispatch).toHaveBeenCalledWith(
      SuppliersActions.addSupplierRequested({
        payload: {
          name: 'Fresh Foods',
          contactPersonName: 'Ramesh',
          contactPersonPhone: '+919999999999',
          address: '42 MG Road',
          city: 'Bengaluru',
          state: 'Karnataka',
          pin: '560001',
          isActive: true,
          isPreferred: true,
        },
      })
    );
  });
});