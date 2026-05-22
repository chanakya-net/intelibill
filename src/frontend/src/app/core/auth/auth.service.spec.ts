import '@angular/compiler';

import { PLATFORM_ID } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { firstValueFrom } from 'rxjs';

import { AuthResult, AuthSession, ExternalAuthProvider } from './auth.models';
import { AuthService } from './auth.service';
import { AuthStorage } from './auth.storage';
import { AUTH_ENDPOINTS } from './auth.constants';
import { LocalizationService } from '../i18n/localization.service';
import { NetworkStatusService } from '../services/network-status.service';
import { OfflineSalesDeviceSettingsStorage } from '../storage/offline-sales-device-settings.storage';
import { OfflineSalesSnapshotIndexedDbService } from '../storage/offline-sales-snapshot-indexeddb.service';

class FakeBroadcastChannel {
  static instances: FakeBroadcastChannel[] = [];

  static reset(): void {
    FakeBroadcastChannel.instances = [];
  }

  readonly name: string;
  onmessage: ((event: MessageEvent<unknown>) => void) | null = null;

  private listeners = new Set<(event: MessageEvent<unknown>) => void>();
  private closed = false;

  constructor(name: string) {
    this.name = name;
    FakeBroadcastChannel.instances.push(this);
  }

  addEventListener(type: string, listener: EventListenerOrEventListenerObject): void {
    if (type !== 'message') {
      return;
    }

    if (typeof listener === 'function') {
      this.listeners.add(listener as (event: MessageEvent<unknown>) => void);
      return;
    }

    this.listeners.add((event) => listener.handleEvent(event));
  }

  removeEventListener(type: string, listener: EventListenerOrEventListenerObject): void {
    if (type !== 'message' || typeof listener !== 'function') {
      return;
    }

    this.listeners.delete(listener as (event: MessageEvent<unknown>) => void);
  }

  postMessage(message: unknown): void {
    for (const instance of FakeBroadcastChannel.instances) {
      if (instance === this || instance.closed || instance.name !== this.name) {
        continue;
      }

      instance.dispatch(message);
    }
  }

  close(): void {
    this.closed = true;
  }

