import { Location } from '@angular/common';
import { provideLocationMocks } from '@angular/common/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { RouterTestingHarness } from '@angular/router/testing';
import { firstValueFrom } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { restoreAdminBrowserCoordination, stubAdminBrowserCoordination } from '../../core/auth/admin-browser-coordination.testing';
import { appConfig } from '../../app.config';
import { AdminSessionService } from '../auth/admin-session.service';

const token = 'i1_' + 'A'.repeat(43) + 'b'.repeat(43);

async function open(url: string) {
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(url);
  return harness;
}

function fill(page: HTMLElement, label: string, value: string) {
  const input = page.querySelector<HTMLInputElement>(`#${label}`)!;
  input.value = value;
}

describe('administrator invitations', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    stubAdminBrowserCoordination();
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting(), provideLocationMocks()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    restoreAdminBrowserCoordination();
    http.verify();
  });

  it('moves the link token out of the address bar and only sends it in the acceptance body', async () => {
    const harness = await open('/auth/invitation#token=' + token);
    expect(TestBed.inject(Location).path(true)).toBe('/auth/invitation');
    http.expectOne('/api/admin/auth/invitation/context').flush({ available: true, requestToken: 'request-token' });
    harness.detectChanges();

    const page: HTMLElement = harness.routeNativeElement!;
    fill(page, 'invitation-email', ' invitee@example.test ');
    fill(page, 'invitation-password', 'Fixture-password-123');
    page.querySelector('form')!.dispatchEvent(new Event('submit'));
    const accept = http.expectOne('/api/admin/auth/invitation/accept');
    expect(accept.request.headers.get('X-Bootstrap-CSRF')).toBe('request-token');
    expect(accept.request.body).toEqual({
      invitationToken: token,
      email: 'invitee@example.test',
      password: 'Fixture-password-123',
    });
    expect(page.querySelector<HTMLInputElement>('#invitation-password')!.value).toBe('');
    accept.flush({ state: 'enrollmentRequired', expiresAt: '2026-11-14T12:15:00Z' });
    harness.detectChanges();

    expect(page.querySelector('h2')?.textContent).toContain('MFA setup required');
    expect(page.textContent).toContain('No provider permissions');
    expect(page.querySelector('input')).toBeNull();
  });

  it('offers no signup when no invitation link or pending enrollment is present', async () => {
    const harness = await open('/auth/invitation');
    http.expectOne('/api/admin/auth/bootstrap/enrollment').flush(null, { status: 401, statusText: 'Unauthorized' });
    harness.detectChanges();

    const page: HTMLElement = harness.routeNativeElement!;
    expect(page.querySelector('form')).toBeNull();
    expect(page.textContent).toContain('Invitations are the only way to join.');
  });

  it('retires a used or expired link in the page', async () => {
    const harness = await open('/auth/invitation#token=' + token);
    http.expectOne('/api/admin/auth/invitation/context').flush({ available: true, requestToken: 'request-token' });
    harness.detectChanges();
    const page: HTMLElement = harness.routeNativeElement!;
    fill(page, 'invitation-email', 'invitee@example.test');
    fill(page, 'invitation-password', 'Fixture-password-123');
    page.querySelector('form')!.dispatchEvent(new Event('submit'));
    http.expectOne('/api/admin/auth/invitation/accept').flush(
      { code: 'Invitation.Unavailable', status: 409 },
      { status: 409, statusText: 'Conflict' },
    );
    harness.detectChanges();

    expect(page.querySelector('form')).toBeNull();
    expect(page.querySelector('[role="alert"]')?.textContent).toContain('expired or was already used');
  });

  it('refuses the invitation form without a provider session', async () => {
    const harness = await open('/admin-users/invite');
    http.expectOne('/api/admin/invitations/context').flush(null, { status: 401, statusText: 'Unauthorized' });
    // The expired access token is renewed once; a refused refresh credential ends the session.
    await vi.waitFor(() => http.expectOne('/api/admin/auth/sign-in/context').flush({ requestToken: 'csrf' }));
    await vi.waitFor(() => http.expectOne('/api/admin/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' }));
    const page: HTMLElement = harness.routeNativeElement!;
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(page.querySelector('[role="alert"]')?.textContent).toContain('Sign in with your provider account');
    });
    expect(page.querySelector('form')).toBeNull();
  });

  it('discards the authorized form after an uncertain submit and rechecks only on explicit recovery', async () => {
    const service = TestBed.inject(AdminSessionService);
    const session = '/api/admin/auth/session';
    const context = '/api/admin/invitations/context';
    const signInContext = '/api/admin/auth/sign-in/context';
    const refresh = '/api/admin/auth/refresh';
    const snapshot = { capabilities: ['admin.invitations.manage'], accessExpiresAt: new Date(Date.now() + 60_000).toISOString() };
    const unauthorized = { status: 401, statusText: 'Unauthorized' };
    const checking = firstValueFrom(service.check());
    http.expectOne(session).flush(snapshot);
    await checking;
    const harness = await open('/admin-users/invite');
    http.expectOne(context).flush({ requestToken: 'old-proof' });
    harness.detectChanges();

    const page: HTMLElement = harness.routeNativeElement!;
    fill(page, 'invite-email', 'invitee@example.test');
    const discardedForm = page.querySelector('form')!;
    discardedForm.dispatchEvent(new Event('submit'));
    http.expectOne('/api/admin/invitations').flush(null, unauthorized);
    await vi.waitFor(() => http.expectOne(signInContext).flush({ requestToken: 'csrf' }));
    await vi.waitFor(() => http.expectOne(refresh).error(new ProgressEvent('error')));
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(page.querySelector('form')).toBeNull();
    });
    expect(service.capabilities()).toEqual([]);
    discardedForm.dispatchEvent(new Event('submit'));
    http.expectNone('/api/admin/invitations');
    http.expectNone(refresh);

    const recovery = Array.from(page.querySelectorAll('button')).find((button) => button.textContent?.trim() === 'Try again')!;
    recovery.click();
    http.expectOne(session).flush(null, unauthorized);
    await vi.waitFor(() => http.expectOne(session).flush(null, unauthorized));
    await vi.waitFor(() => http.expectOne(signInContext).flush({ requestToken: 'new-csrf' }));
    await vi.waitFor(() => http.expectOne(refresh).flush({}));
    await vi.waitFor(() => http.expectOne(session).flush(snapshot));
    await vi.waitFor(() => http.expectOne(context).flush({ requestToken: 'fresh-proof' }));
    harness.detectChanges();

    expect(page.querySelector<HTMLInputElement>('#invite-email')?.value).toBe('');
    fill(page, 'invite-email', 'invitee@example.test');
    page.querySelector('form')!.dispatchEvent(new Event('submit'));
    const invite = http.expectOne('/api/admin/invitations');
    expect(invite.request.headers.get('X-Bootstrap-CSRF')).toBe('fresh-proof');
    invite.flush({ invitationId: 'id', expiresAt: '2026-11-17T12:00:00Z' });
  });

  it('sends an invitation with the request token and confirms delivery', async () => {
    const harness = await open('/admin-users/invite');
    http.expectOne('/api/admin/invitations/context').flush({ requestToken: 'owner-token' });
    harness.detectChanges();

    const page: HTMLElement = harness.routeNativeElement!;
    fill(page, 'invite-email', 'invitee@example.test');
    page.querySelector('form')!.dispatchEvent(new Event('submit'));
    const invite = http.expectOne('/api/admin/invitations');
    expect(invite.request.headers.get('X-Bootstrap-CSRF')).toBe('owner-token');
    expect(invite.request.body).toEqual({ email: 'invitee@example.test' });
    invite.flush({ invitationId: 'id', expiresAt: '2026-11-17T12:00:00Z' }, { status: 201, statusText: 'Created' });
    harness.detectChanges();

    expect(page.querySelector('h2')?.textContent).toContain('Invitation sent');
    expect(page.textContent).toContain('invitee@example.test');
  });
});
