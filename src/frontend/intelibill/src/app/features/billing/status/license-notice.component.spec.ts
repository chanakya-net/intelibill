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

function shopNotices(shopId: string, shopName = 'North Shop', nextDueAt: string | null = null): ShopLicenseNotices {
  return {
    schemaVersion: 1,
    shopId,
    evaluatedAt: '2028-02-22T04:30:00Z',
    notices: [
      {
        noticeId: `notice-${shopId}`,
        shopId,
        purpose: 'PaidRenewalReminder',
        threshold: 'PaidMinus7Days',
        accessState: 'Active',
        guidanceCode: 'PaidRenewalDue',
        templateKey: 'notice.paid.minus7',
        dueAt: '2028-02-22T04:30:00Z',
        effectiveDeadlineAt: '2028-02-29T04:30:00Z',
        timeZone: 'Asia/Kolkata',
        locale: 'en-IN',
        shopName,
      },
    ],
    nextDueAt,
    nextCursor: null,
  };
}

function accountNotices(accountId: string, suspended = false): AccountLicenseNotices {
  return {
    schemaVersion: 1,
    accountId,
    evaluatedAt: '2028-02-22T04:30:00Z',
    notices: [
      {
        noticeId: `acc-notice-${accountId}`,
        accountId,
        purpose: 'PaidRenewalReminder',
        sourceKind: 'Paid',
        sourceId: '44444444-4444-4444-8444-444444444444',
        threshold: 'PaidMinus7Days',
        accessState: suspended ? 'Suspended' : 'Active',
        guidanceCode: suspended ? 'SuspendedNoPaymentLift' : 'PaidRenewalDue',
        templateKey: 'notice.paid.minus7',
        dueAt: '2028-02-22T04:30:00Z',
        effectiveDeadlineAt: '2028-02-29T04:30:00Z',
        timeZone: 'Asia/Kolkata',
        locale: 'en-IN',
        allowedChannels: ['InApp', 'Email'],
        coveredShopCount: 2,
        coveredShopNames: ['North Shop', 'South Shop'],
        evidencedTermPricePaise: 12000,
        evidencedTermCurrency: 'INR',
        renewalQuoteRequired: true,
        paymentDoesNotLiftSuspension: suspended,
      },
    ],
    nextDueAt: null,
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
  it('renders operational shop notice without commercial fields and clears on shop switch and logout', async () => {
    const shopRead = vi.fn((id: string) =>
      of(id === shopA ? shopNotices(shopA, 'North Shop') : shopNotices(shopB, 'South Shop')),
    );
    const { fixture, el, session } = setup({ shop: shopRead }, 'shop');
    await fixture.whenStable();
    fixture.detectChanges();

    const text = el.textContent ?? '';
    expect(el.querySelector('[data-testid="shop-license-notice"]')).not.toBeNull();
    expect(text).toContain('North Shop');
    expect(text).toContain('29 Feb 2028, 10:00:00 IST');
    expect(text).not.toContain('120');
    expect(text).not.toContain('INR');
    expect(text).not.toContain('South Shop');

    session.set({ user: { id: 'u1' }, activeShopId: shopB });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.textContent).toContain('South Shop');
    expect(el.textContent).not.toContain('North Shop');

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

    // Late answer from earlier shop A must be ignored.
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

  it('renders selected-account commercial notices with evidenced price, quote note, and suspension warning', async () => {
    const accountRead = vi.fn(() => of(accountNotices(accountA, true)));
    const { fixture, el } = setup({ account: accountRead }, 'account', accountA);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('[data-testid="account-license-notice"]')).not.toBeNull();
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
