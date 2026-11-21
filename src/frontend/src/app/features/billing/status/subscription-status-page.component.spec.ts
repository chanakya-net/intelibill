import { provideHttpClient } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { EMPTY, Subject, of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import en from '../../../../../public/assets/i18n/en-IN.json';
import { AuthService } from '../../../core/auth/auth.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { billingRoutes } from '../billing.routes';
import { SubscriptionStatusPageComponent } from './subscription-status-page.component';
import { formatBillingInstant, type ShopSubscriptionStatus, type ShopTradingAuthority, type SubscriptionTermStatus } from './subscription-status.models';
import { StatusReadError, SubscriptionStatusService, TradingAuthorityReadError } from './subscription-status.service';

const shopA = '11111111-1111-4111-8111-111111111111';
const shopB = '22222222-2222-4222-8222-222222222222';
const account = '33333333-3333-4333-8333-333333333333';

const term: SubscriptionTermStatus = {
  termId: '44444444-4444-4444-8444-444444444444', state: 'Active', evidenceState: 'Complete', basis: 'Paid',
  billingPeriod: 'Month', timeZone: 'Asia/Kolkata', startsAt: '2027-01-31T04:30:00Z', paidThrough: '2027-02-28T04:30:00Z',
  graceEndsAt: '2027-03-07T04:30:00Z', accessEndsAt: '2027-03-07T04:30:00Z', nextDeadline: '2027-02-28T04:30:00Z',
  nextPeriodEndsAt: '2027-03-31T04:30:00Z', coverageMode: 'Grouped', purchasedCapacity: 3, memberAllowance: 5,
  sourceReference: 'local-fixture-p0209', developmentFixture: true, commercial: null,
};

function status(shopId: string, overrides: Partial<ShopSubscriptionStatus> = {}): ShopSubscriptionStatus {
  return {
    schemaVersion: 1, shopId, evaluatedAt: '2027-02-10T00:00:00Z', state: 'Active', term, accountId: null,
    observeOnly: true, entitlementSynchronization: 'NotAvailable', paymentVerification: 'NotAvailable', ...overrides,
  };
}

function authority(shopId: string, overrides: Partial<ShopTradingAuthority> = {}): ShopTradingAuthority {
  return { schemaVersion: 1, shopId, authorityVerifiedAt: '2027-02-08T00:00:00Z', validUntil: '2027-02-10T00:00:00Z', enforced: false, ...overrides };
}

const authorityDeadline = '[data-testid="trading-authority-deadline"]';
const authorityOutage = '[data-testid="trading-authority-unavailable"]';

function render(api: Partial<SubscriptionStatusService>, params: Record<string, string> = {}) {
  const session = signal<{ user: { id: string }; activeShopId: string | null } | null>({ user: { id: 'u1' }, activeShopId: shopA });
  TestBed.configureTestingModule({
    imports: [TranslocoTestingModule.forRoot({
      langs: { 'en-IN': en }, translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' }, preloadLangs: true,
    })],
    providers: [
      provideRouter([]),
      provideHttpClient(),
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(params) }, paramMap: of(convertToParamMap(params)) } },
      { provide: LocalizationService, useValue: { currentLanguage: signal('en-IN') } },
      { provide: AuthService, useValue: { session } },
      { provide: SubscriptionStatusService, useValue: { tradingAuthority: () => EMPTY, ...api } },
    ],
  });
  const fixture = TestBed.createComponent(SubscriptionStatusPageComponent);
  fixture.detectChanges();
  return { fixture, page: fixture.nativeElement as HTMLElement, session };
}

afterEach(() => TestBed.resetTestingModule());

