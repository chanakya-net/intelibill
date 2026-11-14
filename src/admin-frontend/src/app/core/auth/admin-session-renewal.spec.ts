import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AdminSessionService } from '../../features/auth/admin-session.service';
import { adminSessionInterceptor } from './admin-session.interceptor';

const session = '/api/admin/auth/session';
const invitations = '/api/admin/invitations/context';
const context = '/api/admin/auth/sign-in/context';
const refresh = '/api/admin/auth/refresh';
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
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    Reflect.deleteProperty(navigator, 'locks');
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
    expect(answers).toEqual([{ retried: 'session' }, { retried: 'invitations' }]);
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
    const retried = firstValueFrom(service.check());
    backend.expectOne(session).flush(null, unauthorized);
    await renew(backend, (rotation) => rotation.flush({ accessExpiresAt: new Date().toISOString() }));
    await vi.waitFor(() => backend.expectOne(session).flush(snapshot()));
    expect(await retried).toBe('active');
    expect(service.notice()).toBe('none');
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
    expect(names).toEqual(['intelibill-admin-session-renewal']);
  });
});
