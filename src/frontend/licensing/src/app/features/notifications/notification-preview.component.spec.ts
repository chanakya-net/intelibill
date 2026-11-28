import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SESSION_PORT, type SessionState } from '@intelibill/api';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { Subject, of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import en from '../../../assets/i18n/en-IN.json';
import hi from '../../../assets/i18n/hi-IN.json';
import { ADMIN_SUPPORTED_LANGUAGE_CODES } from '../../core/i18n/admin-languages';
import {
  NotificationPreviewApi,
  type ProviderNotificationItem,
  type ProviderNotificationsPage,
} from './notification-preview-api.service';
import { NotificationPreviewComponent } from './notification-preview.component';

const accountA = '33333333-3333-4333-8333-333333333333';
const accountB = '55555555-5555-4555-8555-555555555555';

function makePage(
  accountId: string,
  commercialIncluded: boolean,
  options: {
    dueAt?: string;
    effectiveDeadlineAt?: string;
    nextCursor?: string | null;
    prefix?: string;
    coveredShopCount?: number;
    coveredShopNames?: string[];
  } = {},
): ProviderNotificationsPage {
  const prefix = options.prefix ?? `job-${accountId}`;
  const dueAt = options.dueAt ?? '2028-02-22T04:30:00Z';
  const deadline = options.effectiveDeadlineAt ?? '2028-02-29T04:30:00Z';
  const names = options.coveredShopNames ?? ['North Shop', 'South Shop'];
  const count = options.coveredShopCount ?? names.length;
  const first = scheduledRow(accountId, prefix, dueAt, deadline, names, count, commercialIncluded);
  const second: ProviderNotificationItem = {
    ...first,
    jobId: `${prefix}-2`,
    threshold: 'PaidMinus1Day',
    isDue: false,
    dueAt: '2028-02-28T04:30:00Z',
    templateKey: 'notice.paid.minus1',
    commercial: null,
  };
  return { schemaVersion: 1, accountId, evaluatedAt: dueAt, commercialIncluded, items: [first, second], nextCursor: options.nextCursor ?? null };
}

function scheduledRow(
  accountId: string, prefix: string, dueAt: string, deadline: string, names: string[], count: number, commercialIncluded: boolean,
): ProviderNotificationItem {
  return {
    jobId: `${prefix}-1`,
    scopeKind: 'Account',
    scopeId: accountId,
    audience: 'CommercialBillingRole',
    purpose: 'PaidRenewalReminder',
    sourceKind: 'Paid',
    sourceId: '44444444-4444-4444-8444-444444444444',
    sourceRevision: 'v1:abc',
    threshold: 'PaidMinus7Days',
    status: 'Scheduled',
    isDue: true,
    dueAt,
    effectiveDeadlineAt: deadline,
    timeZone: 'Asia/Kolkata',
    locale: 'en-IN',
    allowedChannels: ['InApp', 'Email'],
    templateKey: 'notice.paid.minus7',
    guidanceCode: 'PaidRenewalDue',
    coveredShopCount: count,
    paymentDoesNotLiftSuspension: false,
    plannedAt: '2028-01-31T04:30:00Z',
    commercial: commercialIncluded
      ? {
          evidencedTermPricePaise: 12000,
          evidencedTermCurrency: 'INR',
          renewalQuoteRequired: true,
          destinationHint: 'o***@example.test',
          recipientReference: 'Verified billing role',
          coveredShopNames: names,
          coveredShopNamesTruncated: count > names.length,
        }
      : null,
  };
}

function setup(read: NotificationPreviewApi['read'], grants: string[] = ['billing'], accountId = accountA) {
  const capabilities = signal(grants);
  const state = signal<SessionState>('authenticated');
  TestBed.configureTestingModule({
    imports: [
      TranslocoTestingModule.forRoot({
        langs: { 'en-IN': en, 'hi-IN': hi },
        translocoConfig: { availableLangs: ['en-IN', 'hi-IN'], defaultLang: 'en-IN', reRenderOnLangChange: true },
        preloadLangs: true,
      }),
    ],
    providers: [
      { provide: SESSION_PORT, useValue: { state, capabilities } },
      { provide: NotificationPreviewApi, useValue: { read } },
    ],
  });

  const fixture = TestBed.createComponent(NotificationPreviewComponent);
  fixture.componentRef.setInput('account', accountId);
  fixture.detectChanges();
  return { fixture, el: fixture.nativeElement as HTMLElement, capabilities, state };
}

afterEach(() => TestBed.resetTestingModule());

describe('NotificationPreviewComponent', () => {
  it.each([
    ['SourceNoLongerCurrent', 'The agreement changed.'],
    ['ScopeClosed', 'The shop or account closed.'],
    ['WindowElapsed', 'The reminder window ended.'],
    ['RestrictionChanged', 'Current restrictions prevent this reminder.'],
    ['RecipientIneligible', 'The recipient is no longer eligible.'],
    ['unexpected private value', 'No further detail is available.'],
    [undefined, 'No further detail is available.'],
  ])('renders localized suppression reasons for %s', async (suppressionReasonCode, expected) => {
    const page = makePage(accountA, true);
    const emailDelivery = {
      state: 'Suppressed', attempts: 1, lastAttemptAt: page.evaluatedAt, outcomeAt: page.evaluatedAt,
      nextAttemptAt: null, captureUrl: null, suppressionReasonCode,
    };
    const item = { ...page.items[0], emailDelivery };
    const { fixture, el, capabilities } = setup(() => of({ ...page, items: [item] }));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="email-reason"]')?.textContent?.trim()).toBe(expected);
    expect(el.textContent).not.toContain('unexpected private value');
    capabilities.set(['support']);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="email-detail"]')).toBeNull();
  });

  it('shows the local capture with a text outcome only while the live capability allows it', async () => {
    const page = makePage(accountA, true);
    const href = 'http://localhost:28025/search?q=message-id%3A11111111111111111111111111111111%40notices.intelibill.invalid';
    const item = { ...page.items[0], emailDelivery: {
      state: 'Uncertain', attempts: 1, lastAttemptAt: page.evaluatedAt, outcomeAt: page.evaluatedAt, nextAttemptAt: null, captureUrl: href,
    }};
    const { fixture, el, capabilities } = setup(() => of({ ...page, items: [item] }));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="email-state"]')?.textContent).toContain('SMTP outcome unknown');
    expect(el.querySelector('a[data-testid="mailpit-capture"]')?.getAttribute('href')).toBe(href);
    capabilities.set(['support']);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(el.querySelector('a[data-testid="mailpit-capture"]')).toBeNull();
  });

  it('shows commercial preview with exact microsecond UTC and IST instants, bounded shops, and due/future badges', async () => {
    const read = vi.fn(() =>
      of(
        makePage(accountA, true, {
          dueAt: '2028-02-22T04:30:00.123456Z',
          effectiveDeadlineAt: '2028-02-29T04:30:00.123456Z',
          coveredShopCount: 1200,
          coveredShopNames: ['Shop 1', 'Shop 2', 'Shop 3', 'Shop 4', 'Shop 5'],
        }),
      ),
    );
    const { fixture, el } = setup(read, ['billing']);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(read).toHaveBeenCalledWith(accountA, null);
    expect(el.querySelectorAll('[data-testid="provider-notification-row"]').length).toBe(2);
    expect(el.querySelector('[data-testid="provider-notification-due-utc"]')?.textContent).toContain(
      '22 Feb 2028, 04:30:00.123456 UTC',
    );
    expect(el.querySelector('[data-testid="provider-notification-due-ist"]')?.textContent).toContain(
      '22 Feb 2028, 10:00:00.123456 IST',
    );
    expect(el.querySelector('[data-testid="provider-notification-shops"]')?.textContent).toContain('+1195 more (total: 1200)');
    expect(el.querySelector('[data-testid="provider-notification-price"]')?.textContent).toContain('120');
    const badges = Array.from(el.querySelectorAll('[data-testid="provider-notification-due-badge"]')).map((node) =>
      node.textContent?.trim(),
    );
    expect(badges).toEqual(['Due', 'Scheduled (future)']);
  });

  it('redacts commercial details for Support and never leaks prices or destination hints', async () => {
    const { fixture, el } = setup(() => of(makePage(accountA, false)), ['support']);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('[data-testid="provider-notifications-redacted"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="provider-notification-commercial"]')).toBeNull();
    expect(el.textContent).not.toContain('120');
    expect(el.textContent).not.toContain('o***@example.test');
  });

  it('keeps newer redacted or denied focus response authoritative when an older commercial response finishes last', async () => {
    const delayedCommercial = new Subject<ProviderNotificationsPage>();
    const newerRedacted = new Subject<ProviderNotificationsPage>();
    const read = vi
      .fn()
      .mockReturnValueOnce(delayedCommercial.asObservable())
      .mockReturnValueOnce(newerRedacted.asObservable());
    const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
    try {
      const { fixture, el } = setup(read, ['billing'], accountA);
      window.dispatchEvent(new Event('focus'));

      // Newer Support-redacted response arrives first (same account, local capability token unchanged).
      newerRedacted.next(makePage(accountA, false, { prefix: 'redacted' }));
      newerRedacted.complete();
      await Promise.resolve();
      fixture.detectChanges();
      expect(el.querySelector('[data-testid="provider-notifications-redacted"]')).not.toBeNull();
      expect(el.querySelector('[data-testid="provider-notification-commercial"]')).toBeNull();

      // Older commercial Billing response finishes last -> must be rejected!
      delayedCommercial.next(makePage(accountA, true, { prefix: 'stale-commercial' }));
      delayedCommercial.complete();
      await Promise.resolve();
      fixture.detectChanges();
      expect(el.querySelector('[data-testid="provider-notification-commercial"]')).toBeNull();
      expect(el.querySelector('[data-job-id="stale-commercial-1"]')).toBeNull();
      expect(el.querySelector('[data-testid="provider-notifications-redacted"]')).not.toBeNull();
    } finally {
      visibility.mockRestore();
    }
  });

  it('rejects delayed second-page commercial load after newer 403 and clears page 1 on mid-pagination downgrade', async () => {
    const delayedPage2 = new Subject<ProviderNotificationsPage>();
    const focusDenied = new Subject<ProviderNotificationsPage>();
    const read = vi
      .fn()
      .mockReturnValueOnce(of(makePage(accountA, true, { nextCursor: 'cursor-1', prefix: 'page1' })))
      .mockReturnValueOnce(delayedPage2.asObservable())
      .mockReturnValueOnce(focusDenied.asObservable());
    const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
    try {
      const { fixture, el } = setup(read, ['billing'], accountA);
      await fixture.whenStable();
      fixture.detectChanges();

      el.querySelector<HTMLButtonElement>('[data-testid="provider-notifications-more"]')!.click();
      window.dispatchEvent(new Event('focus'));

      focusDenied.error(new HttpErrorResponse({ status: 403 }));
      await Promise.resolve();
      fixture.detectChanges();
      expect(el.querySelector('[data-testid="provider-notifications-denied"]')).not.toBeNull();
      expect(el.querySelectorAll('[data-testid="provider-notification-row"]').length).toBe(0);

      // Delayed commercial page 2 completes after 403 -> must NOT restore rows!
      delayedPage2.next(makePage(accountA, true, { prefix: 'page2' }));
      delayedPage2.complete();
      await Promise.resolve();
      fixture.detectChanges();
      expect(el.querySelector('[data-testid="provider-notifications-denied"]')).not.toBeNull();
      expect(el.querySelectorAll('[data-testid="provider-notification-row"]').length).toBe(0);
    } finally {
      visibility.mockRestore();
    }

    // Same-account mid-pagination downgrade: page 1 commercial -> page 2 redacted -> clears page 1 & reloads redacted!
    TestBed.resetTestingModule();
    const downgradeRead = vi
      .fn()
      .mockReturnValueOnce(of(makePage(accountA, true, { nextCursor: 'cursor-1', prefix: 'comm-p1' })))
      .mockReturnValueOnce(of(makePage(accountA, false, { prefix: 'redacted-p2' })))
      .mockReturnValueOnce(of(makePage(accountA, false, { prefix: 'redacted-p1' })));
    const downgraded = setup(downgradeRead, ['billing'], accountA);
    await downgraded.fixture.whenStable();
    downgraded.fixture.detectChanges();
    expect(downgraded.el.querySelector('[data-testid="provider-notification-commercial"]')).not.toBeNull();

    downgraded.el.querySelector<HTMLButtonElement>('[data-testid="provider-notifications-more"]')!.click();
    await downgraded.fixture.whenStable();
    downgraded.fixture.detectChanges();
    expect(downgradeRead).toHaveBeenCalledTimes(3);
    expect(downgraded.el.querySelector('[data-testid="provider-notification-commercial"]')).toBeNull();
    expect(downgraded.el.querySelector('[data-job-id="comm-p1-1"]')).toBeNull();
    expect(downgraded.el.querySelector('[data-job-id="redacted-p1-1"]')).not.toBeNull();
    expect(downgraded.el.querySelector('[data-testid="provider-notifications-redacted"]')).not.toBeNull();
  });

  it('clears stale rows immediately on capability removal and drops late responses from earlier accounts', async () => {
    const delayedA = new Subject<ProviderNotificationsPage>();
    const read = vi.fn((id: string) =>
      id === accountA ? delayedA.asObservable() : of(makePage(accountB, true)),
    );
    const { fixture, el, capabilities } = setup(read, ['billing'], accountA);

    fixture.componentRef.setInput('account', accountB);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelectorAll('[data-testid="provider-notification-row"]').length).toBe(2);

    delayedA.next(makePage(accountA, true));
    delayedA.complete();
    await Promise.resolve();
    fixture.detectChanges();
    expect(el.querySelector('[data-job-id="job-' + accountA + '-1"]')).toBeNull();

    capabilities.set(['security-administrator']);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="provider-notifications-panel"]')).toBeNull();
  });

  it('switches localized labels across locales and displays error banner on 403', async () => {
    const { fixture, el } = setup(() => of(makePage(accountA, true)), ['licensing-manager']);
    await fixture.whenStable();
    fixture.detectChanges();

    const select = el.querySelector<HTMLSelectElement>('[data-testid="provider-notifications-locale"]')!;
    select.value = 'hi-IN';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(el.querySelector('#provider-notifications-heading')?.textContent).toContain('निर्धारित लाइसेंस सूचनाएं');

    TestBed.resetTestingModule();
    const denied = setup(
      () => throwError(() => new HttpErrorResponse({ status: 403 })),
      ['support'],
    );
    await denied.fixture.whenStable();
    denied.fixture.detectChanges();
    expect(denied.el.querySelector('[data-testid="provider-notifications-denied"]')).not.toBeNull();
  });

  it('ships complete translated assets for all nine Indian locales without untranslated English copies', () => {
    const dir = join(process.cwd(), 'src/assets/i18n');
    const flatten = (obj: Record<string, unknown>, prefix = ''): Record<string, string> => {
      const out: Record<string, string> = {};
      for (const [k, v] of Object.entries(obj)) {
        const key = prefix ? `${prefix}.${k}` : k;
        if (typeof v === 'string') {
          out[key] = v;
        } else if (v && typeof v === 'object') {
          Object.assign(out, flatten(v as Record<string, unknown>, key));
        }
      }
      return out;
    };

    const enFlat = flatten(JSON.parse(readFileSync(join(dir, 'en-IN.json'), 'utf-8')) as Record<string, unknown>);
    const enKeys = Object.keys(enFlat).sort();

    for (const code of ADMIN_SUPPORTED_LANGUAGE_CODES) {
      const parsed = JSON.parse(readFileSync(join(dir, `${code}.json`), 'utf-8')) as Record<string, unknown>;
      const flat = flatten(parsed);
      expect(Object.keys(flat).sort()).toEqual(enKeys);
      if (code !== 'en-IN') {
        const copied = enKeys.filter((key) => flat[key] === enFlat[key]);
        expect(copied, `${code} copied English values`).toEqual([]);
      }
    }
  });
});
