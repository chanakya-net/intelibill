import { afterEach, describe, expect, it, vi } from 'vitest';
import { createEnvironmentInjector, EnvironmentInjector } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';

import { AUTH_ENDPOINTS } from './auth.constants';
import { AuthResult, AuthSession, RefreshTokenRequest } from './auth.models';
import { AuthStorage } from './auth.storage';
import { AuthSessionState, AuthTokenService, REFRESH_REQUEST_TIMEOUT_MS } from './auth-token.service';
import {
  createAuthServiceTestHarness,
  buildAuthResult,
  buildSession,
  FakeBroadcastChannel,
  FakeLockManager,
} from './auth.service.test-utils';
import { REFRESH_LEASE_STORAGE, REFRESH_LOCK_MANAGER } from './refresh-coordinator.service';
import { NetworkStatusService } from '../services/network-status.service';

describe('AuthService (token flows)', () => {
  const t = createAuthServiceTestHarness();

  it('refreshes access token when refresh token is valid', () => {
    const now = Date.now();
    t.storage.loadSession.mockReturnValue(
      buildSession({
        accessTokenExpiresAt: new Date(now - 10_000).toISOString(),
        refreshTokenExpiresAt: new Date(now + 60_000).toISOString(),
      })
    );
    const { service, http } = t.setup();
    let emitted: AuthSession | null | undefined;

    service.refreshAccessToken().subscribe((session) => {
      emitted = session;
    });

    const request = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    expect(request.request.method).toBe('POST');
    // The shop the expiring token was scoped to travels with the refresh, so renewal stays in
    // that shop rather than wandering to whichever one was used most recently.
    expect(request.request.body).toEqual({
      refreshToken: 'refresh-token',
      activeShopId: (request.request.body as { activeShopId: string | null }).activeShopId,
    });
    expect(request.request.body).toHaveProperty('activeShopId');
    request.flush(buildAuthResult({ accessToken: 'new-access-token' }));

    expect(emitted?.accessToken).toBe('new-access-token');
    expect(service.getAccessToken()).toBe('new-access-token');
    expect(t.storage.saveSession).toHaveBeenCalledTimes(1);

    http.verify();
  });

  it('adopts a successor already stored for this session instead of presenting the credential it replaced', () => {
    const now = Date.now();
    const initialSession = buildSession({
      sessionId: 'session-1',
      accessTokenExpiresAt: new Date(now - 10_000).toISOString(),
      refreshTokenExpiresAt: new Date(now + 60_000).toISOString(),
    });
    const rotatedSession = buildSession({
      sessionId: 'session-1',
      accessToken: 'rotated-access-token',
      refreshToken: 'rotated-refresh-token',
      accessTokenExpiresAt: new Date(now + 120_000).toISOString(),
      refreshTokenExpiresAt: new Date(now + 180_000).toISOString(),
    });
    t.storage.loadSession.mockReturnValueOnce(initialSession).mockReturnValue(rotatedSession);

    const { service, http } = t.setup();
    let emitted: AuthSession | null | undefined;

    service.refreshAccessToken().subscribe((session) => {
      emitted = session;
    });

    // Presenting 'refresh-token' now would be a replay of a spent credential and would end the
    // session for every tab.
    http.expectNone(AUTH_ENDPOINTS.refreshToken);
    expect(emitted?.accessToken).toBe('rotated-access-token');
    expect(service.getAccessToken()).toBe('rotated-access-token');
    expect(t.storage.clearSession).not.toHaveBeenCalled();
    http.verify();
  });

  it('rejectedRefreshIsAuthoritative: a refused credential is not rescued by a newer stored session', () => {
    const now = Date.now();
    const initialSession = buildSession({
      sessionId: 'session-1',
      accessTokenExpiresAt: new Date(now - 10_000).toISOString(),
      refreshTokenExpiresAt: new Date(now + 60_000).toISOString(),
    });
    const laterSession = buildSession({
      sessionId: 'session-1',
      accessToken: 'later-access-token',
      refreshToken: 'later-refresh-token',
      refreshTokenExpiresAt: new Date(now + 180_000).toISOString(),
    });
    t.storage.loadSession
      .mockReturnValueOnce(initialSession)
      .mockReturnValueOnce(initialSession)
      .mockReturnValue(laterSession);

    const { service, http } = t.setup();
    let failure: unknown;

    service.refreshAccessToken().subscribe({ error: (error: unknown) => (failure = error) });

    http.expectOne(AUTH_ENDPOINTS.refreshToken).flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(failure).toBeDefined();
    expect(service.session()).toBeNull();
    expect(t.storage.clearSession).toHaveBeenCalled();
    http.verify();
  });

  it('lostRefreshResponseRequiresReauthentication: a renewal whose answer never arrived ends in sign-in, not a retry that works', async () => {
    const now = Date.now();
    t.storage.loadSession.mockReturnValue(
      buildSession({
        sessionId: 'session-1',
        accessTokenExpiresAt: new Date(now - 10_000).toISOString(),
        refreshTokenExpiresAt: new Date(now + 60_000).toISOString(),
      })
    );
    t.networkStatus.canReachApi.mockReturnValue(false);

    const { service, http } = t.setup();

    // The server exchanged the credential, but the response was lost on the way back. With the API
    // unreachable the session is kept for offline work, still holding the spent credential.
    const offline = firstValueFrom(service.bootstrapSessionWithStatus());
    const lost = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    expect((lost.request.body as RefreshTokenRequest).refreshToken).toBe('refresh-token');
    lost.flush({}, { status: 0, statusText: 'Unknown Error' });
    await expect(offline).resolves.toBe('API_UNREACHABLE');
    expect(service.session()).not.toBeNull();

    // Back online, the only credential the browser has is the spent one. The server refuses it as a
    // replay, and the browser asks for a sign-in rather than finding something to retry with.
    t.networkStatus.canReachApi.mockReturnValue(true);
    const online = firstValueFrom(service.bootstrapSessionWithStatus());
    const retry = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    expect((retry.request.body as RefreshTokenRequest).refreshToken).toBe('refresh-token');
    retry.flush({}, { status: 401, statusText: 'Unauthorized' });

    await expect(online).resolves.toBe('REFRESH_FAILED');
    expect(service.session()).toBeNull();
    http.verify();
  });

  it('treats a renewal that never answers as a lost connection, so the lock is not held forever', async () => {
    vi.useFakeTimers();
    const now = Date.now();
    t.storage.loadSession.mockReturnValue(
      buildSession({
        sessionId: 'session-1',
        accessTokenExpiresAt: new Date(now - 10_000).toISOString(),
        refreshTokenExpiresAt: new Date(now + 600_000).toISOString(),
      })
    );
    t.networkStatus.canReachApi.mockReturnValue(false);
    const { service, http } = t.setup();
    let failure: unknown;

    service.refreshAccessToken({ preserveSessionOnError: true }).subscribe({ error: (error: unknown) => (failure = error) });
    const hung = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    await vi.advanceTimersByTimeAsync(REFRESH_REQUEST_TIMEOUT_MS);

    expect(hung.cancelled).toBe(true);
    expect(failure).toBeInstanceOf(HttpErrorResponse);
    expect((failure as HttpErrorResponse).status).toBe(0);
    expect(service.session()).not.toBeNull();
  });

  it('adopts a rotated session broadcast from another tab', () => {
    const { service } = t.setup();
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
    expect(t.storage.saveSession).toHaveBeenCalledWith(rotatedSession);
  });

  it('schedules a proactive refresh before the access token expires', () => {
    vi.useFakeTimers();
    const now = Date.now();
    t.storage.loadSession.mockReturnValue(
      buildSession({
        accessTokenExpiresAt: new Date(now + 90_000).toISOString(),
        refreshTokenExpiresAt: new Date(now + 180_000).toISOString(),
      })
    );

    const { http } = t.setup();

    vi.advanceTimersByTime(30_000);

    const request = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    expect(request.request.method).toBe('POST');
    request.flush(buildAuthResult({ accessToken: 'proactive-access-token' }));

    http.verify();
  });

  it('bootstrapSession clears expired refresh token and returns false', () => {
    const now = Date.now();
    t.storage.loadSession.mockReturnValue(
      buildSession({
        accessTokenExpiresAt: new Date(now - 10_000).toISOString(),
        refreshTokenExpiresAt: new Date(now - 1_000).toISOString(),
      })
    );
    const { service } = t.setup();
    let emitted: boolean | undefined;

    service.bootstrapSession().subscribe((value) => {
      emitted = value;
    });

    expect(emitted).toBe(false);
    expect(t.storage.clearSession).toHaveBeenCalledTimes(1);
    expect(service.session()).toBeNull();
  });

  it('bootstrapSessionWithStatus returns API_UNREACHABLE when refresh fails and API cannot be reached', async () => {
    const now = Date.now();
    t.storage.loadSession.mockReturnValue(
      buildSession({
        accessTokenExpiresAt: new Date(now - 10_000).toISOString(),
        refreshTokenExpiresAt: new Date(now + 60_000).toISOString(),
      })
    );
    t.networkStatus.canReachApi.mockReturnValue(false);

    const { service, http } = t.setup();
    const statusPromise = firstValueFrom(service.bootstrapSessionWithStatus());

    const request = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    request.flush({}, { status: 0, statusText: 'Unknown Error' });

    await expect(statusPromise).resolves.toBe('API_UNREACHABLE');
    expect(t.networkStatus.checkConnectivity).toHaveBeenCalledTimes(1);
    expect(t.storage.clearSession).not.toHaveBeenCalled();
    http.verify();
  });

  it('bootstrapSessionWithStatus returns REFRESH_FAILED when refresh is rejected even if ping later fails', async () => {
    const now = Date.now();
    t.storage.loadSession.mockReturnValue(
      buildSession({
        accessTokenExpiresAt: new Date(now - 10_000).toISOString(),
        refreshTokenExpiresAt: new Date(now + 60_000).toISOString(),
      })
    );
    t.networkStatus.canReachApi.mockReturnValue(false);

    const { service, http } = t.setup();
    const statusPromise = firstValueFrom(service.bootstrapSessionWithStatus());

    const request = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    request.flush({}, { status: 401, statusText: 'Unauthorized' });

    await expect(statusPromise).resolves.toBe('REFRESH_FAILED');
    expect(t.networkStatus.checkConnectivity).not.toHaveBeenCalled();
    expect(t.storage.clearSession).toHaveBeenCalledTimes(1);
    http.verify();
  });

  it('proactive refresh clears the session for rejected refresh responses without probing connectivity', async () => {
    t.storage.loadSession.mockReturnValue(
      buildSession({
        accessTokenExpiresAt: new Date(Date.now() + 120_000).toISOString(),
        refreshTokenExpiresAt: new Date(Date.now() + 240_000).toISOString(),
      })
    );
    t.networkStatus.canReachApi.mockReturnValue(false);
    vi.useFakeTimers();

    const { http } = t.setup();
    await vi.advanceTimersByTimeAsync(60_001);

    const request = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    request.flush({}, { status: 401, statusText: 'Unauthorized' });
    await vi.runAllTimersAsync();

    expect(t.storage.clearSession).toHaveBeenCalledTimes(1);
    expect(t.networkStatus.checkConnectivity).not.toHaveBeenCalled();
    http.verify();
  });

  it('proactive refresh preserves the session for unreachable refresh failures', async () => {
    t.storage.loadSession.mockReturnValue(
      buildSession({
        accessTokenExpiresAt: new Date(Date.now() + 120_000).toISOString(),
        refreshTokenExpiresAt: new Date(Date.now() + 240_000).toISOString(),
      })
    );
    t.networkStatus.canReachApi.mockReturnValue(false);
    vi.useFakeTimers();

    const { http } = t.setup();
    await vi.advanceTimersByTimeAsync(60_001);

    const request = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    request.flush({}, { status: 0, statusText: 'Unknown Error' });
    await vi.runAllTimersAsync();

    expect(t.storage.clearSession).not.toHaveBeenCalled();
    expect(t.networkStatus.checkConnectivity).toHaveBeenCalledTimes(1);
    http.verify();
  });

  it('shares API_UNREACHABLE bootstrap status across concurrent callers', async () => {
    const now = Date.now();
    t.storage.loadSession.mockReturnValue(
      buildSession({
        accessTokenExpiresAt: new Date(now - 10_000).toISOString(),
        refreshTokenExpiresAt: new Date(now + 60_000).toISOString(),
      })
    );
    t.networkStatus.canReachApi.mockReturnValue(false);

    const { service, http } = t.setup();
    const firstStatus = firstValueFrom(service.bootstrapSessionWithStatus());
    const secondStatus = firstValueFrom(service.bootstrapSessionWithStatus());

    const request = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    request.flush({}, { status: 0, statusText: 'Unknown Error' });

    await expect(firstStatus).resolves.toBe('API_UNREACHABLE');
    await expect(secondStatus).resolves.toBe('API_UNREACHABLE');
    expect(t.networkStatus.checkConnectivity).toHaveBeenCalledTimes(1);
    expect(t.storage.clearSession).not.toHaveBeenCalled();
    http.verify();
  });
});