  private dispatch(data: unknown): void {
    const event = { data } as MessageEvent<unknown>;
    this.onmessage?.(event);
    for (const listener of this.listeners) {
      listener(event);
    }
  }
}

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
  const originalBroadcastChannel = globalThis.BroadcastChannel;
  const storage = {
    loadSession: vi.fn<AuthStorage['loadSession']>(),
    saveSession: vi.fn<AuthStorage['saveSession']>(),
    clearSession: vi.fn<AuthStorage['clearSession']>(),
    saveLastIdentifier: vi.fn<AuthStorage['saveLastIdentifier']>(),
    getLastIdentifier: vi.fn<AuthStorage['getLastIdentifier']>(),
    clearLastIdentifier: vi.fn<AuthStorage['clearLastIdentifier']>(),
  };

  const router = {
    navigateByUrl: vi.fn<Router['navigateByUrl']>(),
  };

  const localizationService = {
    setLanguage: vi.fn<LocalizationService['setLanguage']>().mockResolvedValue(undefined),
  };
  const networkStatus = {
    canReachApi: vi.fn<NetworkStatusService['canReachApi']>(),
    checkConnectivity: vi.fn<NetworkStatusService['checkConnectivity']>().mockResolvedValue(undefined),
  };
  const offlineSalesDeviceSettingsStorage = {
    loadSettings: vi.fn<OfflineSalesDeviceSettingsStorage['loadSettings']>(),
  };
  const offlineSalesSnapshotDb = {
    getUsableSnapshotInfo: vi.fn<OfflineSalesSnapshotIndexedDbService['getUsableSnapshotInfo']>(),
  };

  function setup(): { service: AuthService; http: HttpTestingController } {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PLATFORM_ID, useValue: 'browser' },
        { provide: AuthStorage, useValue: storage },
        { provide: Router, useValue: router },
        { provide: LocalizationService, useValue: localizationService },
        { provide: NetworkStatusService, useValue: networkStatus },
        { provide: OfflineSalesDeviceSettingsStorage, useValue: offlineSalesDeviceSettingsStorage },
        { provide: OfflineSalesSnapshotIndexedDbService, useValue: offlineSalesSnapshotDb },
      ],
    });

    return {
      service: TestBed.inject(AuthService),
      http: TestBed.inject(HttpTestingController),
    };
  }

  beforeAll(() => {
    vi.stubGlobal('BroadcastChannel', FakeBroadcastChannel);
  });

  beforeEach(() => {
    FakeBroadcastChannel.reset();
    storage.loadSession.mockReturnValue(null);
    storage.saveSession.mockReset();
    storage.clearSession.mockReset();
    storage.saveLastIdentifier.mockReset();
    storage.getLastIdentifier.mockReturnValue('');
    storage.clearLastIdentifier.mockReset();
    router.navigateByUrl.mockResolvedValue(true);
    localizationService.setLanguage.mockClear();
    networkStatus.canReachApi.mockReturnValue(true);
    networkStatus.checkConnectivity.mockClear();
    offlineSalesDeviceSettingsStorage.loadSettings.mockReset();
    offlineSalesSnapshotDb.getUsableSnapshotInfo.mockReset();
  });

  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  afterAll(() => {
    if (originalBroadcastChannel) {
      vi.stubGlobal('BroadcastChannel', originalBroadcastChannel);
      return;
    }

    vi.unstubAllGlobals();
  });

  it('logs in and stores trimmed last identifier when rememberMe is true', () => {
    const { service, http } = setup();
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
    expect(storage.saveSession).toHaveBeenCalledTimes(1);
    expect(storage.saveLastIdentifier).toHaveBeenCalledWith('user@example.com');
    expect(storage.clearLastIdentifier).not.toHaveBeenCalled();
    expect(service.isAuthenticated()).toBe(true);

    http.verify();
  });

  it('registers with email and sends phone number in the payload', () => {
    const { service, http } = setup();
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
    expect(storage.saveSession).toHaveBeenCalledTimes(1);
    expect(storage.saveLastIdentifier).toHaveBeenCalledWith('user@example.com');

    http.verify();
  });

  it('clears last identifier when rememberMe is false', () => {
    const { service, http } = setup();

    service.login('user@example.com', 'pw', false).subscribe();

    const request = http.expectOne(AUTH_ENDPOINTS.login);
    request.flush(buildAuthResult());

    expect(storage.clearLastIdentifier).toHaveBeenCalledTimes(1);
    expect(storage.saveLastIdentifier).not.toHaveBeenCalled();

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

  it('recovers from a refresh race by adopting a fresher stored session', () => {
    const now = Date.now();
    const initialSession = buildSession({
      accessTokenExpiresAt: new Date(now - 10_000).toISOString(),
      refreshTokenExpiresAt: new Date(now + 60_000).toISOString(),
    });
    const rotatedSession = buildSession({
      accessToken: 'rotated-access-token',
      refreshToken: 'rotated-refresh-token',
      accessTokenExpiresAt: new Date(now + 120_000).toISOString(),
      refreshTokenExpiresAt: new Date(now + 180_000).toISOString(),
    });
    storage.loadSession
      .mockReturnValueOnce(initialSession)
      .mockReturnValue(rotatedSession);

    const { service, http } = setup();
    let emitted: AuthSession | null | undefined;

    service.refreshAccessToken().subscribe((session) => {
      emitted = session;
    });

    const request = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    request.flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(emitted?.accessToken).toBe('rotated-access-token');
    expect(service.getAccessToken()).toBe('rotated-access-token');
    expect(storage.clearSession).not.toHaveBeenCalled();

    http.verify();
  });

  it('adopts a rotated session broadcast from another tab', () => {
    const { service } = setup();
    const externalTab = new FakeBroadcastChannel('intelibill.auth.session');
    const rotatedSession = buildSession({
      accessToken: 'broadcast-access-token',
      refreshToken: 'broadcast-refresh-token',
    });

    externalTab.postMessage({
      type: 'SESSION_UPDATED',
      session: rotatedSession,
    });

    expect(service.getAccessToken()).toBe('broadcast-access-token');
    expect(storage.saveSession).toHaveBeenCalledWith(rotatedSession);
  });

  it('schedules a proactive refresh before the access token expires', () => {
    vi.useFakeTimers();
    const now = Date.now();
    storage.loadSession.mockReturnValue(
      buildSession({
        accessTokenExpiresAt: new Date(now + 90_000).toISOString(),
        refreshTokenExpiresAt: new Date(now + 180_000).toISOString(),
      })
    );

    const { http } = setup();

    vi.advanceTimersByTime(30_000);

    const request = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    expect(request.request.method).toBe('POST');
    request.flush(buildAuthResult({ accessToken: 'proactive-access-token' }));

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

  it('initializeExternalLogin returns authorization url', () => {
    const { service, http } = setup();
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
    const { service, http } = setup();
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
    expect(storage.saveSession).toHaveBeenCalledTimes(1);

    http.verify();
  });

  it('requestPasswordReset posts trimmed email to endpoint', () => {
    const { service, http } = setup();

    service.requestPasswordReset('  user@example.com  ').subscribe();

    const request = http.expectOne(AUTH_ENDPOINTS.requestPasswordReset);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: 'user@example.com' });

    request.flush({});
    http.verify();
  });

  it('requestPasswordReset returns void observable on success', () => {
    const { service, http } = setup();
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
    const { service, http } = setup();

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
    const { service, http } = setup();
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

  it('allows offline sales auth grace when requirements are met', async () => {
    storage.loadSession.mockReturnValue(buildSession({
      activeShopId: 'shop-1',
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Staff', isDefault: true, lastUsedAt: null }],
      refreshTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
    }));
    offlineSalesDeviceSettingsStorage.loadSettings.mockReturnValue({
      shopId: 'shop-1',
      deviceId: 'device-1',
      label: 'Counter 1',
      enabled: true,
      enabledAt: '2026-05-21T00:00:00.000Z',
      enabledByUserId: 'user-1',
      enabledByUserName: 'Test User',
      lastCompleteSnapshotAt: '2026-05-21T00:00:00.000Z',
      lastApiVerifiedAt: new Date(Date.now() - 60_000).toISOString(),
      lastSnapshotWarningMarker: null,
      lastReservedLease: null,
    });
    offlineSalesSnapshotDb.getUsableSnapshotInfo.mockResolvedValue({
      snapshotId: 'snap-1',
      completedAt: '2026-05-21T00:00:00.000Z',
    });

    const { service } = setup();

    await expect(service.canUseOfflineSalesAuthGrace()).resolves.toBe(true);
  });

  it('rejects offline sales auth grace when there is no active shop', async () => {
    storage.loadSession.mockReturnValue(buildSession({ activeShopId: null }));
    const { service } = setup();
    await expect(service.canUseOfflineSalesAuthGrace()).resolves.toBe(false);
  });

  it('rejects offline sales auth grace when refresh token is expired', async () => {
    storage.loadSession.mockReturnValue(buildSession({
      activeShopId: 'shop-1',
      refreshTokenExpiresAt: new Date(Date.now() - 60_000).toISOString(),
    }));
    const { service } = setup();
    await expect(service.canUseOfflineSalesAuthGrace()).resolves.toBe(false);
  });

  it('rejects offline sales auth grace when last API verification is stale', async () => {
    storage.loadSession.mockReturnValue(buildSession({
      activeShopId: 'shop-1',
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Staff', isDefault: true, lastUsedAt: null }],
    }));
    offlineSalesDeviceSettingsStorage.loadSettings.mockReturnValue({
      shopId: 'shop-1',
      deviceId: 'device-1',
      label: 'Counter 1',
      enabled: true,
      enabledAt: '2026-05-21T00:00:00.000Z',
      enabledByUserId: 'user-1',
      enabledByUserName: 'Test User',
      lastCompleteSnapshotAt: '2026-05-21T00:00:00.000Z',
      lastApiVerifiedAt: new Date(Date.now() - (49 * 60 * 60 * 1000)).toISOString(),
      lastSnapshotWarningMarker: null,
      lastReservedLease: null,
    });
    offlineSalesSnapshotDb.getUsableSnapshotInfo.mockResolvedValue({
      snapshotId: 'snap-1',
      completedAt: '2026-05-21T00:00:00.000Z',
    });
    const { service } = setup();
    await expect(service.canUseOfflineSalesAuthGrace()).resolves.toBe(false);
  });

  it('rejects offline sales auth grace when offline device is disabled', async () => {
    storage.loadSession.mockReturnValue(buildSession({
      activeShopId: 'shop-1',
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Staff', isDefault: true, lastUsedAt: null }],
    }));
    offlineSalesDeviceSettingsStorage.loadSettings.mockReturnValue({
      shopId: 'shop-1',
      deviceId: 'device-1',
      label: 'Counter 1',
      enabled: false,
      enabledAt: null,
      enabledByUserId: null,
      enabledByUserName: null,
      lastCompleteSnapshotAt: null,
      lastApiVerifiedAt: null,
      lastSnapshotWarningMarker: null,
      lastReservedLease: null,
    });
    const { service } = setup();
    await expect(service.canUseOfflineSalesAuthGrace()).resolves.toBe(false);
  });

  it('bootstrapSessionWithStatus returns API_UNREACHABLE when refresh fails and API cannot be reached', async () => {
    const now = Date.now();
    storage.loadSession.mockReturnValue(
      buildSession({
        accessTokenExpiresAt: new Date(now - 10_000).toISOString(),
        refreshTokenExpiresAt: new Date(now + 60_000).toISOString(),
      })
    );
    networkStatus.canReachApi.mockReturnValue(false);

    const { service, http } = setup();
    const statusPromise = firstValueFrom(service.bootstrapSessionWithStatus());

    const request = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    request.flush({}, { status: 0, statusText: 'Unknown Error' });

    await expect(statusPromise).resolves.toBe('API_UNREACHABLE');
    expect(networkStatus.checkConnectivity).toHaveBeenCalledTimes(1);
    expect(storage.clearSession).not.toHaveBeenCalled();
    http.verify();
  });

  it('bootstrapSessionWithStatus returns REFRESH_FAILED when refresh is rejected even if ping later fails', async () => {
    const now = Date.now();
    storage.loadSession.mockReturnValue(
      buildSession({
        accessTokenExpiresAt: new Date(now - 10_000).toISOString(),
        refreshTokenExpiresAt: new Date(now + 60_000).toISOString(),
      })
    );
    networkStatus.canReachApi.mockReturnValue(false);

    const { service, http } = setup();
    const statusPromise = firstValueFrom(service.bootstrapSessionWithStatus());

    const request = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    request.flush({}, { status: 401, statusText: 'Unauthorized' });

    await expect(statusPromise).resolves.toBe('REFRESH_FAILED');
    expect(networkStatus.checkConnectivity).not.toHaveBeenCalled();
    expect(storage.clearSession).toHaveBeenCalledTimes(1);
    http.verify();
  });

  it('proactive refresh clears the session for rejected refresh responses without probing connectivity', async () => {
    storage.loadSession.mockReturnValue(
      buildSession({
        accessTokenExpiresAt: new Date(Date.now() + 120_000).toISOString(),
        refreshTokenExpiresAt: new Date(Date.now() + 240_000).toISOString(),
      })
    );
    networkStatus.canReachApi.mockReturnValue(false);
    vi.useFakeTimers();

    const { http } = setup();
    await vi.advanceTimersByTimeAsync(60_001);

    const request = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    request.flush({}, { status: 401, statusText: 'Unauthorized' });
    await vi.runAllTimersAsync();

    expect(storage.clearSession).toHaveBeenCalledTimes(1);
    expect(networkStatus.checkConnectivity).not.toHaveBeenCalled();
    http.verify();
  });

  it('proactive refresh preserves the session for unreachable refresh failures', async () => {
    storage.loadSession.mockReturnValue(
      buildSession({
        accessTokenExpiresAt: new Date(Date.now() + 120_000).toISOString(),
        refreshTokenExpiresAt: new Date(Date.now() + 240_000).toISOString(),
      })
    );
    networkStatus.canReachApi.mockReturnValue(false);
    vi.useFakeTimers();

    const { http } = setup();
    await vi.advanceTimersByTimeAsync(60_001);

    const request = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    request.flush({}, { status: 0, statusText: 'Unknown Error' });
    await vi.runAllTimersAsync();

    expect(storage.clearSession).not.toHaveBeenCalled();
    expect(networkStatus.checkConnectivity).toHaveBeenCalledTimes(1);
    http.verify();
  });

  it('shares API_UNREACHABLE bootstrap status across concurrent callers', async () => {
    const now = Date.now();
    storage.loadSession.mockReturnValue(
      buildSession({
        accessTokenExpiresAt: new Date(now - 10_000).toISOString(),
        refreshTokenExpiresAt: new Date(now + 60_000).toISOString(),
      })
    );
    networkStatus.canReachApi.mockReturnValue(false);

    const { service, http } = setup();
    const firstStatus = firstValueFrom(service.bootstrapSessionWithStatus());
    const secondStatus = firstValueFrom(service.bootstrapSessionWithStatus());

    const request = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    request.flush({}, { status: 0, statusText: 'Unknown Error' });

    await expect(firstStatus).resolves.toBe('API_UNREACHABLE');
    await expect(secondStatus).resolves.toBe('API_UNREACHABLE');
    expect(networkStatus.checkConnectivity).toHaveBeenCalledTimes(1);
    expect(storage.clearSession).not.toHaveBeenCalled();
    http.verify();
  });
});
