import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { Store } from '@ngrx/store';
import { of, throwError } from 'rxjs';

import { AuthSession } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { LoginPageComponent } from './login-page.component';

describe('LoginPageComponent', () => {
  const authService = {
    isAuthenticated: vi.fn<AuthService['isAuthenticated']>(),
    getLastRememberedEmail: vi.fn<AuthService['getLastRememberedEmail']>(),
    loginWithEmail: vi.fn<AuthService['loginWithEmail']>(),
  };

  const store = {
    selectSignal: vi.fn(() => signal(false)),
  };

  function setup(): { component: LoginPageComponent; navigateByUrl: ReturnType<typeof vi.spyOn> } {
    TestBed.configureTestingModule({
      imports: [LoginPageComponent, RouterTestingModule.withRoutes([])],
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: Store, useValue: store },
      ],
    });

    const router = TestBed.inject(Router);
    const navigateByUrl = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(LoginPageComponent);
    fixture.detectChanges();

    return {
      component: fixture.componentInstance,
      navigateByUrl,
    };
  }

  beforeEach(() => {
    authService.isAuthenticated.mockReturnValue(false);
    authService.getLastRememberedEmail.mockReturnValue('');
    authService.loginWithEmail.mockReturnValue(of({} as AuthSession));
    store.selectSignal.mockImplementation(() => signal(false));
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('redirects to overview on init when already authenticated', () => {
    authService.isAuthenticated.mockReturnValue(true);
    const { navigateByUrl } = setup();

    expect(navigateByUrl).toHaveBeenCalledWith('/overview');
  });

  it('prefills remembered email on init', () => {
    authService.getLastRememberedEmail.mockReturnValue('remembered@example.com');
    const { component } = setup();

    expect(component.form.controls.email.value).toBe('remembered@example.com');
    expect(component.form.controls.rememberMe.value).toBe(true);
  });

  it('does not submit when form is invalid', () => {
    const { component } = setup();
    component.form.controls.email.setValue('');
    component.form.controls.password.setValue('');

    component.onSubmit();

    expect(component.form.touched).toBe(true);
    expect(authService.loginWithEmail).not.toHaveBeenCalled();
  });

  it('submits and navigates on success', () => {
    const { component, navigateByUrl } = setup();
    component.form.controls.email.setValue('user@example.com');
    component.form.controls.password.setValue('Password123!');
    component.form.controls.rememberMe.setValue(true);

    component.onSubmit();

    expect(authService.loginWithEmail).toHaveBeenCalledWith('user@example.com', 'Password123!', true);
    expect(navigateByUrl).toHaveBeenCalledWith('/overview');
    expect(component.serverError()).toBe('');
  });

  it('maps invalid credential error into friendly message', () => {
    authService.loginWithEmail.mockReturnValue(
      throwError(() => ({ error: { title: 'Auth.InvalidCredentials' } }))
    );
    const { component } = setup();
    component.form.controls.email.setValue('user@example.com');
    component.form.controls.password.setValue('Password123!');

    component.onSubmit();

    expect(component.serverError()).toBe('The email or password is incorrect.');
  });
});