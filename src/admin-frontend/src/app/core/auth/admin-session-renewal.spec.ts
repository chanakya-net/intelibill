import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminSessionService } from '../../features/auth/admin-session.service';
import { restoreAdminBrowserCoordination, stubAdminBrowserCoordination } from './admin-browser-coordination.testing';
import { AdminRefreshCoordinator } from './admin-refresh-coordinator';
import { adminSessionInterceptor } from './admin-session.interceptor';

const session = '/api/admin/auth/session';
const invitations = '/api/admin/invitations/context';
const context = '/api/admin/auth/sign-in/context';
const refresh = '/api/admin/auth/refresh';
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

async function renew(backend: HttpTestingController, answer: (request: ReturnType<HttpTestingController['expectOne']>) => void) {
  await vi.waitFor(() => backend.expectOne(context).flush({ requestToken: 'fixture-csrf' }));
  const rotation = await vi.waitFor(() => backend.expectOne(refresh));
  expect(rotation.request.method).toBe('POST');
  expect(rotation.request.headers.get('X-Admin-CSRF')).toBe('fixture-csrf');
  answer(rotation);
}

async function signIn(backend: HttpTestingController, service: AdminSessionService) {
  const checked = firstValueFrom(service.check());
  backend.expectOne(session).flush(snapshot());
  expect(await checked).toBe('active');
}

