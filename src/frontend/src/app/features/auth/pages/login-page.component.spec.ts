import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { AuthSession } from '../../../core/auth/auth.models';
import { IdentityFlowService } from '../../../core/auth/identity-flow.service';
import { AuthService } from '../../../core/auth/auth.service';
import { LoginPageComponent } from './login-page.component';

describe('LoginPageComponent', () => {
  const authService = {
    isAuthenticated: vi.fn<AuthService['isAuthenticated']>(),
    getLastRememberedIdentifier: vi.fn<AuthService['getLastRememberedIdentifier']>(),
    login: vi.fn<AuthService['login']>(),
    getPhoneAuthAvailability: vi.fn<AuthService['getPhoneAuthAvailability']>(),
  };

  const identityFlow = {
    afterSignIn: vi.fn(),
    pendingVerification: vi.fn(),
  };
  const store = {
    selectSignal: vi.fn(() => signal(false)),
  };
  function setup(
    queryParams: Record<string, string> = {},
    navigationState: Record<string, unknown> | null = null,
  ): { component: LoginPageComponent; navigateByUrl: ReturnType<typeof vi.spyOn> } {
    TestBed.configureTestingModule({
      imports: [
        LoginPageComponent,
        RouterTestingModule.withRoutes([]),
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: IdentityFlowService, useValue: identityFlow },
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
    if (navigationState) {
      vi.spyOn(router, 'getCurrentNavigation').mockReturnValue({
        extras: { state: navigationState },
      } as never);
    }
    const fixture = TestBed.createComponent(LoginPageComponent);
    fixture.detectChanges();

    return {
      component: fixture.componentInstance,
      navigateByUrl,
    };
  }

  beforeEach(() => {
    authService.isAuthenticated.mockReturnValue(false);
    authService.getLastRememberedIdentifier.mockReturnValue('');
    authService.login.mockReturnValue(of({} as AuthSession));
    authService.getPhoneAuthAvailability.mockReturnValue(of({ enabled: false }));
    store.selectSignal.mockImplementation(() => signal(false));
    sessionStorage.clear();
    identityFlow.afterSignIn.mockReturnValue('/');
    identityFlow.pendingVerification.mockReturnValue(null);
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
    authService.getLastRememberedIdentifier.mockReturnValue('remembered@example.com');
    const { component } = setup();

    expect(component.form.controls.identifier.value).toBe('remembered@example.com');
    expect(component.form.controls.rememberMe.value).toBe(true);
  });

  it('prefills remembered phone identifier on init', () => {
    authService.getLastRememberedIdentifier.mockReturnValue('9876543210');
    const { component } = setup();

    expect(component.form.controls.identifier.value).toBe('9876543210');
    expect(component.form.controls.rememberMe.value).toBe(true);
  });

  it('does not submit when form is invalid', () => {
    const { component } = setup();
    component.form.controls.identifier.setValue('');
    component.form.controls.password.setValue('');

    component.onSubmit();

    expect(component.form.touched).toBe(true);
    expect(authService.login).not.toHaveBeenCalled();
  });

  it('rejects whitespace-only credentials', () => {
    const { component } = setup();
    component.form.patchValue({ identifier: '   ', password: '   ' });

    expect(component.form.controls.identifier.hasError('required')).toBe(true);
    expect(component.form.controls.password.hasError('required')).toBe(true);
  });

  it('submits and navigates on success', () => {
    const { component, navigateByUrl } = setup();
    component.form.controls.identifier.setValue('user@example.com');
    component.form.controls.password.setValue('Password123!');
    component.form.controls.rememberMe.setValue(true);

    component.onSubmit();

    expect(authService.login).toHaveBeenCalledWith('user@example.com', 'Password123!', true);
    expect(navigateByUrl).toHaveBeenCalledWith('/');
    expect(component.serverError()).toBeNull();
  });

  it('accepts phone-like identifier and calls generic auth login', () => {
    const { component } = setup();
    component.form.controls.identifier.setValue('9876543210');
    component.form.controls.password.setValue('Password123!');
    component.form.controls.rememberMe.setValue(false);

    component.onSubmit();

    expect(authService.login).toHaveBeenCalledWith('9876543210', 'Password123!', false);
  });

  it('trims identifier before submit', () => {
    const { component } = setup();
    component.form.controls.identifier.setValue('  user@example.com  ');
    component.form.controls.password.setValue('Password123!');

    component.onSubmit();

    expect(authService.login).toHaveBeenCalledWith('user@example.com', 'Password123!', true);
  });

  it('maps invalid credential error into friendly message', () => {
    authService.login.mockReturnValue(
      throwError(() => ({ error: { title: 'Auth.InvalidCredentials' } })),
    );
    const { component } = setup();
    component.form.controls.identifier.setValue('user@example.com');
    component.form.controls.password.setValue('Password123!');

    component.onSubmit();

    expect(component.serverError()).toBe('errors.auth.invalidCredentials');
  });

  it('shows the password reset success message from the query string', () => {
    const { component } = setup({ passwordReset: 'success' });

    expect(component.successMessage()).toBe('auth.resetPassword.successMessage');
    expect(component.serverError()).toBeNull();
  });

  it('shows the password reset success message from navigation state', () => {
    const { component } = setup({}, { passwordResetSuccess: true });

    expect(component.successMessage()).toBe('auth.resetPassword.successMessage');
    expect(component.serverError()).toBeNull();
  });
  it('password login resumes pending email confirmation without putting bearer proof in navigation', () => {
    identityFlow.afterSignIn.mockReturnValue('/auth/verify-email');
    const { component, navigateByUrl } = setup();
    component.form.patchValue({ identifier: 'old@example.com', password: 'Password123!' });
    component.onSubmit();
    expect(navigateByUrl).toHaveBeenCalledWith('/auth/verify-email');
  });
  it.each([
    ['support-required', 'identity.supportRequired'],
    ['delivery-unavailable', 'identity.deliveryUnavailable'],
    ['failed', 'identity.reauthFailed'],
  ])('renders allowlisted login destination reason %s', (reason, message) => {
    expect(setup({ identity: reason }).component.serverError()).toBe(message);
  });
  it('ignores non-allowlisted login outcome text', () => {
    expect(setup({ identity: 'private-token-do-not-display' }).component.serverError()).toBeNull();
  });
});
