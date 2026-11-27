import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { BehaviorSubject } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import en from '../../../../../public/assets/i18n/en-IN.json';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { stubMemoryStorage } from '../billing.test-utils';
import { CoverageExceptionPageComponent } from './coverage-exception-page.component';

const accountId = '33333333-3333-4333-8333-333333333333';
const termId = '44444444-4444-4444-8444-444444444444';
const northId = '11111111-1111-4111-8111-111111111111';
const southId = '22222222-2222-4222-8222-222222222222';
const exceptionId = '55555555-5555-4555-8555-555555555555';

function mount(params: Record<string, string>) {
  const scope = signal({ userId: 'u1', shopId: 's1', epoch: 1 });
  const paramMap = new BehaviorSubject(convertToParamMap(params));
  TestBed.configureTestingModule({
    imports: [
      CoverageExceptionPageComponent,
      TranslocoTestingModule.forRoot({ langs: { 'en-IN': en }, translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' } }),
    ],
    providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ShopContextService, useValue: { scope } },
      { provide: LocalizationService, useValue: { currentLanguage: () => 'en-IN' } },
      { provide: ActivatedRoute, useValue: { paramMap: paramMap.asObservable(), snapshot: { paramMap: paramMap.value } } },
    ],
  });
  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(CoverageExceptionPageComponent);
  fixture.detectChanges();
  return { fixture, http };
}

async function flush(fixture: { detectChanges(): void }) {
  for (let i = 0; i < 3; i++) await Promise.resolve();
  fixture.detectChanges();
}

describe('CoverageExceptionPageComponent', () => {
  beforeEach(() => {
    stubMemoryStorage();
    vi.stubGlobal('navigator', { locks: { request: (_: string, run: () => unknown) => Promise.resolve(run()) } });
  });
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.unstubAllGlobals();
  });

  it('requires all 4 acknowledgements, freezes the original request across timeout/reload, and retries the same key', async () => {
    const { fixture, http } = mount({ accountId, termId });
    const preview = {
      termId, capacity: 3, memberAllowance: 5, normalRenewalAt: '2028-02-29T04:30:00Z',
      observedAt: '2028-02-14T10:00:00Z', canRequest: true,
      disconnectedQueuesNotice: 'Disconnected device queues are unknown.',
      coveredShops: [{ shopId: northId, shopName: 'North', observedPermitDeadline: '2028-02-14T14:00:00Z',
        lastContactAt: '2028-02-14T10:00:00Z', lastSynchronizedAt: null, outstandingPermitCount: 1 }],
      eligibleShops: [{ shopId: southId, shopName: 'South' }],
      exceptions: [], hasMore: false,
    };
    http.expectOne(r => r.method === 'GET').flush(preview);
    await flush(fixture);

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="early-unknown-queues"]')?.textContent).toContain('unknown');

    const outSel = el.querySelector<HTMLSelectElement>('[data-testid="early-outgoing"]')!;
    outSel.value = northId; outSel.dispatchEvent(new Event('change'));
    const inSel = el.querySelector<HTMLSelectElement>('[data-testid="early-incoming"]')!;
    inSel.value = southId; inSel.dispatchEvent(new Event('change'));
    const reason = el.querySelector<HTMLTextAreaElement>('[data-testid="early-reason"]')!;
    reason.value = 'Relocating shop mid-term'; reason.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(el.querySelector<HTMLButtonElement>('[data-testid="early-submit"]')!.disabled).toBe(true);
    for (const box of el.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')) {
      box.checked = true; box.dispatchEvent(new Event('change'));
    }
    fixture.detectChanges();
    expect(el.querySelector<HTMLButtonElement>('[data-testid="early-submit"]')!.disabled).toBe(false);

    el.querySelector<HTMLButtonElement>('[data-testid="early-submit"]')!.click();
    await flush(fixture);
    const req = http.expectOne(r => r.method === 'POST');
    const originalBody = req.request.body;
    expect(originalBody.overlapDisposition).toBe('CourtesyBridge');
    req.flush(null, { status: 503, statusText: 'Unavailable' });
    await flush(fixture);
    fixture.destroy();

    const restored = TestBed.createComponent(CoverageExceptionPageComponent);
    restored.detectChanges();
    http.expectOne(r => r.method === 'GET').flush(preview);
    await flush(restored);
    restored.nativeElement.querySelector('[data-testid="early-retry"]').click();
    await flush(restored);
    const retryReq = http.expectOne(r => r.method === 'POST');
    expect(retryReq.request.body).toEqual(originalBody);
    retryReq.flush({
      exceptionId, accountId, termId, proposalRevision: 1, termsDigest: 'a'.repeat(64),
      outgoingShopId: northId, outgoingShopName: 'North', incomingShopId: southId, incomingShopName: 'South',
      capacity: 3, memberAllowance: 5, normalRenewalAt: '2028-02-29T04:30:00Z',
      requestedEarlyStartAt: '2028-02-14T10:00:00Z', paidCutoverAt: '2028-02-14T14:00:00Z',
      overlapDisposition: 'CourtesyBridge', observedPermitDeadline: '2028-02-14T14:00:00Z',
      disconnectedQueuesUnknown: true, requestReason: 'Relocating shop mid-term', state: 'AwaitingConsents',
      reason: null, outgoingConsentState: 'AwaitingConsent', incomingConsentState: 'AwaitingConsent',
      courtesyGrantId: null, courtesyStartsAt: null, courtesyEndsAt: null,
      outgoingActivation: 'Pending', incomingActivation: 'Pending', courtesyActivation: 'Pending',
    });
    await flush(restored);
    http.expectOne(r => r.method === 'GET').flush(preview);
    await flush(restored);
    http.verify();
  });

  it('allows an owner to grant and accept consent for their own leg', async () => {
    const { fixture, http } = mount({ accountId, shopId: northId, exceptionId });
    const view = {
      exceptionId, accountId, termId, proposalRevision: 1, termsDigest: 'a'.repeat(64),
      leg: 'Outgoing', shopId: northId, shopName: 'North', counterpartyShopName: 'South',
      normalRenewalAt: '2028-02-29T04:30:00Z', requestedEarlyStartAt: '2028-02-14T10:00:00Z',
      paidCutoverAt: '2028-02-14T14:00:00Z', overlapDisposition: 'CourtesyBridge', memberAllowance: 5,
      observedPermitDeadline: '2028-02-14T14:00:00Z', disconnectedQueuesUnknown: true,
      state: 'AwaitingConsents', consentStatus: 'AwaitingConsent', legActivation: 'Pending',
      expiresAt: '2028-02-21T10:00:00Z',
    };
    http.expectOne(r => r.method === 'GET').flush(view);
    await flush(fixture);

    fixture.nativeElement.querySelector('[data-testid="early-grant"]').click();
    await flush(fixture);
    http.expectOne(r => r.method === 'POST' && r.url.endsWith('/consent/grant')).flush({ ...view, consentStatus: 'Granted' });
    await flush(fixture);
    http.expectOne(r => r.method === 'GET').flush({ ...view, consentStatus: 'Granted' });
    await flush(fixture);

    fixture.nativeElement.querySelector('[data-testid="early-accept"]').click();
    await flush(fixture);
    http.expectOne(r => r.method === 'POST' && r.url.endsWith('/consent/accept')).flush({ ...view, consentStatus: 'Accepted' });
    await flush(fixture);
    http.expectOne(r => r.method === 'GET').flush({ ...view, consentStatus: 'Accepted' });
    await flush(fixture);
    expect(fixture.nativeElement.querySelector('[data-testid="early-owner-status"]').textContent).toContain('Accepted');
    http.verify();
  });
});
