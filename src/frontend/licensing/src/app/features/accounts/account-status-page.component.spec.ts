import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SESSION_PORT, type SessionState } from '@intelibill/api';
import { NEVER, Subject, of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AccountStatusApi, formatIst, type AccountStatus, type TermStatus } from './account-status-api.service';
import { AccountStatusPageComponent } from './account-status-page.component';
import { AdminSessionService } from '../auth/admin-session.service';
import { TrialExtensionApi } from './trial-extension-api.service';
import { TrialExtensionDrafts } from './trial-extension-drafts';
import { ComplimentaryGrantApi } from './complimentary-grant-api.service';
import { DevicePermitApi } from './device-permit-api.service';

const account = '33333333-3333-4333-8333-333333333333';
const term: TermStatus = {
  termId: '44444444-4444-4444-8444-444444444444', state: 'Grace', evidenceState: 'Complete', basis: 'Paid',
  billingPeriod: 'Month', timeZone: 'Asia/Kolkata', startsAt: '2027-01-31T04:30:00Z', paidThrough: '2027-02-28T04:30:00Z',
  graceEndsAt: '2027-03-07T04:30:00Z', accessEndsAt: '2027-03-07T04:30:00Z', nextDeadline: '2027-03-07T04:30:00Z',
  nextPeriodEndsAt: '2027-03-31T04:30:00Z', coverageMode: 'Independent', purchasedCapacity: 1, memberAllowance: 5,
  sourceReference: 'local-fixture-p0209', developmentFixture: true, commercial: null,
};
const status = (commercial = false): AccountStatus => ({
  evaluatedAt: '2027-03-01T00:00:00Z',
  items: [{ status: { ...term, commercial: commercial ? { pricePaise: 149900, currency: 'INR', planVersionId: null } : null },
    coveredShops: [{ shopId: '11111111-1111-4111-8111-111111111111', shopName: 'Fixture Shop' }], coveredShopCount: 1 }],
});

function render(read: AccountStatusApi['read'], grants: string[] = ['support']) {
  const capabilities = signal(grants);
  const state = signal<SessionState>('authenticated');
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: SESSION_PORT, useValue: { state, capabilities } },
      { provide: AccountStatusApi, useValue: { read, closures: () => of([]) } },
      { provide: AdminSessionService, useValue: { adminId: signal('admin-1'), sessionId: signal('session-1') } },
      { provide: ComplimentaryGrantApi, useValue: {} },
      { provide: DevicePermitApi, useValue: { read: () => NEVER } },
      { provide: TrialExtensionApi, useValue: {} },
      { provide: TrialExtensionDrafts, useValue: { read: () => Promise.resolve(null) } },
    ],
  });
  const view = TestBed.createComponent(AccountStatusPageComponent);
  view.detectChanges();
  const page = view.nativeElement as HTMLElement;
  const lookup = (id = account) => {
    const input = page.querySelector<HTMLInputElement>('#status-account')!;
    input.value = id;
    input.dispatchEvent(new Event('input'));
    page.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    view.detectChanges();
  };
  return { view, page, lookup, capabilities, state };
}

afterEach(() => TestBed.resetTestingModule());