describe('admin session renewal', () => {
  beforeEach(stubAdminBrowserCoordination);
  afterEach(() => {
    restoreAdminBrowserCoordination();
    TestBed.inject(HttpTestingController).verify();
  });

  it('renews once for concurrent protected requests and retries each of them', async () => {
    const { http, backend } = setup();
    const answers: unknown[] = [];
    http.get(session).subscribe((value) => answers.push(value));
    http.get(invitations).subscribe((value) => answers.push(value));
    backend.expectOne(session).flush(null, unauthorized);
    backend.expectOne(invitations).flush(null, unauthorized);

    await renew(backend, (rotation) => rotation.flush({ accessExpiresAt: new Date().toISOString() }));
    await vi.waitFor(() => backend.expectOne(session).flush({ retried: 'session' }));
    await vi.waitFor(() => backend.expectOne(invitations).flush({ retried: 'invitations' }));
    await vi.waitFor(() => expect(answers).toEqual([{ retried: 'session' }, { retried: 'invitations' }]));
    backend.expectNone(refresh);
  });

  it('clears protected state when the refresh credential is definitively refused', async () => {
    const { http, backend, session: service } = setup();
    await signIn(backend, service);
    let failure: HttpErrorResponse | undefined;
    http.get(invitations).subscribe({ error: (error: HttpErrorResponse) => (failure = error) });
    backend.expectOne(invitations).flush(null, unauthorized);

    await renew(backend, (rotation) => rotation.flush(null, unauthorized));
    await vi.waitFor(() => expect(failure?.status).toBe(401));
    expect(service.state()).toBe('unauthenticated');
    expect(service.capabilities()).toEqual([]);
    expect(service.notice()).toBe('ended');
    backend.expectNone(invitations);
  });

  it('keeps an ambiguous renewal unknown and never repeats it on its own', async () => {
    const { http, backend, session: service } = setup();
    await signIn(backend, service);
    let failure: HttpErrorResponse | undefined;
    http.get(invitations).subscribe({ error: (error: HttpErrorResponse) => (failure = error) });
    backend.expectOne(invitations).flush(null, unauthorized);

    await renew(backend, (rotation) => rotation.error(new ProgressEvent('error')));
    await vi.waitFor(() => expect(failure?.status).toBe(0));
    expect(service.notice()).toBe('uncertain');
    expect(service.capabilities()).toEqual([]);
    backend.expectNone(refresh);
    backend.expectNone(invitations);

    // Only an explicit retry renews again; this time the server answers.
    const automatic = firstValueFrom(service.check());
    backend.expectOne(session).flush(null, unauthorized);
    await vi.waitFor(() => backend.expectOne(session).flush(null, unauthorized));
    expect(await automatic).toBe('uncertain');
    backend.expectNone(refresh);

    const retried = firstValueFrom(service.check(true));
    backend.expectOne(session).flush(null, unauthorized);
    await vi.waitFor(() => backend.expectOne(session).flush(null, unauthorized));
    await renew(backend, (rotation) => rotation.flush({ accessExpiresAt: new Date().toISOString() }));
    await vi.waitFor(() => backend.expectOne(session).flush(snapshot()));
    expect(await retried).toBe('active');
    expect(service.notice()).toBe('none');
  });

  it('uses a confirmed access cookie after an unknown outcome without rotating again', async () => {
    const { backend, session: service } = setup();
    const first = firstValueFrom(service.check());
    backend.expectOne(session).flush(null, unauthorized);
    await renew(backend, (rotation) => rotation.error(new ProgressEvent('error')));
    expect(await first).toBe('uncertain');

    const confirmed = firstValueFrom(service.check());
    backend.expectOne(session).flush(null, unauthorized);
    await vi.waitFor(() => backend.expectOne(session).flush(snapshot()));
    await vi.waitFor(() => backend.expectOne(session).flush(snapshot()));
    expect(await confirmed).toBe('active');
    expect(service.capabilities()).toEqual(['admin.invitations.manage']);
    backend.expectNone(refresh);
  });

  it('pauses protected writes while a sign-in is replacing the cookies', async () => {
    const { http, backend, session: service } = setup();
    await signIn(backend, service);
    const coordinator = TestBed.inject(AdminRefreshCoordinator);
    const replacement = coordinator.signIn({ email: 'owner@example.test', password: '', otp: '' }, 'csrf');
    const request = backend.expectOne('/api/admin/auth/sign-in');

    const failure = firstValueFrom(http.post('/api/admin/invitations', {})).catch((error: unknown) => error);
    expect(await failure).toMatchObject({ status: 0 });
    expect(service.capabilities()).toEqual([]);
    expect(service.notice()).toBe('uncertain');
    backend.expectNone('/api/admin/invitations');
    request.flush({});
    await replacement;
  });

  it('recovers an abandoned sign-in marker only on explicit recovery', async () => {
    localStorage.setItem(coordinationKey, JSON.stringify({ generation: 'abandoned', state: 'replacing' }));
    const { backend, session: service } = setup();
    expect(await firstValueFrom(service.check())).toBe('uncertain');
    backend.expectNone(session);

    const recovered = firstValueFrom(service.check(true));
    await vi.waitFor(() => backend.expectOne(session).flush(snapshot()));
    expect(await recovered).toBe('active');
    expect(service.capabilities()).toEqual(['admin.invitations.manage']);
    expect(JSON.parse(localStorage.getItem(coordinationKey)!)).toEqual({ generation: 'abandoned', state: 'uncertain' });
    backend.expectNone(refresh);
  });

  it('ends an abandoned sign-in through explicit rotation when the server refuses it', async () => {
    localStorage.setItem(coordinationKey, JSON.stringify({ generation: 'abandoned', state: 'replacing' }));
    const { backend, session: service } = setup();
    const recovered = firstValueFrom(service.check(true));
    await vi.waitFor(() => backend.expectOne(session).flush(null, unauthorized));
    await vi.waitFor(() => backend.expectOne(session).flush(null, unauthorized));
    await renew(backend, (rotation) => rotation.flush(null, unauthorized));
    expect(await recovered).toBe('ended');
    expect(service.notice()).toBe('ended');
    expect(service.capabilities()).toEqual([]);
  });

  it('waits for a sign-in that still holds the lock before explicit recovery', async () => {
    let release!: () => void;
    const held = new Promise<void>((resolve) => (release = resolve));
    const queue: Promise<unknown>[] = [held];
    Object.defineProperty(navigator, 'locks', {
      configurable: true,
      value: {
        request: (_name: string, _options: unknown, task: () => Promise<unknown>) => {
          const run = Promise.all(queue.splice(0)).then(task);
          queue.push(run.catch(() => undefined));
          return run;
        },
      },
    });
    localStorage.setItem(coordinationKey, JSON.stringify({ generation: 'live', state: 'replacing' }));
    const { backend, session: service } = setup();
    let outcome: string | undefined;
    service.check(true).subscribe((value) => (outcome = value));
    await new Promise((resolve) => setTimeout(resolve, 20));
    backend.expectNone(session);
    expect(outcome).toBeUndefined();

    localStorage.setItem(coordinationKey, JSON.stringify({ generation: 'live', state: 'ready' }));
    release();
    await vi.waitFor(() => backend.expectOne(session).flush(snapshot()));
    await vi.waitFor(() => expect(outcome).toBe('active'));
    expect(JSON.parse(localStorage.getItem(coordinationKey)!)).toEqual({ generation: 'live', state: 'ready' });
  });

  it('ends the session when the protected retry is refused', async () => {
    const { http, backend, session: service } = setup();
    await signIn(backend, service);
    let failure: HttpErrorResponse | undefined;
    http.get(invitations).subscribe({ error: (error: HttpErrorResponse) => (failure = error) });
    backend.expectOne(invitations).flush(null, unauthorized);

    await renew(backend, (rotation) => rotation.flush({}));
    await vi.waitFor(() => backend.expectOne(invitations).flush(null, unauthorized));
    await vi.waitFor(() => expect(failure?.status).toBe(401));
    expect(service.notice()).toBe('ended');
    expect(service.capabilities()).toEqual([]);
    backend.expectNone(refresh);
  });

  it.each(['absent', 'rejected'])('hides grants without a renewal request when Web Locks are %s', async (mode) => {
    const { http, backend, session: service } = setup();
    Object.defineProperty(navigator, 'locks', {
      configurable: true,
      value: mode === 'absent' ? undefined : { request: () => Promise.reject(new Error('Lock unavailable')) },
    });
    await signIn(backend, service);
    const failure = firstValueFrom(http.get(invitations)).catch((error: unknown) => error);
    backend.expectOne(invitations).flush(null, unauthorized);

    await vi.waitFor(() => expect(service.notice()).toBe('uncertain'), { timeout: 300 });
    expect(await failure).toMatchObject({ status: 0 });
    expect(service.capabilities()).toEqual([]);
    backend.expectNone(context);
    backend.expectNone(refresh);
  });

  it('treats a server outage during renewal as unknown rather than signed out', async () => {
    const { http, backend, session: service } = setup();
    const checked = firstValueFrom(service.check());
    backend.expectOne(session).flush(null, unauthorized);
    await renew(backend, (rotation) => rotation.flush(null, { status: 503, statusText: 'Unavailable' }));
    expect(await checked).toBe('uncertain');
    expect(service.notice()).toBe('uncertain');
    http.get('/api/admin/auth/bootstrap/enrollment').subscribe({ error: () => undefined });
    backend.expectOne('/api/admin/auth/bootstrap/enrollment').flush(null, unauthorized);
    backend.expectNone(context);
  });

  it('serializes renewal with other tabs through the browser lock', async () => {
    const names: string[] = [];
    Object.defineProperty(navigator, 'locks', {
      configurable: true,
      value: { request: (name: string, _options: unknown, task: () => Promise<unknown>) => (names.push(name), task()) },
    });
    const { http, backend } = setup();
    http.get(session).subscribe();
    backend.expectOne(session).flush(null, unauthorized);
    await renew(backend, (rotation) => rotation.flush({ accessExpiresAt: new Date().toISOString() }));
    await vi.waitFor(() => backend.expectOne(session).flush(snapshot()));
    expect(names).toEqual(['intelibill-admin-session-renewal', 'intelibill-admin-session-renewal']);
  });
});