/**
 * Two tabs of one browser, each with its own AuthTokenService, sharing storage and the browser's
 * lock manager the way real tabs do.
 */
describe('AuthTokenService across tabs', () => {
  let stored: AuthSession | null = null;
  const storage = {
    loadSession: (): AuthSession | null => stored,
    saveSession: (session: AuthSession): void => {
      stored = session;
    },
    clearSession: (): void => {
      stored = null;
    },
  };
  const networkStatus = {
    canReachApi: vi.fn<NetworkStatusService['canReachApi']>().mockReturnValue(true),
    checkConnectivity: vi.fn<NetworkStatusService['checkConnectivity']>().mockResolvedValue(undefined),
  };

  afterEach(() => {
    stored = null;
    TestBed.resetTestingModule();
  });

  function setupTabs(): { tabA: AuthTokenService; tabB: AuthTokenService; http: HttpTestingController } {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthStorage, useValue: storage },
        { provide: NetworkStatusService, useValue: networkStatus },
        { provide: REFRESH_LOCK_MANAGER, useValue: new FakeLockManager() },
        { provide: REFRESH_LEASE_STORAGE, useValue: null },
      ],
    });
    const browser = TestBed.inject(EnvironmentInjector);
    const openTab = (): AuthTokenService =>
      createEnvironmentInjector([AuthTokenService], browser).get(AuthTokenService);

    return { tabA: openTab(), tabB: openTab(), http: TestBed.inject(HttpTestingController) };
  }

  /** One tab's in-memory session; saving writes through to the shared storage. */
  function tabState(initial: AuthSession | null): AuthSessionState & { current: AuthSession | null } {
    const state = {
      current: initial,
      getSession: (): AuthSession | null => state.current,
      setSession: (session: AuthSession): void => {
        state.current = session;
        storage.saveSession(session);
      },
      clearSession: (): void => {
        state.current = null;
        storage.clearSession();
      },
      toSession: (result: AuthResult, rememberMe: boolean): AuthSession => ({
        ...result,
        rememberMe,
        shops: result.shops ?? [],
        sessionId: result.sessionId ?? null,
      }),
    };
    return state;
  }

  function expiringSession(overrides?: Partial<AuthSession>): AuthSession {
    const now = Date.now();
    return buildSession({
      sessionId: 'session-1',
      refreshToken: 'refresh-1',
      accessTokenExpiresAt: new Date(now - 10_000).toISOString(),
      refreshTokenExpiresAt: new Date(now + 60_000).toISOString(),
      ...overrides,
    });
  }

  function renewal(refreshToken: string): AuthResult {
    return buildAuthResult({
      sessionId: 'session-1',
      accessToken: `access-for-${refreshToken}`,
      refreshToken,
      refreshTokenExpiresAt: new Date(Date.now() + 180_000).toISOString(),
    });
  }

  it('twoTabsRotateOnce: two tabs renewing one session together send a single rotation', async () => {
    const session = expiringSession();
    stored = session;
    const { tabA, tabB, http } = setupTabs();
    const stateA = tabState(session);
    const stateB = tabState(session);

    const renewedA = firstValueFrom(tabA.refreshAccessToken(stateA));
    const renewedB = firstValueFrom(tabB.refreshAccessToken(stateB));

    const rotation = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    expect((rotation.request.body as RefreshTokenRequest).refreshToken).toBe('refresh-1');
    rotation.flush(renewal('refresh-2'));

    await expect(renewedA).resolves.toMatchObject({ refreshToken: 'refresh-2' });
    await expect(renewedB).resolves.toMatchObject({ refreshToken: 'refresh-2' });
    http.expectNone(AUTH_ENDPOINTS.refreshToken);
    expect(stateB.current?.refreshToken).toBe('refresh-2');
    http.verify();
  });

  it('waiterUsesStoredSuccessor: a tab that missed the broadcast adopts the stored successor and never presents the spent credential', async () => {
    const session = expiringSession();
    stored = session;
    const { tabA, tabB, http } = setupTabs();
    const stateA = tabState(session);
    const stateB = tabState(session);

    const renewedA = firstValueFrom(tabA.refreshAccessToken(stateA));
    http.expectOne(AUTH_ENDPOINTS.refreshToken).flush(renewal('refresh-2'));
    await renewedA;

    // Tab B still holds 'refresh-1' in memory: the broadcast never reached it.
    expect(stateB.current?.refreshToken).toBe('refresh-1');
    const renewedB = await firstValueFrom(tabB.refreshAccessToken(stateB));

    http.expectNone(AUTH_ENDPOINTS.refreshToken);
    expect(renewedB?.refreshToken).toBe('refresh-2');
    expect(stateB.current?.accessToken).toBe('access-for-refresh-2');
    http.verify();
  });

  it("waiterIgnoresAnotherUsersStoredSession: a newer session for a different user is never adopted", async () => {
    const mine = expiringSession();
    stored = buildSession({
      user: { ...mine.user, id: 'user-2', email: 'someone-else@example.com' },
      sessionId: 'session-9',
      refreshToken: 'their-refresh',
      refreshTokenExpiresAt: new Date(Date.now() + 600_000).toISOString(),
    });
    const { tabB, http } = setupTabs();
    const stateB = tabState(mine);

    const renewed = firstValueFrom(tabB.refreshAccessToken(stateB));

    const rotation = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    expect((rotation.request.body as RefreshTokenRequest).refreshToken).toBe('refresh-1');
    rotation.flush(renewal('refresh-2'));

    await expect(renewed).resolves.toMatchObject({ refreshToken: 'refresh-2', user: { id: 'user-1' } });
    http.verify();
  });
  it('serializes a tab restored from storage without a session id against one that knows it', async () => {
    const known = expiringSession();
    const restored = expiringSession({ sessionId: null });
    stored = known;
    const { tabA, tabB, http } = setupTabs();
    const stateA = tabState(known);
    const stateB = tabState(restored);

    const renewedA = firstValueFrom(tabA.refreshAccessToken(stateA));
    const renewedB = firstValueFrom(tabB.refreshAccessToken(stateB));

    http.expectOne(AUTH_ENDPOINTS.refreshToken).flush(renewal('refresh-2'));

    await expect(renewedA).resolves.toMatchObject({ refreshToken: 'refresh-2' });
    await expect(renewedB).resolves.toMatchObject({ refreshToken: 'refresh-2' });
    http.expectNone(AUTH_ENDPOINTS.refreshToken);
    http.verify();
  });

  it('does not bring a session back when the renewal answers after sign-out', async () => {
    const session = expiringSession();
    stored = session;
    const { tabA, http } = setupTabs();
    const stateA = tabState(session);

    const renewed = firstValueFrom(tabA.refreshAccessToken(stateA));
    const rotation = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    stateA.clearSession();
    rotation.flush(renewal('refresh-2'));

    await expect(renewed).resolves.toBeNull();
    expect(stateA.current).toBeNull();
    expect(stored).toBeNull();
  });

  it("never clears or overwrites another account's session with a late answer for the previous one", async () => {
    const previous = expiringSession();
    const next = buildSession({
      user: { ...previous.user, id: 'user-2', email: 'next@example.com' },
      sessionId: 'session-9',
      refreshToken: 'their-refresh',
    });
    stored = previous;
    const { tabA, http } = setupTabs();
    const stateA = tabState(previous);

    const refused = firstValueFrom(tabA.refreshAccessToken(stateA));
    const rotation = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    stateA.setSession(next);
    rotation.flush({}, { status: 401, statusText: 'Unauthorized' });

    await expect(refused).resolves.toBe(next);
    expect(stateA.current).toBe(next);
    expect(stored).toBe(next);
  });
});
