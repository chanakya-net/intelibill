import { TestBed } from '@angular/core/testing';
import { convertToParamMap, ActivatedRoute, Router } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
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
      imports: [AuthCallbackComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
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

    expect(component.errorMessage()).toBe('errors.auth.missingCallbackData');
    expect(component.isBusy()).toBe(false);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login?externalAuthError=errors.auth.missingCallbackData');
    expect(sessionStorage.getItem('inventory.auth.external.error')).toBe('errors.auth.missingCallbackData');
  });

  it('maps invalid-state backend error to user message', () => {
    authService.completeExternalLogin.mockReturnValue(
      throwError(() => ({ error: { title: 'Auth.ExternalStateInvalid' } }))
    );

    const component = setup({ code: 'code-2', state: 'state-2' });

    expect(component.errorMessage()).toBe('errors.auth.externalStateInvalid');
    expect(component.isBusy()).toBe(false);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login?externalAuthError=errors.auth.externalStateInvalid');
    expect(sessionStorage.getItem('inventory.auth.external.error')).toBe('errors.auth.externalStateInvalid');
  });

  it('shows timeout message when callback exchange does not finish', () => {
    authService.completeExternalLogin.mockReturnValue(throwError(() => new TimeoutError()));

    const component = setup({ code: 'code-3', state: 'state-3' });

    expect(component.errorMessage()).toBe('errors.auth.signInTimeout');
    expect(component.isBusy()).toBe(false);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login?externalAuthError=errors.auth.signInTimeout');
    expect(sessionStorage.getItem('inventory.auth.external.error')).toBe('errors.auth.signInTimeout');
  });

  it('fails with clear message when callback returns without an authenticated session', () => {
    authService.isAuthenticated.mockReturnValue(false);
    authService.completeExternalLogin.mockReturnValue(of({} as AuthSession));

    const component = setup({ code: 'code-4', state: 'state-4' });

    expect(component.errorMessage()).toBe('errors.auth.noSessionEstablished');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login?externalAuthError=errors.auth.noSessionEstablished');
  });
});

