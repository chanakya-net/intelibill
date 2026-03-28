import { signal, Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { vi } from 'vitest';

import { ChangePasswordOverlayComponent } from './change-password-overlay.component';
import { UsersActions } from '../state/users.actions';
import {
  selectUsersErrorMessage,
  selectUsersLastMutationSucceeded,
  selectUsersLastMutationType,
  selectUsersSubmitting,
} from '../state/users.selectors';

describe('ChangePasswordOverlayComponent', () => {
  const dispatch = vi.fn();
  const isSubmittingSignal = signal(false);
  const errorSignal = signal('');
  const lastMutationTypeSignal = signal<'update-profile' | 'change-password' | null>(null);
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
    component: ChangePasswordOverlayComponent;
    fixture: ReturnType<typeof TestBed.createComponent<ChangePasswordOverlayComponent>>;
  } {
    TestBed.configureTestingModule({
      imports: [ChangePasswordOverlayComponent],
      providers: [{ provide: Store, useValue: store }],
    });

    const fixture = TestBed.createComponent(ChangePasswordOverlayComponent);
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

  it('does not submit when form is invalid', () => {
    const { component } = setup();
    component.form.controls.currentPassword.setValue('');
    component.form.controls.newPassword.setValue('short');
    component.form.controls.confirmNewPassword.setValue('short');

    component.onSubmit();

    expect(component.form.touched).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: UsersActions.changePasswordRequested.type })
    );
  });

  it('dispatches change password action and emits closeRequested on success', () => {
    const { component, fixture } = setup();
    const closeSpy = vi.fn();
    component.closeRequested.subscribe(closeSpy);

    component.form.controls.currentPassword.setValue('OldPass123!');
    component.form.controls.newPassword.setValue('NewPass123!');
    component.form.controls.confirmNewPassword.setValue('NewPass123!');

    component.onSubmit();

    expect(dispatch).toHaveBeenCalledWith(UsersActions.clearError());
    expect(dispatch).toHaveBeenCalledWith(UsersActions.clearMutationStatus());
    expect(dispatch).toHaveBeenCalledWith(
      UsersActions.changePasswordRequested({
        payload: {
          currentPassword: 'OldPass123!',
          newPassword: 'NewPass123!',
        },
      })
    );

    lastMutationTypeSignal.set('change-password');
    lastMutationSucceededSignal.set(true);
    fixture.detectChanges();

    expect(closeSpy).toHaveBeenCalledTimes(1);
  });

  it('reads server error from selector', () => {
    const { component, fixture } = setup();
    errorSignal.set('Current password is incorrect.');
    fixture.detectChanges();

    expect(component.serverError()).toBe('Current password is incorrect.');
  });

  it('does not submit when retyped password does not match', () => {
    const { component } = setup();

    component.form.controls.currentPassword.setValue('OldPass123!');
    component.form.controls.newPassword.setValue('NewPass123!');
    component.form.controls.confirmNewPassword.setValue('Different123!');

    component.onSubmit();

    expect(component.form.hasError('passwordMismatch')).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: UsersActions.changePasswordRequested.type })
    );
  });
});