describe('subscription status', () => {
  it.each([false, true])('shows reopening confirmation for the current shop when applied is %s', (applied) => {
    const { fixture, page, session } = render({
      shop: id => of(status(id, id === shopA ? { reopenedAt: '2027-02-10T00:00:00.123456Z',
        reopeningEnforced: applied, reopeningReceived: applied, enforcedSuspensionMode: 'Security' } : { closedAt: '2027-02-10T00:00:00Z' })),
    });
    const result = '[data-testid="shop-reopening-status"]';
    expect(page.querySelector(result)?.textContent).toContain('05:30:00.123456 IST');
    expect(page.querySelector(result)?.textContent).toContain(applied
      ? 'Reopening applied at this shop.' : 'Awaiting confirmation from the merchant service.');
    expect(page.querySelector('[data-testid="enforced-suspension"]')).not.toBeNull();
    session.update(current => ({ ...current!, activeShopId: shopB }));
    fixture.detectChanges();
    expect(page.querySelector(result)).toBeNull();
    expect(page.querySelector('[data-testid="shop-closure-status"]')).not.toBeNull();
  });

  it('shows the suspension enforced by the local merchant projection', () => {
    const { page } = render({
      shop: id => of(status(id, { observeOnly: false, enforcedSuspensionMode: 'Security',
        suspension: { mode: 'Security', effectiveAt: '2027-02-10T00:00:00Z', customerExplanation: 'Contact support.' } })),
    });
    expect(page.querySelector('[data-testid="observe-only"]')).toBeNull();
    expect(page.querySelector('[data-testid="enforced-suspension"]')?.textContent).toContain('enforced for this shop');
  });

  it('shows the customer explanation and clears the suspension on a shop change', () => {
    const { fixture, page, session } = render({
      shop: id => of(status(id, id === shopA ? { suspension: { mode: 'Security',
        effectiveAt: '2027-02-10T00:00:00.123456Z', customerExplanation: 'Contact support about shop access.' } } : {})),
    });
    expect(page.querySelector('[data-testid="shop-suspension"]')?.textContent).toContain('Contact support about shop access.');
    expect(page.querySelector('[data-testid="shop-suspension"]')?.textContent).toContain('05:30:00.123456 IST');
    expect(page.querySelector('[data-testid="observe-only"]')).toBeTruthy();
    expect(page.querySelector('[data-testid="enforced-suspension"]')).toBeNull();
    session.set({ user: { id: 'u1' }, activeShopId: shopB });
    fixture.detectChanges();
    expect(page.querySelector('[data-testid="shop-suspension"]')).toBeNull();
  });

  it('shows the saved revision while merchant confirmation is pending, then clears it on a shop change', () => {
    const synchronization = {
      state: 'Pending' as const, delivery: 'AwaitingAcknowledgement' as const, savedAccountRevision: 2, savedShopRevision: 3,
      savedVerifiedAt: '2027-02-10T00:00:00.123456Z', appliedAccountRevision: null, appliedShopRevision: null,
      lastSynchronizedAt: null, attention: null,
    };
    const shop = (id: string) => of(id === shopA ? { ...status(id), synchronization } : status(id));
    const { fixture, page, session } = render({ shop });
    const sync = page.querySelector('[data-testid="entitlement-sync"]');
    expect(sync?.textContent).toContain('Saved, waiting for confirmation');
    expect(sync?.textContent).toContain('Account 2 · Shop 3');
    expect(sync?.textContent).toContain('Not confirmed yet');
    session.set({ user: { id: 'u1' }, activeShopId: shopB });
    fixture.detectChanges();
    expect(page.querySelector('[data-testid="entitlement-sync"]')).toBeNull();
    expect(page.textContent).toContain('Entitlement delivery to devices is not available yet.');
  });

  it('refreshes local authority after a release and keeps the original paid and grace deadlines', () => {
    const expired = status(shopA, { state: 'Expired', observeOnly: false, term: { ...term, state: 'Expired' } });
    const shop = vi.fn()
      .mockReturnValueOnce(of({ ...expired, enforcedSuspensionMode: 'Security' }))
      .mockReturnValueOnce(of(expired));
    const tradingAuthority = vi.fn()
      .mockReturnValueOnce(throwError(() => new TradingAuthorityReadError('restricted')))
      .mockReturnValueOnce(throwError(() => new TradingAuthorityReadError('expired')));
    const { fixture, page } = render({ shop, tradingAuthority });
    const dates = () => ['paid-through', 'grace-ends'].map(id => page.querySelector(`[data-testid="${id}"]`)?.textContent);
    const before = dates();
    expect(page.querySelector('[data-testid="trading-authority-restricted"]')).toBeTruthy();
    (page.querySelector('[data-testid="status-retry"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(page.querySelector('[data-testid="trading-authority-restricted"]')).toBeNull();
    expect(page.querySelector('[data-testid="trading-authority-expired"]')).toBeTruthy();
    expect(page.querySelector('[data-testid="status-state"]')?.textContent).toContain('Expired');
    expect(dates()).toEqual(before);
    expect(shop).toHaveBeenCalledTimes(2);
    expect(tradingAuthority).toHaveBeenCalledTimes(2);
  });

  it('shows the verified local deadline even when commercial status cannot load', () => {
    const { page } = render({
      shop: () => throwError(() => new StatusReadError('unavailable')),
      tradingAuthority: () => of({ schemaVersion: 1, shopId: shopA,
        authorityVerifiedAt: '2027-02-08T00:00:00Z', validUntil: '2027-02-10T00:00:00Z', enforced: false }),
    });
    expect(page.querySelector('[data-testid="trading-authority-deadline"]')?.textContent)
      .toContain('10 Feb 2027, 05:30:00 IST');
  });

  it.each([
    ['restricted', en.billing.status.tradingAuthority.restricted],
    ['securitySuspended', en.billing.status.suspension.enforcedSecurity],
  ] as const)('shows the local %s restriction when commercial status is unavailable', (failure, message) => {
    const commercialStatus = new Subject<ShopSubscriptionStatus>();
    const { fixture, page } = render({
      shop: () => commercialStatus,
      tradingAuthority: () => throwError(() => new TradingAuthorityReadError(failure, true)),
    });
    const observeOnly = '[data-testid="observe-only"]';
    expect(page.querySelector('[role="status"]')?.textContent).toContain(en.common.loading);
    expect.soft(page.querySelector(observeOnly)).toBeNull();

    commercialStatus.error(new StatusReadError('unavailable'));
    fixture.detectChanges();

    expect(page.querySelector('[data-testid="verification-unavailable"]')?.textContent)
      .toContain(en.billing.status.unavailable);
    expect(page.querySelector(`[data-testid="trading-authority-${failure}"]`)?.textContent).toContain(message);
    expect(page.querySelector(observeOnly)).toBeNull();
  });

  it('shows a positive one-microsecond authority interval', () => {
    const { page } = render({ tradingAuthority: () => of(authority(shopA, {
      authorityVerifiedAt: '2027-02-10T00:00:00.000001Z', validUntil: '2027-02-10T00:00:00.000002Z',
    })), shop: () => of(status(shopA)) });
    expect(page.querySelector(authorityDeadline)?.textContent).toContain('05:30:00.000002 IST');
  });

  it.each([
    ['an inverted microsecond interval', () => authority(shopA, {
      authorityVerifiedAt: '2027-02-10T00:00:00.000002Z', validUntil: '2027-02-10T00:00:00.000001Z' })],
    ['a zero interval', () => authority(shopA, { validUntil: '2027-02-08T00:00:00Z' })],
    ['48 hours plus one microsecond', () => authority(shopA, { validUntil: '2027-02-10T00:00:00.000001Z' })],
    ['an empty success', () => null],
    ['no verification time', () => { const { authorityVerifiedAt: _omitted, ...rest } = authority(shopA); return rest; }],
    ['an unparseable deadline', () => authority(shopA, { validUntil: 'soon' })],
    ['an inverted verification interval', () => authority(shopA, { authorityVerifiedAt: '2027-02-11T00:00:00Z' })],
  ])('treats %s as unavailable, clears the deadline and recovers on retry', (_name, body) => {
    const tradingAuthority = vi.fn()
      .mockReturnValueOnce(of(authority(shopA)))
      .mockReturnValueOnce(of(body()))
      .mockReturnValue(of(authority(shopA)));
    const { fixture, page } = render({ shop: () => of(status(shopA)), tradingAuthority });
    expect(page.querySelector(authorityDeadline)).toBeTruthy();

    const retry = () => {
      (page.querySelector('[data-testid="status-retry"]') as HTMLButtonElement).click();
      fixture.detectChanges();
    };
    retry();
    expect(page.querySelector(authorityOutage)).toBeTruthy();
    expect(page.querySelector(authorityDeadline)).toBeNull();

    retry();
    expect(page.querySelector(authorityOutage)).toBeNull();
    expect(page.querySelector(authorityDeadline)?.textContent).toContain('10 Feb 2027, 05:30:00 IST');
  });

  it('has shop and account destinations before the billing landing route', () => {
    const paths = billingRoutes.map(route => route.path);
    expect(paths).toContain('status');
    expect(paths).toContain('accounts/:accountId/status');
    expect(paths.indexOf('status')).toBeLessThan(paths.indexOf(''));
  });

  it('shows an extended trial deadline exactly as evaluated', () => {
    const ends = '2027-03-10T13:15:30.123456Z';
    const trial: SubscriptionTermStatus = {
      ...term, basis: 'Trial', billingPeriod: null, paidThrough: ends, accessEndsAt: ends, nextDeadline: ends,
      graceEndsAt: null, nextPeriodEndsAt: null, coverageMode: 'Independent', purchasedCapacity: 1,
    };
    const { page } = render({ shop: vi.fn(() => of(status(shopA, { term: trial }))) });
    for (const selector of ['paid-through', 'next-deadline']) {
      expect(page.querySelector(`[data-testid="${selector}"]`)!.textContent).toBe('10 Mar 2027, 18:45:30.123456 IST');
    }
    expect(page.querySelector('[data-testid="grace-ends"]')?.textContent).toContain('No grace period');
  });

  it('explains courtesy access by its exact end and included capacity, with no price, grace or paid wording', () => {
    const courtesy: SubscriptionTermStatus = {
      ...term, basis: 'Complimentary', billingPeriod: null, startsAt: '2027-02-28T18:30:00Z',
      paidThrough: '2027-03-31T18:30:00.000001Z', accessEndsAt: '2027-03-31T18:30:00.000001Z', graceEndsAt: null,
      nextDeadline: '2027-03-31T18:30:00.000001Z', nextPeriodEndsAt: null, coverageMode: 'Independent', purchasedCapacity: 1,
      memberAllowance: 4, sourceReference: 'complimentary-access/1', developmentFixture: false, commercial: null,
    };
    const { page } = render({ shop: vi.fn(() => of(status(shopA, { term: courtesy }))) });
    const facts = page.querySelector('[data-testid="status-term"]')!.textContent!;
    expect(facts).toContain('Courtesy access');
    expect(facts).toContain('Courtesy access ends');
    expect(facts).toContain('Included capacity');
    expect(facts).not.toContain('Paid through');
    expect(facts).not.toContain('Purchased capacity');
    expect(page.querySelector('[data-testid="paid-through"]')!.textContent).toBe('1 Apr 2027, 00:00:00.000001 IST');
    expect(page.querySelector('[data-testid="starts-at"]')!.textContent).toBe('1 Mar 2027, 00:00:00 IST');
    expect(page.querySelector('[data-testid="grace-ends"]')!.textContent).toContain('No grace period');
    expect(page.querySelector('[data-testid="member-allowance"]')!.textContent).toBe('4');
    expect(page.querySelector('[data-testid="commercial-price"]')).toBeNull();
    expect(page.querySelector('[data-testid="courtesy-note"]')!.textContent).toContain('no payment is recorded');
  });

  it('formats absolute IST deadlines whatever the browser zone is', () => {
    expect(formatBillingInstant('2027-02-28T04:30:00Z', 'en-IN')).toBe('28 Feb 2027, 10:00 IST');
    expect(formatBillingInstant('2027-03-30T20:30:00Z', 'en-IN')).toBe('31 Mar 2027, 02:00 IST');
    expect(formatBillingInstant(null, 'en-IN')).toBeNull();
  });

  it('rendersRoleAppropriateStatusAndClearsStaleScope', () => {
    const late = new Subject<ShopSubscriptionStatus>();
    const shop = vi.fn((id: string) => id === shopA ? late : of(status(shopB, { state: 'Expired', term: { ...term, state: 'Expired' } })));
    const lateAuthority = new Subject<ShopTradingAuthority>();
    const outageB = new Subject<ShopTradingAuthority>();
    const tradingAuthority = vi.fn((id: string) => id === shopA ? lateAuthority : outageB);
    const { fixture, page, session } = render({ shop, tradingAuthority });
    expect(shop).toHaveBeenCalledWith(shopA);
    expect(tradingAuthority).toHaveBeenCalledWith(shopA);

    // The shop changes before shop A answers: A's late answer must never repopulate B's page.
    session.set({ user: { id: 'u1' }, activeShopId: shopB });
    fixture.detectChanges();
    expect(tradingAuthority).toHaveBeenCalledWith(shopB);
    expect(page.querySelector(authorityDeadline)).toBeNull();
    outageB.error(new TradingAuthorityReadError('unavailable'));
    fixture.detectChanges();
    expect(page.querySelector(authorityOutage)).toBeTruthy();

    late.next(status(shopA, { term: { ...term, commercial: { pricePaise: 149900, currency: 'INR', planVersionId: null } }, accountId: account }));
    lateAuthority.next(authority(shopA));
    fixture.detectChanges();
    expect(page.textContent).not.toContain('₹1,499.00');
    expect(page.querySelector(authorityOutage)).toBeTruthy();
    expect(page.querySelector(authorityDeadline)).toBeNull();
    expect(page.querySelector('[data-testid="status-state"]')?.textContent).toContain('Expired');

    const facts = page.querySelector('[data-testid="status-term"]')!.textContent!;
    expect(facts).toContain('28 Feb 2027, 10:00 IST');
    expect(facts).toContain('7 Mar 2027, 10:00 IST');
    expect(facts).toContain('3');
    expect(facts).toContain('5');
    expect(page.querySelector('[data-testid="observe-only"]')?.textContent).toContain('Observe-only subscription dates and commercial terms');
    expect(page.querySelector('[data-testid="commercial-price"]')).toBeNull();
  });

  it('shows the commercial subview only when the server includes it', () => {
    const shop = () => of(status(shopA, { accountId: account, term: { ...term, commercial: { pricePaise: 149900, currency: 'INR', planVersionId: null } } }));
    const { page } = render({ shop });
    expect(page.querySelector('[data-testid="commercial-price"]')?.textContent).toContain('₹1,499.00');
  });

  it('keeps an outage distinct from expiry and clears the previous status', () => {
    let fail = false;
    const shop = vi.fn(() => fail ? throwError(() => new StatusReadError('unavailable')) : of(status(shopA)));
    const { fixture, page } = render({ shop });
    expect(page.querySelector('[data-testid="status-state"]')?.textContent).toContain('Active');
    fail = true;
    (page.querySelector('[data-testid="status-retry"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(page.querySelector('[data-testid="verification-unavailable"]')?.textContent).toContain('not an expiry');
    expect(page.querySelector('[data-testid="status-state"]')).toBeNull();
    expect(page.textContent).not.toContain('28 Feb 2027');
  });

  it('labels a Complete Trial as having no grace period', () => {
    const trial = { ...term, basis: 'Trial' as const, graceEndsAt: null };
    const { page } = render({ shop: () => of(status(shopA, { term: trial })) });
    expect(page.querySelector('[data-testid="grace-ends"]')?.textContent).toContain('No grace period');
  });

  it('coalesces visibilitychange and focus while revalidating', () => {
    const pending = new Subject<ShopSubscriptionStatus>();
    const shop = vi.fn().mockReturnValueOnce(of(status(shopA))).mockReturnValue(pending);
    render({ shop });
    const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
    try {
      document.dispatchEvent(new Event('visibilitychange'));
      window.dispatchEvent(new Event('focus'));
      expect(shop).toHaveBeenCalledTimes(2);
      pending.next(status(shopA));
      pending.complete();
      window.dispatchEvent(new Event('focus'));
      expect(shop).toHaveBeenCalledTimes(3);
    } finally {
      visibility.mockRestore();
    }
  });

  it('labels incomplete legacy evidence instead of inventing grace', () => {
    const incomplete = { ...term, state: 'EvidenceIncomplete' as const, evidenceState: 'Incomplete' as const, basis: null,
      graceEndsAt: null, accessEndsAt: null, nextDeadline: null, memberAllowance: null };
    const { page } = render({ shop: () => of(status(shopA, { state: 'EvidenceIncomplete', term: incomplete })) });
    expect(page.textContent).toContain('Some subscription facts are not recorded');
    expect(page.querySelector('[data-testid="grace-ends"]')?.textContent).toContain('Not recorded');
  });

  it('reads every account page for a billing-only member without a shop', () => {
    const accountRead = vi.fn(() => of({ evaluatedAt: '2027-02-10T00:00:00Z', items: [
      { status: term, coveredShops: [{ shopId: shopA, shopName: 'Fixture Shop' }], coveredShopCount: 1 },
    ] }));
    const { page } = render({ account: accountRead, shop: vi.fn() }, { accountId: account });
    expect(accountRead).toHaveBeenCalledWith(account);
    expect(page.querySelector('[data-testid="account-term"]')?.textContent).toContain('Fixture Shop');
  });

  it('offers withdrawal only for an account term whose cancellation is still scheduled', () => {
    const cancellation = { changeId: 'change', version: 1, effectiveAt: term.paidThrough, policyVersion: 'term-boundary/1' };
    const scheduled = { ...term, graceEndsAt: null, accessEndsAt: term.paidThrough, cancellation };
    const accountRead = vi.fn(() => of({ evaluatedAt: '2027-02-10T00:00:00Z', items: [
      { status: scheduled, coveredShops: [], coveredShopCount: 0 },
      { status: { ...term, termId: '55555555-5555-4555-8555-555555555555' }, coveredShops: [], coveredShopCount: 0 },
    ] }));
    const { page } = render({ account: accountRead, shop: vi.fn() }, { accountId: account });
    const terms = page.querySelectorAll('[data-testid="account-term"]');
    expect(terms[0].querySelector('[data-testid="withdrawal-review"]')).toBeTruthy();
    expect(terms[1].querySelector('[data-testid="withdrawal-review"]')).toBeNull();
  });
});
