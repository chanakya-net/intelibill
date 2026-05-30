import { describe, expect, it, vi } from 'vitest';
import { firstValueFrom } from 'rxjs';

import { AUTH_ENDPOINTS } from './auth.constants';
import { AuthSession } from './auth.models';
import { createAuthServiceTestHarness, buildAuthResult, buildSession, FakeBroadcastChannel } from './auth.service.test-utils';

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
    expect(request.request.body).toEqual({ refreshToken: 'refresh-token' });
    request.flush(buildAuthResult({ accessToken: 'new-access-token' }));

    expect(emitted?.accessToken).toBe('new-access-token');
    expect(service.getAccessToken()).toBe('new-access-token');
    expect(t.storage.saveSession).toHaveBeenCalledTimes(1);

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
    t.storage.loadSession.mockReturnValueOnce(initialSession).mockReturnValue(rotatedSession);

    const { service, http } = t.setup();
    let emitted: AuthSession | null | undefined;

    service.refreshAccessToken().subscribe((session) => {
      emitted = session;
    });

    const request = http.expectOne(AUTH_ENDPOINTS.refreshToken);
    request.flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(emitted?.accessToken).toBe('rotated-access-token');
    expect(service.getAccessToken()).toBe('rotated-access-token');
    expect(t.storage.clearSession).not.toHaveBeenCalled();

    http.verify();
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
