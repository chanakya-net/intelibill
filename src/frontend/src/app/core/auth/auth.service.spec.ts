import { PLATFORM_ID, signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';

import { AuthResult, AuthSession } from './auth.models';
import { AuthService } from './auth.service';
import { AuthStorage } from './auth.storage';
import { AUTH_ENDPOINTS } from './auth.constants';

function buildAuthResult(overrides?: Partial<AuthResult>): AuthResult {
  const now = Date.now();

  return {
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    accessTokenExpiresAt: new Date(now + 60_000).toISOString(),
    refreshTokenExpiresAt: new Date(now + 120_000).toISOString(),
    user: {
      id: 'user-1',
      email: 'user@example.com',
      phoneNumber: null,
      firstName: 'Test',
      lastName: 'User',
    },
    activeShopId: null,
    shops: [],
    ...overrides,
  };
}

function buildSession(overrides?: Partial<AuthSession>): AuthSession {
  const result = buildAuthResult();

  return {
    accessToken: result.accessToken,
    refreshToken: result.refreshToken,
    accessTokenExpiresAt: result.accessTokenExpiresAt,
    refreshTokenExpiresAt: result.refreshTokenExpiresAt,
    rememberMe: true,
    user: result.user,
    activeShopId: result.activeShopId,
    shops: result.shops,
    ...overrides,
  };
}

describe('AuthService', () => {
  const storage = {
    loadSession: vi.fn<AuthStorage['loadSession']>(),
    saveSession: vi.fn<AuthStorage['saveSession']>(),
    clearSession: vi.fn<AuthStorage['clearSession']>(),
    saveLastEmail: vi.fn<AuthStorage['saveLastEmail']>(),
    getLastEmail: vi.fn<AuthStorage['getLastEmail']>(),
    clearLastEmail: vi.fn<AuthStorage['clearLastEmail']>(),
  };

  const router = {
    navigateByUrl: vi.fn<Router['navigateByUrl']>(),
  };

  function setup(): { service: AuthService; http: HttpTestingController } {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PLATFORM_ID, useValue: 'browser' },
        { provide: AuthStorage, useValue: storage },
        { provide: Router, useValue: router },
      ],
    });

    return {
      service: TestBed.inject(AuthService),
      http: TestBed.inject(HttpTestingController),
    };
  }

  beforeEach(() => {
    storage.loadSession.mockReturnValue(null);
    storage.saveSession.mockReset();
    storage.clearSession.mockReset();
    storage.saveLastEmail.mockReset();
    storage.getLastEmail.mockReturnValue('');
    storage.clearLastEmail.mockReset();
    router.navigateByUrl.mockResolvedValue(true);
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('logs in and stores last email when rememberMe is true', () => {
    const { service, http } = setup();
    const result = buildAuthResult();
    let emitted: AuthSession | undefined;

    service.loginWithEmail(' user@example.com ', 'pw', true).subscribe((session) => {
      emitted = session;
    });

    const request = http.expectOne(AUTH_ENDPOINTS.loginWithEmail);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: ' user@example.com ', password: 'pw' });
    request.flush(result);

    expect(emitted?.accessToken).toBe(result.accessToken);
    expect(emitted?.rememberMe).toBe(true);
    expect(storage.saveSession).toHaveBeenCalledTimes(1);
    expect(storage.saveLastEmail).toHaveBeenCalledWith(' user@example.com ');
    expect(storage.clearLastEmail).not.toHaveBeenCalled();
    expect(service.isAuthenticated()).toBe(true);

    http.verify();
  });

  it('clears last email when rememberMe is false', () => {
    const { service, http } = setup();

    service.loginWithEmail('user@example.com', 'pw', false).subscribe();

    const request = http.expectOne(AUTH_ENDPOINTS.loginWithEmail);
    request.flush(buildAuthResult());

    expect(storage.clearLastEmail).toHaveBeenCalledTimes(1);
    expect(storage.saveLastEmail).not.toHaveBeenCalled();

    http.verify();
  });

  it('refreshes access token when refresh token is valid', () => {
    const now = Date.now();
    storage.loadSession.mockReturnValue(
      buildSession({
        accessTokenExpiresAt: new Date(now - 10_000).toISOString(),
        refreshTokenExpiresAt: new Date(now + 60_000).toISOString(),
      })
    );
    const { service, http } = setup();
    let emitted: AuthSession | null | undefined;

    service.refreshAccessToken().subscribe((session) => {
      emitted = session;
    });

    const request = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ refreshToken: 'refresh-token' });
    request.flush(
      buildAuthResult({
        accessToken: 'new-access-token',
      })
    );

    expect(emitted?.accessToken).toBe('new-access-token');
    expect(service.getAccessToken()).toBe('new-access-token');
    expect(storage.saveSession).toHaveBeenCalledTimes(1);

    http.verify();
  });

  it('bootstrapSession clears expired refresh token and returns false', () => {
    const now = Date.now();
    storage.loadSession.mockReturnValue(
      buildSession({
        accessTokenExpiresAt: new Date(now - 10_000).toISOString(),
        refreshTokenExpiresAt: new Date(now - 1_000).toISOString(),
      })
    );
    const { service } = setup();
    let emitted: boolean | undefined;

    service.bootstrapSession().subscribe((value) => {
      emitted = value;
    });

    expect(emitted).toBe(false);
    expect(storage.clearSession).toHaveBeenCalledTimes(1);
    expect(service.session()).toBeNull();
  });

  it('signOut revokes refresh token and clears session', () => {
    storage.loadSession.mockReturnValue(buildSession());
    const { service, http } = setup();

    service.signOut().subscribe();

    const request = http.expectOne(AUTH_ENDPOINTS.revokeToken);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ refreshToken: 'refresh-token' });
    request.flush({});

    expect(storage.clearSession).toHaveBeenCalledTimes(1);
    expect(service.session()).toBeNull();

    http.verify();
  });
});