describe('provider account access status', () => {
  it.each([
    ['Pending', 'Retrying', null, 'Saved, waiting for confirmation'],
    ['Applied', 'Acknowledged', null, 'Applied'],
    ['NeedsReview', 'DeadLetter', 'Unavailable', 'Needs review'],
  ] as const)('renders %s per shop with separate saved and confirmed revisions', (state, delivery, attention, label) => {
    const current = status();
    const item = current.items[0];
    const synchronization = {
      state, delivery, attention, savedAccountRevision: 2, savedShopRevision: 3, savedVerifiedAt: '2027-02-10T00:00:00Z',
      appliedAccountRevision: 2, appliedShopRevision: state === 'Applied' ? 3 : 2,
      lastSynchronizedAt: '2027-02-10T00:00:01.123456Z',
    };
    const coveredShops = [{ ...item.coveredShops[0], synchronization }];
    const { page, lookup } = render(() => of({ ...current, items: [{ ...item, coveredShops }] }));
    lookup();
    expect(page.querySelector('[data-testid="provider-sync-state"]')?.textContent).toBe(label);
    expect(page.querySelector('[data-testid="provider-sync-saved"]')?.textContent).toBe('Account 2 · Shop 3');
    expect(page.querySelector('[data-testid="provider-sync-applied"]')?.textContent)
      .toBe(state === 'Applied' ? 'Account 2 · Shop 3' : 'Account 2 · Shop 2');
    expect(page.querySelector('[data-testid="provider-sync-time"]')?.textContent).toBe('10 Feb 2027, 05:30:01.123456 IST');
    expect(page.querySelector('[data-testid="provider-sync-attention"]')?.textContent ?? null).toBe(attention);
  });

  it('renders exact IST deadlines for Support without commercial details', () => {
    const read = vi.fn(() => of(status()));
    const { page, lookup } = render(read);
    lookup();
    expect(read).toHaveBeenCalledWith(account);
    const text = page.querySelector('[data-testid="provider-term"]')!.textContent!;
    expect(text).toContain('In grace period');
    expect(text).toContain('28 Feb 2027, 10:00 IST');
    expect(text).toContain('7 Mar 2027, 10:00 IST');
    expect(text).toContain('Fixture Shop');
    expect(page.querySelector('[data-testid="provider-price"]')).toBeNull();
    expect(page.textContent).toContain('Observe-only');
  });

  it('shows an exact Trial end and no grace period', () => {
    const current = status();
    const trial = { ...term, state: 'Active' as const, basis: 'Trial' as const, graceEndsAt: null,
      accessEndsAt: '2027-03-10T13:15:30.123456Z' };
    const { page, lookup } = render(() => of({ ...current, items: [{ ...current.items[0], status: trial }] }));
    lookup();
    expect(page.querySelector('[data-testid="provider-term"]')?.textContent).toContain('No grace period');
    expect(page.querySelector('[data-testid="provider-trial-ends"]')?.textContent).toBe('10 Mar 2027, 18:45:30.123456 IST');
    expect(page.querySelector('adm-trial-extension')).toBeNull();
  });

  it('coalesces visibilitychange and focus for the selected account', () => {
    const pending = new Subject<AccountStatus>();
    const read = vi.fn().mockReturnValueOnce(of(status())).mockReturnValue(pending);
    const { lookup, capabilities, view } = render(read);
    const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
    try {
      window.dispatchEvent(new Event('focus'));
      expect(read).not.toHaveBeenCalled();
      lookup();
      document.dispatchEvent(new Event('visibilitychange'));
      window.dispatchEvent(new Event('focus'));
      expect(read).toHaveBeenCalledTimes(2);
      expect(read).toHaveBeenLastCalledWith(account);

      capabilities.set(['billing']);
      view.detectChanges();
      window.dispatchEvent(new Event('focus'));
      expect(read).toHaveBeenCalledTimes(2);
    } finally {
      visibility.mockRestore();
    }
  });

  it('shows the price only when the server includes it for commercial grants', () => {
    const { page, lookup } = render(() => of(status(true)), ['billing']);
    lookup();
    expect(page.querySelector('[data-testid="provider-price"]')?.textContent).toContain('1,499.00');
  });

  it('clears status when grants change and ignores a late answer', () => {
    const late = new Subject<AccountStatus>();
    const { view, page, lookup, capabilities } = render(() => late);
    lookup();
    capabilities.set(['security-administrator']);
    view.detectChanges();
    late.next(status(true));
    view.detectChanges();
    expect(page.querySelector('[data-testid="provider-term"]')).toBeNull();
    expect(page.textContent).toContain('Only Support, Billing or Licensing Manager');
  });

  it('keeps an outage distinct from expiry', () => {
    const { page, lookup } = render(() => throwError(() => new HttpErrorResponse({ status: 503 })));
    lookup();
    expect(page.querySelector('[role="alert"]')?.textContent).toContain('not an expiry');
  });

  it('labels courtesy access by its exact end and included capacity, never as paid', () => {
    const current = status();
    const courtesy: TermStatus = { ...term, state: 'Active', basis: 'Complimentary', billingPeriod: null, graceEndsAt: null,
      paidThrough: '2027-03-31T18:30:00.000001Z', accessEndsAt: '2027-03-31T18:30:00.000001Z', nextDeadline: '2027-03-31T18:30:00.000001Z',
      nextPeriodEndsAt: null, coverageMode: 'Grouped', purchasedCapacity: 2, memberAllowance: 4, developmentFixture: false };
    const { page, lookup } = render(() => of({ ...current, items: [{ ...current.items[0], status: courtesy }] }));
    lookup();
    const text = page.querySelector('[data-testid="provider-term"]')!.textContent!;
    expect(page.querySelector('[data-testid="provider-courtesy-ends"]')!.textContent).toBe('1 Apr 2027, 00:00:00.000001 IST');
    expect(text).toContain('Courtesy access');
    expect(text).toContain('Included capacity');
    expect(text).toContain('None recorded (complimentary)');
    expect(text).toContain('No grace period');
    expect(text).not.toContain('Paid through');
    expect(text).not.toContain('Purchased capacity');
  });

  it('offers the grant editor only to a Licensing Manager', () => {
    const support = render(() => of(status()));
    support.lookup();
    expect(support.page.querySelector('[data-testid="grant-editor"]')).toBeNull();
    TestBed.resetTestingModule();
    const manager = render(() => of(status()), ['licensing-manager']);
    manager.lookup();
    expect(manager.page.querySelector('[data-testid="grant-editor"]')).not.toBeNull();
    expect(manager.page.querySelector('adm-trial-extension')).not.toBeNull();
    manager.capabilities.set(['support']);
    manager.view.detectChanges();
    expect(manager.page.querySelector('[data-testid="grant-editor"]')).toBeNull();
  });

  it('formats the billing zone whatever the browser zone is', () => {
    expect(formatIst('2027-03-30T20:30:00Z')).toBe('31 Mar 2027, 02:00 IST');
  });

  it('lets Security Administrator look up device permissions without requesting account status or closures', () => {
    const read = vi.fn(() => of(status(true)));
    const closures = vi.fn(() => of([]));
    const readPermits = vi.fn(() => of({
      accountId: account, evaluatedAt: '2026-11-22T04:00:00Z', disconnectedQueues: 'Unknown' as const,
      observedSince: '2026-11-21T04:00:00Z', devices: [],
    }));
    const capabilities = signal(['security-administrator']);
    const state = signal<SessionState>('authenticated');
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: SESSION_PORT, useValue: { state, capabilities } },
        { provide: AccountStatusApi, useValue: { read, closures } },
        { provide: AdminSessionService, useValue: { adminId: signal('admin-1'), sessionId: signal('session-1') } },
        { provide: ComplimentaryGrantApi, useValue: {} },
        { provide: DevicePermitApi, useValue: { read: readPermits } },
        { provide: TrialExtensionApi, useValue: {} },
        { provide: TrialExtensionDrafts, useValue: { read: () => Promise.resolve(null) } },
      ],
    });
    const view = TestBed.createComponent(AccountStatusPageComponent);
    view.detectChanges();
    const page = view.nativeElement as HTMLElement;
    const input = page.querySelector<HTMLInputElement>('#status-account');
    expect(input).not.toBeNull();
    input!.value = account;
    input!.dispatchEvent(new Event('input'));
    page.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    view.detectChanges();

    expect(readPermits).toHaveBeenCalledWith(account, null);
    expect(read).not.toHaveBeenCalled();
    expect(closures).not.toHaveBeenCalled();
    expect(page.querySelector('[data-testid="device-permit-panel"]')).not.toBeNull();
    expect(page.querySelector('[data-testid="provider-term"]')).toBeNull();
    expect(page.querySelector('[data-testid="provider-closure-detail"]')).toBeNull();
    expect(page.querySelector('[data-testid="grant-editor"]')).toBeNull();

    window.dispatchEvent(new Event('focus'));
    expect(read).not.toHaveBeenCalled();

    capabilities.set(['platform-owner']);
    view.detectChanges();
    expect(page.querySelector('#status-account')).toBeNull();
    expect(page.querySelector('[data-testid="device-permit-panel"]')).toBeNull();
  });
});
