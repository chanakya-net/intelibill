import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { restoreAdminBrowserCoordination, stubAdminBrowserCoordination } from '../../../core/auth/admin-browser-coordination.testing';
import { AdminSessionService } from '../../auth/admin-session.service';
import { SessionsPageComponent } from './sessions-page.component';

const base = '/api/admin/sessions';
const own = '1a8afa0a-7143-4b6e-8098-2c972bda0841';
const other = '2a8afa0a-7143-4b6e-8098-2c972bda0841';
const times = {
  createdAt: '2026-11-14T09:00:00Z', lastSeenAt: '2026-11-14T09:01:00Z',
  absoluteExpiresAt: '2026-11-14T21:00:00Z',
};
const items = [{ ...times, id: own, isCurrent: true }, { ...times, id: other, isCurrent: false }];

async function setup() {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
  const http = TestBed.inject(HttpTestingController);
  const session = TestBed.inject(AdminSessionService);
  const checked = firstValueFrom(session.check());
  http.expectOne('/api/admin/auth/session').flush({
    accessExpiresAt: new Date(Date.now() + 60_000).toISOString(), sessionId: own, capabilities: [],
  });
  expect(await checked).toBe('active');
  const fixture = TestBed.createComponent(SessionsPageComponent);
  fixture.detectChanges();
  return { http, session, fixture, element: fixture.nativeElement as HTMLElement };
}

describe('provider device sessions', () => {
  beforeEach(stubAdminBrowserCoordination);
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    restoreAdminBrowserCoordination();
  });

  it('shows an ordinary administrator their devices and requires a reason and CSRF to revoke', async () => {
    const { http, fixture, element } = await setup();
    http.expectOne(base).flush({ items, nextCursor: null });
    fixture.detectChanges();

    expect(element.textContent).toContain('This device');
    expect(element.textContent).toContain('Other device');
    expect(element.textContent).toContain('2026-11-14 09:00:00 UTC');
    expect(element.textContent).not.toContain(other);
    const button = [...element.querySelectorAll('button')].find((item) => item.textContent?.trim() === 'Revoke device');
    button?.click();
    fixture.detectChanges();

    const form = element.querySelector('form')!;
    form.dispatchEvent(new SubmitEvent('submit', { cancelable: true }));
    http.expectNone('/api/admin/auth/sign-in/context');
    form.querySelector('select')!.value = 'device-unused';
    form.dispatchEvent(new SubmitEvent('submit', { cancelable: true }));
    http.expectOne('/api/admin/auth/sign-in/context').flush({ requestToken: 'fixture-csrf' });
    const revoke = http.expectOne(base + '/' + other + '/revoke');
    expect(revoke.request.headers.get('X-Admin-CSRF')).toBe('fixture-csrf');
    expect(revoke.request.body).toEqual({ reason: 'device-unused' });
    revoke.flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne(base).flush({ items: items.slice(0, 1), nextCursor: null });
    fixture.detectChanges();
    expect(element.textContent).toContain('Device session revoked.');
    expect(element.textContent).not.toContain('Other device');
  });

  it('drops a revoke intent if the browser signs in again while obtaining CSRF', async () => {
    const { http, fixture, element } = await setup();
    http.expectOne(base).flush({ items, nextCursor: null });
    fixture.detectChanges();
    const button = [...element.querySelectorAll('button')].find((item) => item.textContent?.trim() === 'Revoke device');
    button?.click();
    fixture.detectChanges();

    const form = element.querySelector('form')!;
    form.querySelector('select')!.value = 'device-unused';
    form.dispatchEvent(new SubmitEvent('submit', { cancelable: true }));
    const pending = http.expectOne('/api/admin/auth/sign-in/context');
    localStorage.setItem('intelibill.admin.session-coordination.v1', JSON.stringify({ generation: 'new', state: 'ready' }));
    pending.flush({ requestToken: 'fixture-csrf' });
    http.expectNone(base + '/' + other + '/revoke');
  });

  it('discards a list that resolves after local access ends', async () => {
    const { http, session, fixture, element } = await setup();
    const held = http.expectOne(base);
    session.settle('ended');
    held.flush({ items, nextCursor: null });
    fixture.detectChanges();
    expect(element.textContent).not.toContain('This device');
    expect(element.textContent).not.toContain('Other device');
  });
});
