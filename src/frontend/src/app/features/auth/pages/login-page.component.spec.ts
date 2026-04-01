import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { AuthSession, ExternalAuthProvider } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { LoginPageComponent } from './login-page.component';

describe('LoginPageComponent', () => {
  const authService = {
    isAuthenticated: vi.fn<AuthService['isAuthenticated']>(),
    getLastRememberedEmail: vi.fn<AuthService['getLastRememberedEmail']>(),
    loginWithEmail: vi.fn<AuthService['loginWithEmail']>(),
    initializeExternalLogin: vi.fn<AuthService['initializeExternalLogin']>(),
  };

  const store = {
    selectSignal: vi.fn(() => signal(false)),
  };

  function setup(queryParams: Record<string, string> = {}): { component: LoginPageComponent; navigateByUrl: ReturnType<typeof vi.spyOn> } {
    TestBed.configureTestingModule({
      imports: [LoginPageComponent, RouterTestingModule.withRoutes([]), TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: Store, useValue: store },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              queryParamMap: convertToParamMap(queryParams),
            },
          },
        },
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
    authService.initializeExternalLogin.mockReturnValue(of('https://provider.example.com/oauth'));
    store.selectSignal.mockImplementation(() => signal(false));
    sessionStorage.clear();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('redirects to root on init when already authenticated', () => {
    authService.isAuthenticated.mockReturnValue(true);
    const { navigateByUrl } = setup();

    expect(navigateByUrl).toHaveBeenCalledWith('/');
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
    expect(navigateByUrl).toHaveBeenCalledWith('/');
    expect(component.serverError()).toBeNull();
  });

  it('maps invalid credential error into friendly message', () => {
    authService.loginWithEmail.mockReturnValue(
      throwError(() => ({ error: { title: 'Auth.InvalidCredentials' } }))
    );
    const { component } = setup();
    component.form.controls.email.setValue('user@example.com');
    component.form.controls.password.setValue('Password123!');

    component.onSubmit();

    expect(component.serverError()).toBe('errors.auth.invalidCredentials');
  });

  it('starts google login and redirects browser to provider', () => {
    const { component } = setup();

    component.onGoogleLogin();

    expect(authService.initializeExternalLogin).toHaveBeenCalledWith(ExternalAuthProvider.Google);
    expect(component.serverError()).toBeNull();
  });

  it('sets server error when external login init fails', () => {
    authService.initializeExternalLogin.mockReturnValue(
      throwError(() => ({ error: { title: 'Auth.UnsupportedProvider' } }))
    );
    const { component } = setup();

    component.onFacebookLogin();

    expect(authService.initializeExternalLogin).toHaveBeenCalledWith(ExternalAuthProvider.Facebook);
    expect(component.serverError()).toBe('Google login is not enabled on the server.');
  });

  it('reads external auth error from query string', () => {
    const { component } = setup({ externalAuthError: 'Sign-in timed out. Please try again.' });

    expect(component.serverError()).toBe('Sign-in timed out. Please try again.');
  });

  it('reads external auth error from session storage fallback', () => {
    sessionStorage.setItem('inventory.auth.external.error', 'External sign-in failed.');
    const { component } = setup();

    expect(component.serverError()).toBe('External sign-in failed.');
    expect(sessionStorage.getItem('inventory.auth.external.error')).toBeNull();
  });

  it('shows fallback error when external sign-in was pending but no callback error was provided', () => {
    sessionStorage.setItem('inventory.auth.external.pending', ExternalAuthProvider.Google.toString());
    const { component } = setup();

    expect(component.serverError()).toBe('External sign-in did not complete. Please try again.');
    expect(sessionStorage.getItem('inventory.auth.external.pending')).toBeNull();
  });
});
