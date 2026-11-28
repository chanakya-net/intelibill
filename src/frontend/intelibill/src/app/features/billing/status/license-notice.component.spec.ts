import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { EMPTY, Subject, of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import en from '../../../../../public/assets/i18n/en-IN.json';
import { AuthService } from '../../../core/auth/auth.service';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { LicenseNoticeComponent } from './license-notice.component';
import type { AccountLicenseNotices, ShopLicenseNotices } from './license-notice.models';
import { LicenseNoticeReadError, LicenseNoticeService } from './license-notice.service';

const shopA = '11111111-1111-4111-8111-111111111111';
const shopB = '22222222-2222-4222-8222-222222222222';
const accountA = '33333333-3333-4333-8333-333333333333';
const accountB = '55555555-5555-4555-8555-555555555555';

function shopNotices(
  shopId: string,
  shopName = 'North Shop',
  options: { nextDueAt?: string | null; guidanceCode?: string; accessState?: string; evaluatedAt?: string } = {},
): ShopLicenseNotices {
  return {
    schemaVersion: 1,
    shopId,
    evaluatedAt: options.evaluatedAt ?? '2028-02-22T04:30:00Z',
    notices: [
      {
        noticeId: `notice-${shopId}`,
        shopId,
        purpose: options.guidanceCode?.startsWith('Grace') ? 'GraceNotice' : 'PaidRenewalReminder',
        threshold: options.guidanceCode === 'GraceEnding' ? 'GraceMinus1Day' : 'PaidMinus7Days',
        accessState: options.accessState ?? 'Active',
        guidanceCode: options.guidanceCode ?? 'PaidRenewalDue',
        templateKey: 'notice.paid.minus7',
        dueAt: '2028-02-22T04:30:00Z',
        effectiveDeadlineAt: '2028-02-29T04:30:00Z',
        timeZone: 'Asia/Kolkata',
        locale: 'en-IN',
        shopName,
      },
    ],
    nextDueAt: options.nextDueAt ?? null,
    nextCursor: null,
  };
}

function accountNotices(
  accountId: string,
  options: {
    suspended?: boolean;
    guidanceCode?: string;
    coveredShopCount?: number;
    coveredShopNames?: string[];
    evaluatedAt?: string;
    nextDueAt?: string | null;
  } = {},
): AccountLicenseNotices {
  const names = options.coveredShopNames ?? ['North Shop', 'South Shop'];
  const count = options.coveredShopCount ?? names.length;
  return {
    schemaVersion: 1,
    accountId,
    evaluatedAt: options.evaluatedAt ?? '2028-02-22T04:30:00Z',
    notices: [
      {
        noticeId: `acc-notice-${accountId}`,
        accountId,
        purpose: 'PaidRenewalReminder',
        sourceKind: 'Paid',
        sourceId: '44444444-4444-4444-8444-444444444444',
        threshold: 'PaidMinus7Days',
        accessState: options.suspended ? 'Suspended' : 'Active',
        guidanceCode: options.guidanceCode ?? (options.suspended ? 'SuspendedNoPaymentLift' : 'PaidRenewalDue'),
        templateKey: 'notice.paid.minus7',
        dueAt: '2028-02-22T04:30:00Z',
        effectiveDeadlineAt: '2028-02-29T04:30:00Z',
        timeZone: 'Asia/Kolkata',
        locale: 'en-IN',
        allowedChannels: ['InApp', 'Email'],
        coveredShopCount: count,
        coveredShopNames: names,
        coveredShopNamesTruncated: count > names.length,
        evidencedTermPricePaise: 12000,
        evidencedTermCurrency: 'INR',
        renewalQuoteRequired: true,
        paymentDoesNotLiftSuspension: Boolean(options.suspended),
      },
    ],
    nextDueAt: options.nextDueAt ?? null,
    nextCursor: null,
  };
}

function setup(
  api: Partial<LicenseNoticeService>,
  mode: 'shop' | 'account' = 'shop',
  accountId: string | null = null,
) {
  const session = signal<{ user: { id: string }; activeShopId: string | null } | null>({
    user: { id: 'u1' },
    activeShopId: shopA,
  });
  const transitioning = signal(false);
  const dataRestricted = signal(false);

  TestBed.configureTestingModule({
    imports: [
      TranslocoTestingModule.forRoot({
        langs: { 'en-IN': en },
        translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' },
        preloadLangs: true,
      }),
    ],
    providers: [
      { provide: AuthService, useValue: { session } },
      { provide: ShopContextService, useValue: { transitioning, dataRestricted } },
      { provide: LocalizationService, useValue: { currentLanguage: signal('en-IN') } },
      { provide: LicenseNoticeService, useValue: { shop: () => EMPTY, account: () => EMPTY, ...api } },
    ],
  });

  const fixture = TestBed.createComponent(LicenseNoticeComponent);
  fixture.componentRef.setInput('mode', mode);
  fixture.componentRef.setInput('accountId', accountId);
  fixture.detectChanges();

  return { fixture, el: fixture.nativeElement as HTMLElement, session, transitioning, dataRestricted };
}

afterEach(() => {
  vi.useRealTimers();
  TestBed.resetTestingModule();
});

describe('LicenseNoticeComponent', () => {
  it('renders operational shop guidance for staff without commercial fields or renewal prompts and clears on switch/logout', async () => {
    const shopRead = vi.fn((id: string) =>
      of(
        id === shopA
          ? shopNotices(shopA, 'North Shop', { guidanceCode: 'PaidRenewalDue' })
          : shopNotices(shopB, 'South Shop', { guidanceCode: 'GraceEnding', accessState: 'Grace' }),
      ),
    );
    const { fixture, el, session } = setup({ shop: shopRead }, 'shop');
    await fixture.whenStable();
    fixture.detectChanges();

    const paidGuidance = el.querySelector('[data-testid="shop-license-notice-guidance"]')?.textContent ?? '';
    expect(el.querySelector('[data-testid="shop-license-notice"]')).not.toBeNull();
    expect(el.textContent).toContain('North Shop');
    expect(el.textContent).toContain('29 Feb 2028, 10:00:00 IST');
    expect(paidGuidance).toContain('Contact the account billing owner or manager');
    expect(paidGuidance.toLowerCase()).not.toContain('renew');
    expect(el.textContent).not.toContain('120');
    expect(el.textContent).not.toContain('INR');

    session.set({ user: { id: 'u1' }, activeShopId: shopB });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const graceGuidance = el.querySelector('[data-testid="shop-license-notice-guidance"]')?.textContent ?? '';
    expect(el.textContent).toContain('South Shop');
    expect(graceGuidance).toContain('Contact the account billing owner or manager immediately');
    expect(graceGuidance.toLowerCase()).not.toContain('renew');

    session.set(null);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="shop-license-notice"]')).toBeNull();
  });

  it('drops a late response from an earlier shop after switching scopes (A -> B -> A race)', async () => {
    const delayedA = new Subject<ShopLicenseNotices>();
    const shopRead = vi.fn((id: string) =>
      id === shopA ? delayedA.asObservable() : of(shopNotices(shopB, 'South Shop')),
    );
    const { fixture, el, session } = setup({ shop: shopRead }, 'shop');

    session.set({ user: { id: 'u1' }, activeShopId: shopB });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.textContent).toContain('South Shop');

    delayedA.next(shopNotices(shopA, 'Stale North Shop'));
    delayedA.complete();
    await Promise.resolve();
    fixture.detectChanges();
    expect(el.textContent).not.toContain('Stale North Shop');
    expect(el.textContent).toContain('South Shop');
  });

  it('revalidates on focus, online, and nextDueAt timer boundary', async () => {
    vi.useFakeTimers();
    const now = new Date('2028-02-22T04:29:55Z');
    vi.setSystemTime(now);
    const shopRead = vi
      .fn()
      .mockReturnValueOnce(
        of({
          schemaVersion: 1,
          shopId: shopA,
          evaluatedAt: now.toISOString(),
          notices: [],
          nextDueAt: '2028-02-22T04:30:00Z',
          nextCursor: null,
        } satisfies ShopLicenseNotices),
      )
      .mockReturnValue(of(shopNotices(shopA, 'North Shop')));
    const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
    try {
      const { fixture, el } = setup({ shop: shopRead }, 'shop');
      await Promise.resolve();
      fixture.detectChanges();
      expect(el.querySelector('[data-testid="shop-license-notice"]')).toBeNull();

      await vi.advanceTimersByTimeAsync(6_000);
      fixture.detectChanges();
      expect(shopRead).toHaveBeenCalledTimes(2);
      expect(el.querySelector('[data-testid="shop-license-notice"]')).not.toBeNull();

      window.dispatchEvent(new Event('online'));
      await Promise.resolve();
      expect(shopRead).toHaveBeenCalledTimes(3);

      window.dispatchEvent(new Event('focus'));
      await Promise.resolve();
      expect(shopRead).toHaveBeenCalledTimes(4);
    } finally {
      visibility.mockRestore();
    }
  });

  it('arms multi-day shop timer using server evaluatedAt despite skewed client clock and cancels on scope change', async () => {
    vi.useFakeTimers();
    // Client system clock is skewed +5 days ahead of the server evaluation instant.
    vi.setSystemTime(new Date('2028-02-24T04:30:00Z'));
    const shopRead = vi
      .fn()
      .mockReturnValueOnce(
        of({
          schemaVersion: 1,
          shopId: shopA,
          evaluatedAt: '2028-02-19T04:30:00Z',
          notices: [],
          nextDueAt: '2028-02-22T04:30:00Z',
          nextCursor: null,
        } satisfies ShopLicenseNotices),
      )
      .mockReturnValueOnce(of(shopNotices(shopA, 'North Shop')))
      .mockReturnValue(of(shopNotices(shopB, 'South Shop')));

    const { fixture, el, session } = setup({ shop: shopRead }, 'shop');
    await Promise.resolve();
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="shop-license-notice"]')).toBeNull();

    // Advance 71h59m without focus/online events: still before the 3-day server deadline.
    await vi.advanceTimersByTimeAsync(71 * 60 * 60 * 1_000 + 59 * 60 * 1_000);
    expect(shopRead).toHaveBeenCalledTimes(1);

    // Cross the 72h server deadline -> notice fetches and appears automatically.
    await vi.advanceTimersByTimeAsync(2 * 60 * 1_000);
    fixture.detectChanges();
    expect(shopRead).toHaveBeenCalledTimes(2);
    expect(el.textContent).toContain('North Shop');

    // Old scope's armed timer must never populate a switched shop view.
    shopRead.mockReturnValueOnce(
      of({
        schemaVersion: 1,
        shopId: shopB,
        evaluatedAt: '2028-02-19T04:30:00Z',
        notices: [],
        nextDueAt: '2028-02-22T04:30:00Z',
        nextCursor: null,
      } satisfies ShopLicenseNotices),
    );
    session.set({ user: { id: 'u1' }, activeShopId: shopB });
    fixture.detectChanges();
    await Promise.resolve();
    session.set({ user: { id: 'u1' }, activeShopId: shopA });
    fixture.detectChanges();
    await Promise.resolve();
    await vi.advanceTimersByTimeAsync(73 * 60 * 60 * 1_000);
    fixture.detectChanges();
    expect(el.textContent).not.toContain('North ShopStale');
  });

  it('arms multi-day account timer despite skewed client clock and cancels old account timer on switch', async () => {
    vi.useFakeTimers();
    // Client clock is skewed -4 days behind the server evaluation instant.
    vi.setSystemTime(new Date('2028-02-15T04:30:00Z'));
    const accountRead = vi
      .fn()
      .mockReturnValueOnce(
        of({
          schemaVersion: 1,
          accountId: accountA,
          evaluatedAt: '2028-02-19T04:30:00Z',
          notices: [],
          nextDueAt: '2028-02-22T04:30:00Z',
          nextCursor: null,
        } satisfies AccountLicenseNotices),
      )
      .mockReturnValueOnce(of(accountNotices(accountA)))
      .mockReturnValue(of(accountNotices(accountB, { coveredShopNames: ['Beta Shop'] })));

    const { fixture, el } = setup({ account: accountRead }, 'account', accountA);
    await Promise.resolve();
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="account-license-notices-upcoming"]')).not.toBeNull();

    await vi.advanceTimersByTimeAsync(72 * 60 * 60 * 1_000 + 2_000);
    fixture.detectChanges();
    expect(accountRead).toHaveBeenCalledTimes(2);
    expect(el.querySelector('[data-testid="account-license-notice"]')).not.toBeNull();

    // Switch to account B: old account A timer cannot overwrite account B.
    fixture.componentRef.setInput('accountId', accountB);
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();
    expect(el.textContent).toContain('Beta Shop');
    expect(el.textContent).not.toContain('North Shop');
  });

  it('renders commercial notices with partial-suspension qualifier, bounded shop sample, and quote note', async () => {
    const accountRead = vi.fn(() =>
      of(
        accountNotices(accountA, {
          suspended: true,
          guidanceCode: 'PaidRenewalPartialSuspension',
          coveredShopCount: 1200,
          coveredShopNames: ['Shop 1', 'Shop 2', 'Shop 3', 'Shop 4', 'Shop 5'],
        }),
      ),
    );
    const { fixture, el } = setup({ account: accountRead }, 'account', accountA);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('[data-testid="account-license-notice"]')).not.toBeNull();
    expect(el.textContent).toContain('At least one covered shop is suspended');
    expect(el.textContent).not.toContain('keep trading uninterrupted');
    expect(el.querySelector('[data-testid="account-notice-shops"]')?.textContent).toContain('+1195 more (total: 1200)');
    expect(el.querySelector('[data-testid="account-notice-price"]')?.textContent).toContain('120');
    expect(el.querySelector('[data-testid="account-notice-quote-note"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="account-notice-suspension-note"]')).not.toBeNull();
  });

  it('shows failure message when selected-account notices read is denied', async () => {
    const { fixture, el } = setup(
      { account: () => throwError(() => new LicenseNoticeReadError('denied')) },
      'account',
      accountA,
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('[data-testid="account-license-notices-error"]')).not.toBeNull();
  });
});
