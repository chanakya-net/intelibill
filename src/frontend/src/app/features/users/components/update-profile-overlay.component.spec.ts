import { signal, Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
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
      imports: [
        UpdateProfileOverlayComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [provideRouter([]), { provide: Store, useValue: store }],
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
    component.form.controls.firstName.setValue('');

    component.onSubmit();

    expect(component.form.touched).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: UsersActions.updateProfileRequested.type }),
    );
  });

  it('rejects whitespace-only profile names', () => {
    const { component } = setup();
    component.form.controls.firstName.setValue('   ');
    component.form.controls.lastName.setValue('   ');

    expect(component.form.controls.firstName.hasError('required')).toBe(true);
    expect(component.form.controls.lastName.hasError('required')).toBe(true);
  });

  it('emailChangeKeepsOldAddressUntilConfirmed', () => {
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
          email: 'user@example.com',
          phoneNumber: '+15557654321',
          language: 'en-IN',
        },
      }),
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
  it('lets email-free accounts save names and phone without claiming an email', () => {
    const { component, fixture } = setup();
    fixture.componentRef.setInput('user', {
      id: 'user-1',
      email: null,
      phoneNumber: null,
      firstName: 'First',
      lastName: 'Last',
    });
    component.ngOnInit();
    component.form.controls.firstName.setValue('Updated');
    component.onSubmit();
    expect(dispatch).toHaveBeenCalledWith(
      UsersActions.updateProfileRequested({
        payload: {
          firstName: 'Updated',
          lastName: 'Last',
          email: '',
          phoneNumber: null,
          language: 'en-IN',
        },
      }),
    );
  });
});
