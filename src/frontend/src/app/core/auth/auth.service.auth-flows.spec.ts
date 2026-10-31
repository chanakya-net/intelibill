import { describe, expect, it } from 'vitest';

import { AUTH_ENDPOINTS } from './auth.constants';
import { AuthSession } from './auth.models';
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

  it('requests email enrollment without creating or storing a session', () => {
    const { service, http } = t.setup();
    service.registerWithEmail(' user@example.com ').subscribe();
    const request = http.expectOne(AUTH_ENDPOINTS.requestEmailEnrollment);
    expect(request.request.body).toEqual({ email: 'user@example.com' });
    request.flush({ message: 'generic' }, { status: 202, statusText: 'Accepted' });
    expect(service.session()).toBeNull();
    expect(t.storage.saveSession).not.toHaveBeenCalled();
    expect(t.storage.saveLastIdentifier).not.toHaveBeenCalled();
    http.verify();
  });

  it('confirms owner-chosen credentials before establishing a session', () => {
    const { service, http } = t.setup();
    const payload = {
      challengeId: 'id',
      token: 'proof',
      password: 'Chosen123!',
      firstName: 'Ada',
      lastName: 'L',
    };
    service.confirmEmailEnrollment(payload).subscribe();
    const request = http.expectOne(AUTH_ENDPOINTS.confirmEmailEnrollment);
    expect(request.request.body).toEqual(payload);
    expect(t.storage.saveSession).not.toHaveBeenCalled();
    request.flush(buildAuthResult());
    expect(service.isAuthenticated()).toBe(true);
    http.verify();
  });

  it('email-change acceptance preserves current address but confirmation clears session', () => {
    const { service, http } = t.setup();
    service.applyAuthResult(buildAuthResult());
    service.requestEmailChange('new@example.com', 'recent').subscribe();
    const request = http.expectOne(AUTH_ENDPOINTS.requestEmailChange);
    expect(request.request.body).toEqual({ email: 'new@example.com', reauthProof: 'recent' });
    request.flush({ message: 'pending' }, { status: 202, statusText: 'Accepted' });
    expect(service.session()?.user.email).toBe('user@example.com');
    service.confirmEmailChange('id', 'proof').subscribe();
    http.expectOne(AUTH_ENDPOINTS.confirmEmailChange).flush({ message: 'updated' });
    expect(service.session()).toBeNull();
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
