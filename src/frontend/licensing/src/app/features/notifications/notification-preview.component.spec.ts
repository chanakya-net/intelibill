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
  type ProviderNotificationsPage,
} from './notification-preview-api.service';
import { NotificationPreviewComponent } from './notification-preview.component';

const accountA = '33333333-3333-4333-8333-333333333333';
const accountB = '55555555-5555-4555-8555-555555555555';

function makePage(accountId: string, commercialIncluded: boolean): ProviderNotificationsPage {
  return {
    schemaVersion: 1,
    accountId,
    evaluatedAt: '2028-02-22T04:30:00Z',
    commercialIncluded,
    items: [
      {
        jobId: `job-${accountId}-1`,
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
        dueAt: '2028-02-22T04:30:00Z',
        effectiveDeadlineAt: '2028-02-29T04:30:00Z',
        timeZone: 'Asia/Kolkata',
        locale: 'en-IN',
        allowedChannels: ['InApp', 'Email'],
        templateKey: 'notice.paid.minus7',
        guidanceCode: 'PaidRenewalDue',
        coveredShopCount: 2,
        paymentDoesNotLiftSuspension: false,
        plannedAt: '2028-01-31T04:30:00Z',
        commercial: commercialIncluded
          ? {
              evidencedTermPricePaise: 12000,
              evidencedTermCurrency: 'INR',
              renewalQuoteRequired: true,
              destinationHint: 'o***@example.test',
              recipientReference: 'user:1',
              coveredShopNames: ['North Shop', 'South Shop'],
            }
          : null,
      },
      {
        jobId: `job-${accountId}-2`,
        scopeKind: 'Account',
        scopeId: accountId,
        audience: 'CommercialBillingRole',
        purpose: 'PaidRenewalReminder',
        sourceKind: 'Paid',
        sourceId: '44444444-4444-4444-8444-444444444444',
        sourceRevision: 'v1:abc',
        threshold: 'PaidMinus1Day',
        status: 'Scheduled',
        isDue: false,
        dueAt: '2028-02-28T04:30:00Z',
        effectiveDeadlineAt: '2028-02-29T04:30:00Z',
        timeZone: 'Asia/Kolkata',
        locale: 'en-IN',
        allowedChannels: ['InApp', 'Email'],
        templateKey: 'notice.paid.minus1',
        guidanceCode: 'PaidRenewalDue',
        coveredShopCount: 2,
        paymentDoesNotLiftSuspension: false,
        plannedAt: '2028-01-31T04:30:00Z',
        commercial: null,
      },
    ],
    nextCursor: null,
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
  it('shows commercial preview with UTC and IST due times and due/future badges for Billing', async () => {
    const read = vi.fn(() => of(makePage(accountA, true)));
    const { fixture, el } = setup(read, ['billing']);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(read).toHaveBeenCalledWith(accountA, null);
    const rows = el.querySelectorAll('[data-testid="provider-notification-row"]');
    expect(rows.length).toBe(2);
    expect(el.querySelector('[data-testid="provider-notification-due-utc"]')?.textContent).toContain('22 Feb 2028, 04:30:00 UTC');
    expect(el.querySelector('[data-testid="provider-notification-due-ist"]')?.textContent).toContain('22 Feb 2028, 10:00:00 IST');
    expect(el.querySelector('[data-testid="provider-notification-commercial"]')).not.toBeNull();
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

    // Late reply for account A must be dropped.
    delayedA.next(makePage(accountA, true));
    delayedA.complete();
    await Promise.resolve();
    fixture.detectChanges();
    expect(el.querySelector('[data-job-id="job-' + accountA + '-1"]')).toBeNull();

    // Removing capability clears the panel immediately.
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
