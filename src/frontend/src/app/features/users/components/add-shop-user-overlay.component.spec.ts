import { signal, Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { vi } from 'vitest';

import { AddShopUserOverlayComponent } from './add-shop-user-overlay.component';
import { UsersActions } from '../state/users.actions';
import {
  selectUsersErrorMessage,
  selectUsersLastMutationSucceeded,
  selectUsersLastMutationType,
  selectUsersSubmitting,
} from '../state/users.selectors';

describe('AddShopUserOverlayComponent', () => {
  const dispatch = vi.fn();
  const isSubmittingSignal = signal(false);
  const errorSignal = signal('');
  const lastMutationTypeSignal = signal<'update-profile' | 'change-password' | 'add-shop-user' | null>(null);
  const lastMutationSucceededSignal = signal(false);

  const store = {
    dispatch,
    selectSignal: vi.fn((selector: unknown): Signal<unknown> => {
      if (selector === selectUsersSubmitting) {
        return isSubmittingSignal;
      }

      if (selector === selectUsersErrorMessage) {
        return errorSignal;
      }

      if (selector === selectUsersLastMutationType) {
        return lastMutationTypeSignal;
      }

      if (selector === selectUsersLastMutationSucceeded) {
        return lastMutationSucceededSignal;
      }

      return signal(undefined);
    }),
  };

  function setup(): {
    component: AddShopUserOverlayComponent;
    fixture: ReturnType<typeof TestBed.createComponent<AddShopUserOverlayComponent>>;
  } {
    TestBed.configureTestingModule({
      imports: [AddShopUserOverlayComponent],
      providers: [{ provide: Store, useValue: store }],
    });

    const fixture = TestBed.createComponent(AddShopUserOverlayComponent);
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

  it('does not submit when password and confirm password mismatch', () => {
    const { component } = setup();

    component.form.controls.firstName.setValue('Sales');
    component.form.controls.lastName.setValue('User');
    component.form.controls.phoneNumber.setValue('+15551234567');
    component.form.controls.password.setValue('Pass1234!');
    component.form.controls.confirmPassword.setValue('Mismatch123!');
    component.form.controls.role.setValue('SalesPerson');

    component.onSubmit();

    expect(component.form.hasError('passwordMismatch')).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: UsersActions.addShopUserRequested.type })
    );
  });

  it('dispatches add user action with confirm password in payload', () => {
    const { component } = setup();

    component.form.controls.firstName.setValue('Sales');
    component.form.controls.lastName.setValue('User');
    component.form.controls.phoneNumber.setValue('+15551234567');
    component.form.controls.password.setValue('Pass1234!');
    component.form.controls.confirmPassword.setValue('Pass1234!');
    component.form.controls.role.setValue('SalesPerson');

    component.onSubmit();

    expect(dispatch).toHaveBeenCalledWith(UsersActions.clearError());
    expect(dispatch).toHaveBeenCalledWith(UsersActions.clearMutationStatus());
    expect(dispatch).toHaveBeenCalledWith(
      UsersActions.addShopUserRequested({
        payload: {
          firstName: 'Sales',
          lastName: 'User',
          phoneNumber: '+15551234567',
          password: 'Pass1234!',
          confirmPassword: 'Pass1234!',
          role: 'SalesPerson',
        },
      })
    );
  });
});
