import { signal, Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { vi } from 'vitest';

import { CreateShopOverlayComponent } from './create-shop-overlay.component';
import { ShopsActions } from '../state/shops.actions';
import {
  selectShopsErrorMessage,
  selectShopsLastMutationSucceeded,
  selectShopsLastMutationType,
  selectShopsSubmitting,
} from '../state/shops.selectors';

describe('CreateShopOverlayComponent', () => {
  const dispatch = vi.fn();
  const isSubmittingSignal = signal(false);
  const errorSignal = signal('');
  const lastMutationTypeSignal = signal<'create' | 'update' | 'set-default' | null>(null);
  const lastMutationSucceededSignal = signal(false);

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

  function setup(): { component: CreateShopOverlayComponent; fixture: ReturnType<typeof TestBed.createComponent<CreateShopOverlayComponent>> } {
    TestBed.configureTestingModule({
      imports: [CreateShopOverlayComponent],
      providers: [{ provide: Store, useValue: store }],
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
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('does not submit when required fields are missing', () => {
    const { component } = setup();

    component.form.controls.name.setValue('');
    component.form.controls.address.setValue('');
    component.form.controls.city.setValue('');
    component.form.controls.state.setValue('');
    component.form.controls.pincode.setValue('');

    component.onSubmit();

    expect(component.form.touched).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: ShopsActions.createShopRequested.type })
    );
  });

  it('dispatches create action with trimmed values and blank optionals omitted', () => {
    const { component } = setup();

    component.form.controls.name.setValue('  Main Shop  ');
    component.form.controls.address.setValue('  42 MG Road  ');
    component.form.controls.city.setValue('  Bengaluru  ');
    component.form.controls.state.setValue('  Karnataka  ');
    component.form.controls.pincode.setValue('  560001  ');
    component.form.controls.contactPerson.setValue('   ');
    component.form.controls.mobileNumber.setValue('  ');
    component.form.controls.gstNumber.setValue('');

    component.onSubmit();

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

    component.form.controls.name.setValue('Main Shop');
    component.form.controls.address.setValue('42 MG Road');
    component.form.controls.city.setValue('Bengaluru');
    component.form.controls.state.setValue('Karnataka');
    component.form.controls.pincode.setValue('560001');
    component.form.controls.gstNumber.setValue('123');

    component.onSubmit();

    expect(component.form.controls.gstNumber.invalid).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: ShopsActions.createShopRequested.type })
    );
  });

  it('submits when gstNumber is empty', () => {
    const { component } = setup();

    component.form.controls.name.setValue('Main Shop');
    component.form.controls.address.setValue('42 MG Road');
    component.form.controls.city.setValue('Bengaluru');
    component.form.controls.state.setValue('Karnataka');
    component.form.controls.pincode.setValue('560001');
    component.form.controls.gstNumber.setValue('');

    component.onSubmit();

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

  it('emits close after create mutation succeeds', () => {
    const { component, fixture } = setup();
    const closeSpy = vi.fn();

    component.closeRequested.subscribe(closeSpy);

    component.form.controls.name.setValue('Main Shop');
    component.form.controls.address.setValue('42 MG Road');
    component.form.controls.city.setValue('Bengaluru');
    component.form.controls.state.setValue('Karnataka');
    component.form.controls.pincode.setValue('560001');

    component.onSubmit();

    lastMutationTypeSignal.set('create');
    lastMutationSucceededSignal.set(true);
    fixture.detectChanges();

    expect(closeSpy).toHaveBeenCalledTimes(1);
  });

  it('emits closeRequested when close is clicked', () => {
    const { component } = setup();
    const closeSpy = vi.fn();

    component.closeRequested.subscribe(closeSpy);
    component.onClose();

    expect(closeSpy).toHaveBeenCalledTimes(1);
  });
});
