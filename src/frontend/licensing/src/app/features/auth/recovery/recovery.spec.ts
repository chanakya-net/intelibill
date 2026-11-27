import { Location } from '@angular/common';
import { provideLocationMocks } from '@angular/common/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { RouterTestingHarness } from '@angular/router/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { appConfig } from '../../../app.config';
import { restoreAdminBrowserCoordination, stubAdminBrowserCoordination } from '../../../core/auth/admin-browser-coordination.testing';

const context = '/api/admin/auth/sign-in/context';
const session = '/api/admin/auth/session';
const code = '0123ABCD-4567EF01-89ABCDEF-01234567';

function configure() {
  TestBed.configureTestingModule({
    providers: [...appConfig.providers, provideHttpClientTesting(), provideLocationMocks()],
  });
  return TestBed.inject(HttpTestingController);
}

function fill(page: HTMLElement, selector: string, value: string) {
  page.querySelector<HTMLInputElement>(selector)!.value = value;
}

function submit(page: HTMLElement, selector = 'form') {
  page.querySelector(selector)!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
}

describe('administrator recovery', () => {
  beforeEach(stubAdminBrowserCoordination);
  afterEach(restoreAdminBrowserCoordination);

  it('redeems a recovery code with the request token and continues only to authenticator enrollment', async () => {
    const http = configure();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/auth/recovery');
    http.expectOne(context).flush({ requestToken: 'recovery-csrf' });
    harness.detectChanges();
    const page: HTMLElement = harness.routeNativeElement!;
    fill(page, '#recovery-email', 'owner@example.test');
    fill(page, '#recovery-password', 'Fixture-password-123');
    fill(page, '#recovery-proof', code);
    submit(page);

    const request = http.expectOne('/api/admin/auth/recovery/code');
    expect(request.request.headers.get('X-Admin-CSRF')).toBe('recovery-csrf');
    expect(request.request.body).toEqual({ email: 'owner@example.test', password: 'Fixture-password-123', recoveryCode: code });
    expect(page.querySelector<HTMLInputElement>('#recovery-password')!.value).toBe('');
    expect(page.querySelector<HTMLInputElement>('#recovery-proof')!.value).toBe('');
    request.flush({ state: 'enrollmentRequired', expiresAt: new Date(Date.now() + 900_000).toISOString() });
    await vi.waitFor(() => expect(TestBed.inject(Location).path()).toBe('/auth/mfa-enrollment'));
    http.expectOne('/api/admin/auth/bootstrap/enrollment/mfa').flush(null, { status: 401, statusText: 'Unauthorized' });
    http.verify();
  });

  it('asks the operator to save a continuation before using their final recovery code', async () => {
    const http = configure();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/auth/recovery');
    http.expectOne(context).flush({ requestToken: 'recovery-csrf' });
    harness.detectChanges();
    const page = harness.routeNativeElement!;
    fill(page, '#recovery-email', 'owner@example.test');
    fill(page, '#recovery-password', 'Fixture-password-123');
    fill(page, '#recovery-proof', code);
    submit(page);

    http.expectOne('/api/admin/auth/recovery/code').flush({ code: 'Admin.RecoveryContinuationRequired' },
      { status: 409, statusText: 'Conflict' });
    harness.detectChanges();
    const key = page.querySelector('[data-testid="continuation-key"]')!.textContent!;
    expect(key).toMatch(/^[0-9A-F]{64}$/);
    const saved = page.querySelector<HTMLInputElement>('input[type="checkbox"]')!;
    expect(saved.validity.valueMissing).toBe(true);
    saved.checked = true;
    fill(page, '#recovery-password', 'Fixture-password-123');
    fill(page, '#recovery-proof', code);
    submit(page);
    const redemption = http.expectOne('/api/admin/auth/recovery/code');
    expect(redemption.request.body.continuationKey).toBe(key);
    redemption.flush(null, { status: 503, statusText: 'Service Unavailable' });

    page.querySelector<HTMLInputElement>('input[value="continue"]')!.dispatchEvent(new Event('change'));
    harness.detectChanges();
    expect(page.querySelector('[data-testid="continuation-key"]')).toBeNull();
    fill(page, '#recovery-password', 'Fixture-password-123');
    fill(page, '#recovery-proof', key);
    submit(page);
    const continuation = http.expectOne('/api/admin/auth/recovery/continue');
    expect(continuation.request.body).toEqual({
      email: 'owner@example.test', password: 'Fixture-password-123', continuationKey: key,
    });
    continuation.flush({ state: 'enrollmentRequired', expiresAt: new Date(Date.now() + 900_000).toISOString() });
    await vi.waitFor(() => expect(TestBed.inject(Location).path()).toBe('/auth/mfa-enrollment'));
    http.expectOne('/api/admin/auth/bootstrap/enrollment/mfa').flush(null, { status: 401, statusText: 'Unauthorized' });
    http.verify();
  });

  it('sends an owner reset grant to the reset route and keeps a refused proof on the page', async () => {
    const http = configure();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/auth/recovery');
    http.expectOne(context).flush({ requestToken: 'recovery-csrf' });
    harness.detectChanges();
    const page: HTMLElement = harness.routeNativeElement!;
    page.querySelector<HTMLInputElement>('input[value="reset"]')!.dispatchEvent(new Event('change'));
    harness.detectChanges();
    fill(page, '#recovery-email', 'admin@example.test');
    fill(page, '#recovery-password', 'Fixture-password-123');
    fill(page, '#recovery-proof', 'k1_' + 'a'.repeat(86));
    submit(page);
    const request = http.expectOne('/api/admin/auth/recovery/reset');
    expect(request.request.body.resetToken).toBe('k1_' + 'a'.repeat(86));
    request.flush({ code: 'Admin.RecoveryRefused' }, { status: 401, statusText: 'Unauthorized' });
    harness.detectChanges();
    expect(page.querySelector('[role="alert"]')?.textContent).toContain('never works twice');
    expect(TestBed.inject(Location).path()).toBe('/auth/recovery');
    http.verify();
  });

  it('lets an owner issue a reviewed reset with evidence and shows the one-use code once', async () => {
    const http = configure();
    const harness = await RouterTestingHarness.create();
    const navigation = harness.navigateByUrl('/security/recovery');
    await vi.waitFor(() => http.expectOne(session).flush({
      capabilities: ['admin.recovery.review'], accessExpiresAt: new Date(Date.now() + 60_000).toISOString(),
    }));
    await navigation;
    http.expectOne(context).flush({ requestToken: 'owner-csrf' });
    harness.detectChanges();
    const page: HTMLElement = harness.routeNativeElement!;
    fill(page, '#review-email', 'admin@example.test');
    fill(page, '#review-evidence', 'SEC-2026-0412');
    page.querySelector<HTMLInputElement>('input[name="confirmed"]')!.checked = true;
    submit(page);
    harness.detectChanges();

    const stale = http.expectOne('/api/admin/recovery/resets');
    expect(stale.request.headers.get('X-Admin-CSRF')).toBe('owner-csrf');
    expect(stale.request.body).toEqual({
      email: 'admin@example.test', reason: 'authenticator-lost', verification: 'video-call',
      evidenceReference: 'SEC-2026-0412', confirmed: true,
    });
    stale.flush({ code: 'Admin.RecentMfaRequired' }, { status: 403, statusText: 'Forbidden' });
    harness.detectChanges();
    expect(page.querySelector('[role="alert"]')?.textContent).toContain('within five minutes');

    fill(page, '#review-email', 'admin@example.test');
    fill(page, '#review-evidence', 'SEC-2026-0412');
    page.querySelector<HTMLInputElement>('input[name="confirmed"]')!.checked = true;
    submit(page);
    http.expectOne('/api/admin/recovery/resets').flush({
      recoveryId: 'fixture-recovery', resetToken: 'k1_fixture-reset-code', expiresAt: '2026-11-16T12:00:00Z',
    });
    harness.detectChanges();
    expect(page.querySelector('[data-testid="reset-code"]')?.textContent).toBe('k1_fixture-reset-code');
    page.querySelector<HTMLButtonElement>('button[type="button"]')!.click();
    harness.detectChanges();
    expect(page.textContent).not.toContain('k1_fixture-reset-code');
    http.verify();
  });
});
