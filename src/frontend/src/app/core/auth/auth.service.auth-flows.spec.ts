import { describe, expect, it } from 'vitest';

import { AUTH_ENDPOINTS } from './auth.constants';
import { AuthSession, ExternalAuthProvider } from './auth.models';
import { createAuthServiceTestHarness, buildAuthResult } from './auth.service.test-utils';

describe('AuthService (auth flows)', () => {
  const t = createAuthServiceTestHarness();

  it('logs in and stores trimmed last identifier when rememberMe is true', () => {
    const { service, http } = t.setup();
    const result = buildAuthResult();
    let emitted: AuthSession | undefined;

    service.login(' user@example.com ', 'pw', true).subscribe((session) => {
      emitted = session;
    });

    const request = http.expectOne(AUTH_ENDPOINTS.login);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ identifier: 'user@example.com', password: 'pw' });
    request.flush(result);

    expect(emitted?.accessToken).toBe(result.accessToken);
    expect(emitted?.rememberMe).toBe(true);
    expect(t.storage.saveSession).toHaveBeenCalledTimes(1);
    expect(t.storage.saveLastIdentifier).toHaveBeenCalledWith('user@example.com');
    expect(t.storage.clearLastIdentifier).not.toHaveBeenCalled();
    expect(service.isAuthenticated()).toBe(true);

    http.verify();
  });

  it('registers with email and sends phone number in the payload', () => {
    const { service, http } = t.setup();
    const result = buildAuthResult();
    let emitted: AuthSession | undefined;

    service
      .registerWithEmail('First', 'Last', 'user@example.com', '+15551234567', 'pw', true)
      .subscribe((session) => {
        emitted = session;
      });

    const request = http.expectOne(AUTH_ENDPOINTS.registerWithEmail);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      firstName: 'First',
      lastName: 'Last',
      email: 'user@example.com',
      phoneNumber: '+15551234567',
      password: 'pw',
    });
    request.flush(result);

    expect(emitted?.accessToken).toBe(result.accessToken);
    expect(t.storage.saveSession).toHaveBeenCalledTimes(1);
    expect(t.storage.saveLastIdentifier).toHaveBeenCalledWith('user@example.com');

    http.verify();
  });

  it('clears last identifier when rememberMe is false', () => {
    const { service, http } = t.setup();

    service.login('user@example.com', 'pw', false).subscribe();

    const request = http.expectOne(AUTH_ENDPOINTS.login);
    request.flush(buildAuthResult());

    expect(t.storage.clearLastIdentifier).toHaveBeenCalledTimes(1);
    expect(t.storage.saveLastIdentifier).not.toHaveBeenCalled();

    http.verify();
  });

  it('signOut revokes refresh token and clears session', () => {
    t.storage.loadSession.mockReturnValueOnce({
      ...buildAuthResult(),
      rememberMe: true,
      activeShopId: null,
      shops: [],
      user: buildAuthResult().user,
    } as unknown as AuthSession);
    const { service, http } = t.setup();

    service.signOut().subscribe();

    const request = http.expectOne(AUTH_ENDPOINTS.revokeToken);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ refreshToken: 'refresh-token' });
    request.flush({});

    expect(t.storage.clearSession).toHaveBeenCalledTimes(1);
    expect(service.session()).toBeNull();

    http.verify();
  });

  it('initializeExternalLogin returns authorization url', () => {
    const { service, http } = t.setup();
    let resultUrl = '';

    service.initializeExternalLogin(ExternalAuthProvider.Google).subscribe((url) => {
      resultUrl = url;
    });

    const request = http.expectOne(AUTH_ENDPOINTS.loginExternalInit);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ provider: ExternalAuthProvider.Google });
    request.flush({ authorizationUrl: 'https://accounts.google.com/o/oauth2/v2/auth?client_id=test' });

    expect(resultUrl).toContain('accounts.google.com');
    http.verify();
  });

  it('completeExternalLogin stores session from callback response', () => {
    const { service, http } = t.setup();
    let emitted: AuthSession | undefined;

    service.completeExternalLogin('code-123', 'state-123').subscribe((session) => {
      emitted = session;
    });

    const request = http.expectOne(AUTH_ENDPOINTS.loginExternalCallback);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      code: 'code-123',
      state: 'state-123',
      firstName: undefined,
      lastName: undefined,
    });

    request.flush(buildAuthResult({ accessToken: 'external-access-token' }));

    expect(emitted?.accessToken).toBe('external-access-token');
    expect(service.getAccessToken()).toBe('external-access-token');
    expect(t.storage.saveSession).toHaveBeenCalledTimes(1);

    http.verify();
  });

  it('requestPasswordReset posts trimmed email to endpoint', () => {
    const { service, http } = t.setup();

    service.requestPasswordReset('  user@example.com  ').subscribe();

    const request = http.expectOne(AUTH_ENDPOINTS.requestPasswordReset);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: 'user@example.com' });

    request.flush({});
    http.verify();
  });

  it('requestPasswordReset returns void observable on success', () => {
    const { service, http } = t.setup();
    let completed = false;

    service.requestPasswordReset('user@example.com').subscribe({
      complete: () => {
        completed = true;
      },
    });

    const request = http.expectOne(AUTH_ENDPOINTS.requestPasswordReset);
    request.flush({});

    expect(completed).toBe(true);
    http.verify();
  });

  it('resetPassword posts confirmation payload to endpoint', () => {
    const { service, http } = t.setup();

    service.resetPassword('user@example.com', 'token-123', 'Password123!').subscribe();

    const request = http.expectOne(AUTH_ENDPOINTS.confirmPasswordReset);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      email: 'user@example.com',
      token: 'token-123',
      newPassword: 'Password123!',
    });

    request.flush({});
    http.verify();
  });

  it('resetPassword returns void observable on success', () => {
    const { service, http } = t.setup();
    let completed = false;

    service.resetPassword('user@example.com', 'token-123', 'Password123!').subscribe({
      complete: () => {
        completed = true;
      },
    });

    const request = http.expectOne(AUTH_ENDPOINTS.confirmPasswordReset);
    request.flush({});

    expect(completed).toBe(true);
    http.verify();
  });
});
