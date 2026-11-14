import { Location } from '@angular/common';
import { provideLocationMocks } from '@angular/common/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { RouterTestingHarness } from '@angular/router/testing';
import { describe, expect, it, vi } from 'vitest';
import { appConfig } from '../../app.config';

const context = '/api/admin/auth/sign-in/context';
const signIn = '/api/admin/auth/sign-in';
const session = '/api/admin/auth/session';

describe('provider sign-in', () => {
  it('redirects the protected dashboard and renders only capabilities from the authenticated API', async () => {
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting(), provideLocationMocks()],
    });
    const http = TestBed.inject(HttpTestingController);
    const harness = await RouterTestingHarness.create();
    const navigation = harness.navigateByUrl('/');
    await vi.waitFor(() => http.expectOne(session).flush(null, { status: 401, statusText: 'Unauthorized' }));
    // Without a live refresh credential the renewal is refused and the dashboard redirects.
    await vi.waitFor(() => http.expectOne(context).flush({ requestToken: 'renewal-csrf' }));
    await vi.waitFor(() => http.expectOne('/api/admin/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' }));
    await navigation;
    expect(TestBed.inject(Location).path()).toBe('/auth/sign-in');
    http.expectOne(context).flush({ requestToken: 'fixture-csrf' });
    harness.detectChanges();

    const page: HTMLElement = harness.routeNativeElement!;
    const password = page.querySelector<HTMLInputElement>('#sign-in-password')!;
    const otp = page.querySelector<HTMLInputElement>('#sign-in-otp')!;
    page.querySelector<HTMLInputElement>('#sign-in-email')!.value = 'owner@example.test';
    password.value = 'Fixture-password-123';
    otp.value = '012345';
    page.querySelector('form')!.dispatchEvent(new Event('submit'));
    const request = http.expectOne(signIn);
    expect(request.request.headers.get('X-Admin-CSRF')).toBe('fixture-csrf');
    expect(password.value).toBe('');
    expect(otp.value).toBe('');
    request.flush({});
    await vi.waitFor(() => http.expectOne(session).flush({
      capabilities: ['admin.invitations.manage'],
      accessExpiresAt: new Date(Date.now() + 60_000).toISOString(),
    }));
    await harness.fixture.whenStable();
    harness.detectChanges();

    const dashboard: HTMLElement = harness.routeNativeElement!;
    expect(dashboard.textContent).toContain('Manage administrator invitations');
    expect(dashboard.textContent).not.toMatch(/billing|security/i);
    http.verify();
  });
});
