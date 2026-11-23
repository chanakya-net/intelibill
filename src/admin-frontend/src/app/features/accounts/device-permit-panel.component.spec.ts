import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SESSION_PORT, type SessionState } from '@intelibill/api';
import { type Observable, Subject, of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { type AccountDevicePermits, type DevicePermit, DevicePermitApi, remaining } from './device-permit-api.service';
import { DevicePermitPanelComponent } from './device-permit-panel.component';

const account = '33333333-3333-4333-8333-333333333333';
const shop = '11111111-1111-4111-8111-111111111111';
const device = (over: Partial<DevicePermit> = {}): DevicePermit => ({
  shopId: shop, shopName: 'Fixture Shop', deviceRef: 'ab'.repeat(32), lastContactAt: '2026-11-21T06:30:00.000123Z',
  lastSynchronizedAt: '2026-11-21T06:30:00.000123Z', lastPermitIssuedAt: '2026-11-21T04:00:00Z', outstanding: true,
  knownPermitEnd: '2026-11-23T03:59:59.999999Z', outstandingPermits: 1, limit: 'VerificationFreshness',
  accountRevision: 2, shopRevision: 3, authorityVerifiedAt: '2026-11-21T03:59:59.999999Z', ...over,
});
const seen = (devices: DevicePermit[], observedSince: string | null = '2026-11-21T04:00:00Z'): AccountDevicePermits => ({
  accountId: account, evaluatedAt: '2026-11-22T04:00:00Z', disconnectedQueues: 'Unknown', observedSince, devices,
});

function render(read: (account: string, shop?: string | null) => Observable<AccountDevicePermits>, grants = ['support']) {
  const capabilities = signal(grants);
  const state = signal<SessionState>('authenticated');
  const spy = vi.fn(read);
  TestBed.configureTestingModule({ providers: [
    { provide: SESSION_PORT, useValue: { state, capabilities } },
    { provide: DevicePermitApi, useValue: { read: spy } },
  ] });
  const view = TestBed.createComponent(DevicePermitPanelComponent);
  view.componentRef.setInput('account', account);
  view.componentRef.setInput('shops', [{ shopId: shop, shopName: 'Fixture Shop' }]);
  view.detectChanges();
  const text = (id: string) => (view.nativeElement as HTMLElement).querySelector(`[data-testid="${id}"]`)?.textContent?.replace(/\s+/g, ' ').trim();
  return { view, page: view.nativeElement as HTMLElement, text, spy, capabilities };
}

afterEach(() => {
  vi.useRealTimers();
  TestBed.resetTestingModule();
});

describe('device permission panel', () => {
  it('shows the exact known end and the remaining time from the server evaluation, never the browser clock', () => {
    vi.useFakeTimers({ now: new Date('2031-01-01T00:00:00Z') });
    const { text, spy } = render(() => of(seen([device()])));
    expect(spy).toHaveBeenCalledWith(account, null);
    expect(text('device-permit-end')).toBe('23 Nov 2026, 09:29:59.999999 IST · 23 h 59 min remaining at evaluation');
    expect(text('device-permit-state')).toBe('Permission outstanding');
    expect(text('device-permit-sync')).toBe('21 Nov 2026, 12:00:00.000123 IST');
    expect(text('device-permit-issued')).toBe('21 Nov 2026, 09:30:00 IST');
    expect(text('device-permit-queue')).toBe('Unknown');
    expect(text('device-permit-evaluated')).toContain('Evaluated 22 Nov 2026, 09:30:00 IST');
    expect(text('device-permit-since')).toContain('Reporting for this account began 21 Nov 2026, 09:30:00 IST');
    expect(remaining('2026-11-22T04:00:00Z', '2026-11-25T05:01:00Z')).toBe('3 d 1 h 1 min');
    expect(remaining('2026-11-22T04:00:00Z', '2026-11-22T04:00:00.000001Z')).toBe('less than 1 min');
  });

  it('never borrows a permit time for an upload, and says so when none was observed', () => {
    const { text } = render(() => of(seen([device({ lastSynchronizedAt: null, lastContactAt: '2026-11-21T04:00:00Z' })])));
    expect(text('device-permit-sync')).toBe('None observed');
    expect(text('device-permit-issued')).toBe('21 Nov 2026, 09:30:00 IST');
  });

  it('keeps an ended permit visible without calling it outstanding', () => {
    const ended = device({ outstanding: false, outstandingPermits: 0, knownPermitEnd: '2026-11-22T04:00:00Z' });
    const uploader = device({ deviceRef: 'cd'.repeat(32), outstanding: false, outstandingPermits: 0, knownPermitEnd: null,
      lastPermitIssuedAt: null, limit: null, accountRevision: null, shopRevision: null, authorityVerifiedAt: null });
    const { page } = render(() => of(seen([ended, uploader])));
    const ends = [...page.querySelectorAll('[data-testid="device-permit-end"]')].map(item => item.textContent?.replace(/\s+/g, ' ').trim());
    expect(ends).toEqual(['No known outstanding permission; last ended 22 Nov 2026, 09:30:00 IST',
      'No permission issuance observed for this device']);
    expect([...page.querySelectorAll('[data-testid="device-permit-state"]')].map(item => item.textContent))
      .toEqual(['No known outstanding permission', 'No known outstanding permission']);
  });

  it('always separates observed, not observed and unknown, and never claims instant revocation', () => {
    const { text, page } = render(() => of(seen([], null)));
    expect(text('device-permit-observed')).toContain("Times are the merchant service's clock");
    expect(text('device-permit-not-observed')).toContain('Whether a device is connected now');
    expect(text('device-permit-unknown')).toContain('A received upload does not mean its queue is empty');
    expect(text('device-permit-revocation')).toContain('does not revoke permission already issued');
    expect(text('device-permit-empty')).toContain('This is not proof that no offline permission is outstanding');
    expect(page.querySelector('[data-testid="device-permit-since"]')).toBeNull();
    expect(page.querySelector('button')).toBeNull();
    expect(page.textContent).not.toMatch(/revoked|offline now|online now|queue is empty\b(?!\.)|no pending invoices/i);
  });

  it.each([[['billing']], [['platform-owner']], [[]]])('renders nothing and asks nothing with %j', grants => {
    const { page, spy } = render(() => of(seen([device()])), grants);
    expect(spy).not.toHaveBeenCalled();
    expect(page.textContent?.trim()).toBe('');
  });

  it.each([[401, 'Your provider session ended'], [403, 'Only Support, Licensing Manager or Security administrators'],
    [404, 'could not be found'], [503, 'Nothing is known until it can be']])('maps %i to its own message', (status, message) => {
    const { page } = render(() => throwError(() => new HttpErrorResponse({ status })));
    expect(page.querySelector('[role="alert"]')?.textContent).toContain(message);
    expect(page.querySelector('[data-testid="device-permit-device"]')).toBeNull();
  });

  it('filters by shop and drops a late answer for the previous scope or grant', () => {
    const answers: Subject<AccountDevicePermits>[] = [];
    const { view, page, spy, capabilities } = render(() => {
      answers.push(new Subject());
      return answers.at(-1)!;
    }, ['security-administrator']);
    const select = page.querySelector<HTMLSelectElement>('#device-permit-shop')!;
    select.value = shop;
    select.dispatchEvent(new Event('change'));
    expect(spy).toHaveBeenLastCalledWith(account, shop);
    answers[0].next(seen([device({ shopName: 'Late answer' })]));
    view.detectChanges();
    expect(page.textContent).not.toContain('Late answer');
    answers[1].next(seen([device()]));
    view.detectChanges();
    expect(page.querySelectorAll('[data-testid="device-permit-device"]')).toHaveLength(1);

    view.componentRef.setInput('account', '44444444-4444-4444-8444-444444444444');
    view.detectChanges();
    expect(spy).toHaveBeenLastCalledWith('44444444-4444-4444-8444-444444444444', null);
    expect(page.querySelector('[data-testid="device-permit-device"]')).toBeNull();
    capabilities.set(['billing']);
    view.detectChanges();
    answers[2].next(seen([device()]));
    view.detectChanges();
    expect(page.textContent?.trim()).toBe('');
  });
});
