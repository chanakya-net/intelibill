import { Signal, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Store } from '@ngrx/store';

import { RegisterActions } from '../state/register.actions';
import { selectRegisterErrorMessage, selectRegisterSubmitting } from '../state/register.selectors';
import { RegisterPageComponent } from './register-page.component';

describe('RegisterPageComponent', () => {
  const dispatch = vi.fn();
  const store = {
    dispatch,
    selectSignal: vi.fn((selector: unknown): Signal<unknown> => {
      if (selector === selectRegisterErrorMessage) {
        return signal('');
      }

      if (selector === selectRegisterSubmitting) {
        return signal(false);
      }

      return signal(undefined);
    }),
  };

  function setup(): RegisterPageComponent {
    TestBed.configureTestingModule({
      imports: [RegisterPageComponent],
      providers: [
        provideRouter([]),
        { provide: Store, useValue: store },
      ],
    });

    const fixture = TestBed.createComponent(RegisterPageComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    dispatch.mockReset();
    store.selectSignal.mockClear();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('clears server error on init', () => {
    setup();

    expect(dispatch).toHaveBeenCalledWith(RegisterActions.clearError());
  });

  it('does not dispatch requested action when form is invalid', () => {
    const component = setup();
    component.form.controls.firstName.setValue('');
    component.form.controls.lastName.setValue('');
    component.form.controls.email.setValue('');
    component.form.controls.password.setValue('');
    component.form.controls.confirmPassword.setValue('');

    component.onSubmit();

    expect(component.form.touched).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: RegisterActions.requested.type })
    );
  });

  it('marks form invalid when passwords do not match', () => {
    const component = setup();
    component.form.controls.firstName.setValue('Ada');
    component.form.controls.lastName.setValue('Lovelace');
    component.form.controls.email.setValue('ada@example.com');
    component.form.controls.password.setValue('Password123!');
    component.form.controls.confirmPassword.setValue('Mismatch123!');

    expect(component.form.valid).toBe(false);
    expect(component.form.errors).toEqual({ passwordMismatch: true });
  });

  it('dispatches clear and requested actions with trimmed payload on valid submit', () => {
    const component = setup();
    component.form.controls.firstName.setValue('  Ada  ');
    component.form.controls.lastName.setValue('  Lovelace  ');
    component.form.controls.email.setValue('ada@example.com');
    component.form.controls.password.setValue('Password123!');
    component.form.controls.confirmPassword.setValue('Password123!');
    component.form.controls.rememberMe.setValue(false);

    component.onSubmit();

    expect(dispatch).toHaveBeenNthCalledWith(2, RegisterActions.clearError());
    expect(dispatch).toHaveBeenNthCalledWith(
      3,
      RegisterActions.requested({
        firstName: 'Ada',
        lastName: 'Lovelace',
        email: 'ada@example.com',
        password: 'Password123!',
        rememberMe: false,
      })
    );
  });
});