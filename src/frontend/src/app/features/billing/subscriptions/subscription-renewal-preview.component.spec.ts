import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it } from 'vitest';
import en from '../../../../../public/assets/i18n/en-IN.json';
import { LocalizationService } from '../../../core/i18n/localization.service';
import type { Quote } from '../checkout/checkout.models';
import { SubscriptionRenewalPreviewComponent } from './subscription-renewal-preview.component';

const renewal: Quote = {
  accountId: 'account', orderId: 'order', revision: 1, schemaVersion: 1, planVersionId: 'plan', planName: 'Single shop',
  planVersion: 1, period: 'Month', termId: 'term', amountPaise: 12000, currency: 'INR', priceBasis: 'TaxInclusiveFinal',
  capacity: 1, membersPerShop: 5, shopCount: 1, shopHash: 'a'.repeat(64), snapshotHash: 'b'.repeat(64),
  customerTermsVersion: 'terms/1', refundPolicyVersion: 'refund/1', billingTimeZone: 'Asia/Kolkata',
  previewStartsAt: '2027-04-30T04:30:00Z', previewEndsAt: '2027-05-31T04:30:00Z', expiresAt: '2027-05-02T07:45:00Z',
  startRule: 'PriorPaidThrough', graceDaysIncluded: 3, developmentFixture: true, acceptance: null,
};

function render(quote: Quote) {
  TestBed.configureTestingModule({
    imports: [TranslocoTestingModule.forRoot({
      langs: { 'en-IN': en }, translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' }, preloadLangs: true,
    })],
    providers: [{ provide: LocalizationService, useValue: { currentLanguage: signal('en-IN') } }],
  });
  const fixture = TestBed.createComponent(SubscriptionRenewalPreviewComponent);
  fixture.componentRef.setInput('quote', quote);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

const text = (host: HTMLElement, id: string) => host.querySelector(`[data-testid="${id}"]`)?.textContent?.trim() ?? null;

describe('SubscriptionRenewalPreviewComponent', () => {
  it('shows the previous paid-through, the anchored new end and the grace days already included, in IST', () => {
    const host = render(renewal);
    expect(text(host, 'renewal-previous-end')).toBe('30 Apr 2027, 10:00:00 IST');
    expect(text(host, 'renewal-new-end')).toBe('31 May 2027, 10:00:00 IST');
    expect(text(host, 'renewal-grace')).toBe(
      'This renewal includes 3 grace days already elapsed since the previous paid-through. ' +
      'The new period still starts at the previous paid-through, so grace is not added again.');
  });

  it('states that an early renewal loses no paid time', () => {
    const host = render({ ...renewal, graceDaysIncluded: 0 });
    expect(text(host, 'renewal-grace')).toBe('The new period starts at the previous paid-through, so no paid time is lost.');
  });

  it('renders nothing for a first purchase or a restored-access start', () => {
    expect(render({ ...renewal, termId: null, startRule: 'Fulfilment' }).querySelector('[data-testid="renewal-preview"]')).toBeNull();
    TestBed.resetTestingModule();
    expect(render({ ...renewal, startRule: 'RestoredAccess' }).querySelector('[data-testid="renewal-preview"]')).toBeNull();
  });
});
