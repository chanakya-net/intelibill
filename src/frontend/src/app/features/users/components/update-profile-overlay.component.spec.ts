import { signal, Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { vi } from 'vitest';

import { UpdateProfileOverlayComponent } from './update-profile-overlay.component';
import { UsersActions } from '../state/users.actions';
import {
  selectUsersErrorMessage,
  selectUsersLastMutationSucceeded,
  selectUsersLastMutationType,
  selectUsersSubmitting,
} from '../state/users.selectors';

describe('UpdateProfileOverlayComponent', () => {
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
    component: UpdateProfileOverlayComponent;
    fixture: ReturnType<typeof TestBed.createComponent<UpdateProfileOverlayComponent>>;
  } {
    TestBed.configureTestingModule({
      imports: [UpdateProfileOverlayComponent],
      providers: [{ provide: Store, useValue: store }],
    });

    const fixture = TestBed.createComponent(UpdateProfileOverlayComponent);
    fixture.componentRef.setInput('user', {
      id: 'user-1',
      email: 'user@example.com',
      phoneNumber: '+15551234567',
      firstName: 'Test',
      lastName: 'User',
    });
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
    component.form.controls.email.setValue('');

    component.onSubmit();

    expect(component.form.touched).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: UsersActions.updateProfileRequested.type })
    );
  });

  it('dispatches update action with trimmed values and emits close on success', () => {
    const { component, fixture } = setup();
    const closeSpy = vi.fn();
    component.closeRequested.subscribe(closeSpy);

    component.form.controls.firstName.setValue('  Jane  ');
    component.form.controls.lastName.setValue('  Doe  ');
    component.form.controls.email.setValue('jane@example.com');
    component.form.controls.phoneNumber.setValue('+15557654321');

    component.onSubmit();

    expect(dispatch).toHaveBeenCalledWith(UsersActions.clearError());
    expect(dispatch).toHaveBeenCalledWith(UsersActions.clearMutationStatus());
    expect(dispatch).toHaveBeenCalledWith(
      UsersActions.updateProfileRequested({
        payload: {
          firstName: 'Jane',
          lastName: 'Doe',
          email: 'jane@example.com',
          phoneNumber: '+15557654321',
        },
      })
    );

    lastMutationTypeSignal.set('update-profile');
    lastMutationSucceededSignal.set(true);
    fixture.detectChanges();

    expect(closeSpy).toHaveBeenCalledTimes(1);
  });

  it('reads server error from selector', () => {
    const { component, fixture } = setup();
    errorSignal.set('This email is already used by another account.');
    fixture.detectChanges();

    expect(component.serverError()).toBe('This email is already used by another account.');
  });
});
