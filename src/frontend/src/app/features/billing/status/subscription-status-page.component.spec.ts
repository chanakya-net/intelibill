import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { Subject, of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import en from '../../../../../public/assets/i18n/en-IN.json';
import { AuthService } from '../../../core/auth/auth.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { billingRoutes } from '../billing.routes';
import { SubscriptionStatusPageComponent } from './subscription-status-page.component';
import { formatBillingInstant, type ShopSubscriptionStatus, type SubscriptionTermStatus } from './subscription-status.models';
import { StatusReadError, SubscriptionStatusService } from './subscription-status.service';

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

function render(api: Partial<SubscriptionStatusService>, params: Record<string, string> = {}) {
  const session = signal<{ user: { id: string }; activeShopId: string | null } | null>({ user: { id: 'u1' }, activeShopId: shopA });
  TestBed.configureTestingModule({
    imports: [TranslocoTestingModule.forRoot({
      langs: { 'en-IN': en }, translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' }, preloadLangs: true,
    })],
    providers: [
      provideRouter([]),
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(params) } } },
      { provide: LocalizationService, useValue: { currentLanguage: signal('en-IN') } },
      { provide: AuthService, useValue: { session } },
      { provide: SubscriptionStatusService, useValue: api },
    ],
  });
  const fixture = TestBed.createComponent(SubscriptionStatusPageComponent);
  fixture.detectChanges();
  return { fixture, page: fixture.nativeElement as HTMLElement, session };
}

afterEach(() => TestBed.resetTestingModule());

describe('subscription status', () => {
  it('has shop and account destinations before the billing landing route', () => {
    const paths = billingRoutes.map(route => route.path);
    expect(paths).toContain('status');
    expect(paths).toContain('accounts/:accountId/status');
    expect(paths.indexOf('status')).toBeLessThan(paths.indexOf(''));
  });

  it('formats absolute IST deadlines whatever the browser zone is', () => {
    expect(formatBillingInstant('2027-02-28T04:30:00Z', 'en-IN')).toBe('28 Feb 2027, 10:00 IST');
    expect(formatBillingInstant('2027-03-30T20:30:00Z', 'en-IN')).toBe('31 Mar 2027, 02:00 IST');
    expect(formatBillingInstant(null, 'en-IN')).toBeNull();
  });

  it('rendersRoleAppropriateStatusAndClearsStaleScope', () => {
    const late = new Subject<ShopSubscriptionStatus>();
    const shop = vi.fn((id: string) => id === shopA ? late : of(status(shopB, { state: 'Expired', term: { ...term, state: 'Expired' } })));
    const { fixture, page, session } = render({ shop });
    expect(shop).toHaveBeenCalledWith(shopA);

    // The shop changes before shop A answers: A's late answer must never repopulate B's page.
    session.set({ user: { id: 'u1' }, activeShopId: shopB });
    fixture.detectChanges();
    late.next(status(shopA, { term: { ...term, commercial: { pricePaise: 149900, currency: 'INR', planVersionId: null } }, accountId: account }));
    fixture.detectChanges();
    expect(page.textContent).not.toContain('₹1,499.00');
    expect(page.querySelector('[data-testid="status-state"]')?.textContent).toContain('Expired');

    const facts = page.querySelector('[data-testid="status-term"]')!.textContent!;
    expect(facts).toContain('28 Feb 2027, 10:00 IST');
    expect(facts).toContain('7 Mar 2027, 10:00 IST');
    expect(facts).toContain('3');
    expect(facts).toContain('5');
    expect(page.querySelector('[data-testid="observe-only"]')?.textContent).toContain('does not block');
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
});
