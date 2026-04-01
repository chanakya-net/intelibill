import { TestBed } from '@angular/core/testing';
import { convertToParamMap, ActivatedRoute, Router } from '@angular/router';
import { of, throwError, TimeoutError } from 'rxjs';

import { AuthSession } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { AuthCallbackComponent } from './auth-callback.component';

describe('AuthCallbackComponent', () => {
  const authService = {
    isAuthenticated: vi.fn<AuthService['isAuthenticated']>(),
    completeExternalLogin: vi.fn<AuthService['completeExternalLogin']>(),
  };

  const router = {
    navigateByUrl: vi.fn<Router['navigateByUrl']>().mockResolvedValue(true),
  };

  function setup(queryParams: Record<string, string>): AuthCallbackComponent {
    TestBed.configureTestingModule({
      imports: [AuthCallbackComponent],
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: Router, useValue: router },
        {
          provide: ActivatedRoute,
          useValue: {
            queryParamMap: of(convertToParamMap(queryParams)),
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(AuthCallbackComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    authService.isAuthenticated.mockReturnValue(false);
    authService.completeExternalLogin.mockReturnValue(of({} as AuthSession));
    router.navigateByUrl.mockClear();
    sessionStorage.clear();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('redirects to root when callback succeeds', () => {
    authService.isAuthenticated
      .mockReturnValueOnce(false)
      .mockReturnValue(true);
    setup({ code: 'code-1', state: 'state-1' });

    expect(authService.completeExternalLogin).toHaveBeenCalledWith('code-1', 'state-1');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/');
  });

  it('shows message when code or state is missing', () => {
    const component = setup({});

    expect(component.errorMessage()).toContain('Missing callback code or state');
    expect(component.isBusy()).toBe(false);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login?externalAuthError=Missing%20callback%20code%20or%20state.%20Please%20retry%20sign-in.');
    expect(sessionStorage.getItem('inventory.auth.external.error')).toContain('Missing callback code or state');
  });

  it('maps invalid-state backend error to user message', () => {
    authService.completeExternalLogin.mockReturnValue(
      throwError(() => ({ error: { title: 'Auth.ExternalStateInvalid' } }))
    );

    const component = setup({ code: 'code-2', state: 'state-2' });

    expect(component.errorMessage()).toBe('Your sign-in session expired. Please try again.');
    expect(component.isBusy()).toBe(false);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login?externalAuthError=Your%20sign-in%20session%20expired.%20Please%20try%20again.');
    expect(sessionStorage.getItem('inventory.auth.external.error')).toBe('Your sign-in session expired. Please try again.');
  });

  it('shows timeout message when callback exchange does not finish', () => {
    authService.completeExternalLogin.mockReturnValue(throwError(() => new TimeoutError()));

    const component = setup({ code: 'code-3', state: 'state-3' });

    expect(component.errorMessage()).toContain('Sign-in timed out');
    expect(component.isBusy()).toBe(false);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login?externalAuthError=Sign-in%20timed%20out.%20Please%20try%20again.');
    expect(sessionStorage.getItem('inventory.auth.external.error')).toContain('Sign-in timed out');
  });

  it('fails with clear message when callback returns without an authenticated session', () => {
    authService.isAuthenticated.mockReturnValue(false);
    authService.completeExternalLogin.mockReturnValue(of({} as AuthSession));

    const component = setup({ code: 'code-4', state: 'state-4' });

    expect(component.errorMessage()).toBe('Sign-in completed but no session was established. Please try again.');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login?externalAuthError=Sign-in%20completed%20but%20no%20session%20was%20established.%20Please%20try%20again.');
  });
});
