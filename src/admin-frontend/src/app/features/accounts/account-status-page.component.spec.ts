import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SESSION_PORT, type SessionState } from '@intelibill/api';
import { Subject, of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AccountStatusApi, formatIst, type AccountStatus, type TermStatus } from './account-status-api.service';
import { AccountStatusPageComponent } from './account-status-page.component';

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
      { provide: AccountStatusApi, useValue: { read } },
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

  it('labels a Complete Trial as having no grace period', () => {
    const current = status();
    const trial = { ...term, state: 'Active' as const, basis: 'Trial' as const, graceEndsAt: null };
    const { page, lookup } = render(() => of({ ...current, items: [{ ...current.items[0], status: trial }] }));
    lookup();
    expect(page.querySelector('[data-testid="provider-term"]')?.textContent).toContain('No grace period');
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

  it('formats the billing zone whatever the browser zone is', () => {
    expect(formatIst('2027-03-30T20:30:00Z')).toBe('31 Mar 2027, 02:00 IST');
  });
});
