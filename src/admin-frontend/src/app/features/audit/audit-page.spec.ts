import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { RouterTestingHarness } from '@angular/router/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { appConfig } from '../../app.config';
import { restoreAdminBrowserCoordination, stubAdminBrowserCoordination } from '../../core/auth/admin-browser-coordination.testing';
import { AdminSessionService } from '../auth/admin-session.service';

const sessionUrl = '/api/admin/auth/session';
const auditUrl = '/api/admin/audit';
const capability = 'admin.audit.identity.read';

function configure() {
  TestBed.configureTestingModule({
    providers: [...appConfig.providers, provideHttpClientTesting()],
  });
  return TestBed.inject(HttpTestingController);
}

async function open(http: HttpTestingController, capabilities: string[]) {
  const harness = await RouterTestingHarness.create();
  const navigation = harness.navigateByUrl('/audit');
  await vi.waitFor(() => http.expectOne(sessionUrl).flush({
    capabilities, accessExpiresAt: new Date(Date.now() + 60_000).toISOString(),
  }));
  await navigation;
  harness.detectChanges();
  return harness;
}

describe('provider audit history', () => {
  beforeEach(stubAdminBrowserCoordination);
  afterEach(restoreAdminBrowserCoordination);

  it('denies the direct route for an authenticated session without the grant', async () => {
    const http = configure();
    const harness = await open(http, []);
    expect(harness.routeNativeElement?.querySelector('[role="alert"]')?.textContent)
      .toContain('You cannot inspect provider audit history.');
    http.expectNone((request) => request.url === auditUrl);
    http.verify();
  });

  it('replaces an older filter request and clears evidence on capability loss', async () => {
    const http = configure();
    const harness = await open(http, [capability]);
    const first = http.expectOne((request) => request.url === auditUrl);
    const page = harness.routeNativeElement!;
    page.querySelector<HTMLSelectElement>('#audit-action')!.value = 'invitation-issued';
    page.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    harness.detectChanges();
    const filtered = http.expectOne((request) =>
      request.url === auditUrl && request.params.get('action') === 'invitation-issued');
    expect(first.cancelled).toBe(true);
    filtered.flush({ items: [{
      id: 'fixture-id', action: 'invitation-issued', scope: 'provider-identity',
      outcome: 'succeeded', occurredAt: '2026-11-14T12:00:00Z', correlationId: 'fixture-request',
    }], nextCursor: null });
    harness.detectChanges();
    expect(page.querySelector('[aria-label="Audit events"]')?.textContent).toContain('Invitation issued');
    page.querySelector('summary')!.click();
    expect(page.textContent).toContain('fixture-request');

    TestBed.inject(AdminSessionService).check().subscribe();
    http.expectOne(sessionUrl).flush({ capabilities: [], accessExpiresAt: new Date(Date.now() + 60_000).toISOString() });
    harness.detectChanges();
    expect(page.textContent).not.toContain('fixture-request');
    expect(page.querySelector('[aria-label="Audit events"]')).toBeNull();
    http.verify();
  });

  it('clears a pending read when the provider session ends', async () => {
    const http = configure();
    const harness = await open(http, [capability]);
    const pending = http.expectOne((request) => request.url === auditUrl);
    TestBed.inject(AdminSessionService).settle('ended');
    harness.detectChanges();
    expect(pending.cancelled).toBe(true);
    expect(harness.routeNativeElement?.querySelector('[aria-label="Audit events"]')).toBeNull();
    http.verify();
  });
});
