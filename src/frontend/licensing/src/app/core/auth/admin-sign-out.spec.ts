import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminSessionService } from '../../features/auth/admin-session.service';
import { restoreAdminBrowserCoordination, stubAdminBrowserCoordination, stubQueuedAdminBrowserLock } from './admin-browser-coordination.testing';
import { AdminRefreshCoordinator } from './admin-refresh-coordinator';
import { adminSessionInterceptor } from './admin-session.interceptor';

const session = '/api/admin/auth/session';
const invitations = '/api/admin/invitations/context';
const context = '/api/admin/auth/sign-in/context';
const refresh = '/api/admin/auth/refresh';
const signOut = '/api/admin/auth/sign-out';
const coordinationKey = 'intelibill.admin.session-coordination.v1';
const unauthorized = { status: 401, statusText: 'Unauthorized' };
const snapshot = () => ({ capabilities: ['admin.invitations.manage'], accessExpiresAt: new Date(Date.now() + 60_000).toISOString() });

function setup() {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(withInterceptors([adminSessionInterceptor])), provideHttpClientTesting()],
  });
  return {
    http: TestBed.inject(HttpClient),
    backend: TestBed.inject(HttpTestingController),
    session: TestBed.inject(AdminSessionService),
  };
}

async function signIn(backend: HttpTestingController, service: AdminSessionService) {
  const checked = firstValueFrom(service.check());
  backend.expectOne(session).flush(snapshot());
  expect(await checked).toBe('active');
}

describe('admin sign-out coordination', () => {
  beforeEach(stubAdminBrowserCoordination);
  afterEach(() => {
    restoreAdminBrowserCoordination();
    TestBed.inject(HttpTestingController).verify();
  });

  it.each([200, 401])('sign-out serializes cookie writers and rejects stale responses after refresh %s', async (status) => {
    stubQueuedAdminBrowserLock();
    const { http, backend, session: service } = setup();
    await signIn(backend, service);
    const delayed = firstValueFrom(http.get(invitations)).catch((error: unknown) => error);
    const stale = backend.expectOne(invitations);
    const renew = firstValueFrom(http.get(session)).catch((error: unknown) => error);
    backend.expectOne(session).flush(null, unauthorized);
    await vi.waitFor(() => backend.expectOne(context).flush({ requestToken: 'csrf' }));
    const rotation = await vi.waitFor(() => backend.expectOne(refresh));

    const ending = service.signOut();
    expect(service.capabilities()).toEqual([]);
    expect(service.state()).toBe('unauthenticated');
    backend.expectNone(signOut);
    expect(await firstValueFrom(service.check(true))).toBe('ended');

    stale.flush({ shouldBeDiscarded: true });
    expect(await delayed).toMatchObject({ status: 401 });
    rotation.flush({}, status === 401 ? unauthorized : undefined);
    expect(await renew).toMatchObject({ status: 401 });
    await vi.waitFor(() => backend.expectOne(context).flush({ requestToken: 'csrf' }));
    const endRequest = await vi.waitFor(() => backend.expectOne(signOut));
    expect(endRequest.request.headers.get('X-Admin-CSRF')).toBe('csrf');
    expect(service.capabilities()).toEqual([]);
    endRequest.flush(null, { status: 204, statusText: 'No Content' });
    await ending;
    expect(await firstValueFrom(service.check(true))).toBe('ended');
    backend.expectNone(refresh);
  });

  it('finishes an unconfirmed sign-out before a fresh sign-in and ignores stale tab intent', async () => {
    stubQueuedAdminBrowserLock();
    localStorage.setItem(coordinationKey, JSON.stringify({ generation: 'old', state: 'ready' }));
    const { backend, session: service } = setup();
    await signIn(backend, service);
    const end = service.signOut();
    await vi.waitFor(() => backend.expectOne(context).flush({ requestToken: 'csrf' }));
    const first = await vi.waitFor(() => backend.expectOne(signOut));
    const coordinator = TestBed.inject(AdminRefreshCoordinator);
    const replacing = coordinator.signIn({ email: 'owner@example.test', password: '', otp: '' }, 'csrf');
    backend.expectNone('/api/admin/auth/sign-in');
    first.error(new ProgressEvent('error'));
    await end;
    expect(service.signOutNotice()).toBe('unconfirmed');
    expect(await firstValueFrom(service.check(true))).toBe('ended');
    await vi.waitFor(() => backend.expectOne(context).flush({ requestToken: 'csrf' }));
    await vi.waitFor(() => backend.expectOne(signOut).flush(null, { status: 204, statusText: 'No Content' }));
    await vi.waitFor(() => backend.expectOne('/api/admin/auth/sign-in').flush({}));
    await replacing;
    await signIn(backend, service);

    const key = coordinationKey + '.sign-out.old';
    localStorage.setItem(key, '1');
    window.dispatchEvent(new StorageEvent('storage', { key, newValue: '1' }));
    await service.retrySignOut();
    expect(service.signOutNotice()).toBeNull();
    expect(service.state()).toBe('authenticated');
    backend.expectNone(signOut);
  });

  it('recovers after reload with an abandoned end intent through explicit sign-out only', async () => {
    localStorage.setItem(coordinationKey, JSON.stringify({ generation: 'abandoned', state: 'pending' }));
    localStorage.setItem(coordinationKey + '.sign-out.abandoned', '1');
    const { http, backend, session: service } = setup();
    expect(service.signOutNotice()).toBe('unconfirmed');
    expect(await firstValueFrom(service.check(true))).toBe('ended');
    await expect(firstValueFrom(http.get(invitations))).rejects.toMatchObject({ status: 401 });
    backend.expectNone(session);
    backend.expectNone(invitations);
    backend.expectNone(refresh);

    const ending = service.retrySignOut();
    backend.expectOne(context).flush({ requestToken: 'csrf' });
    await vi.waitFor(() => backend.expectOne(signOut).flush(null, { status: 204, statusText: 'No Content' }));
    await ending;
    expect(service.signOutNotice()).toBe('signed-out');
  });

  it('ends local access when Web Locks are unavailable and keeps the intent retryable', async () => {
    Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined });
    const { backend, session: service } = setup();
    await signIn(backend, service);
    await service.signOut();
    expect(service.signOutNotice()).toBe('unconfirmed');
    expect(service.capabilities()).toEqual([]);
    expect(await firstValueFrom(service.check(true))).toBe('ended');
    backend.expectNone(context);
    backend.expectNone(signOut);

    stubQueuedAdminBrowserLock();
    const ending = service.retrySignOut();
    await vi.waitFor(() => backend.expectOne(context).flush({ requestToken: 'csrf' }));
    await vi.waitFor(() => backend.expectOne(signOut).flush(null, { status: 204, statusText: 'No Content' }));
    await ending;
    expect(service.signOutNotice()).toBe('signed-out');
  });

});
