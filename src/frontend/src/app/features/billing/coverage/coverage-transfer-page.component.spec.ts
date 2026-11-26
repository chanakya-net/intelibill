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
import { CoverageTransferPageComponent } from './coverage-transfer-page.component';

const sourceId = '33333333-3333-4333-8333-333333333333';
const destinationId = '66666666-6666-4666-8666-666666666666';
const termId = '44444444-4444-4444-8444-444444444444';
const northId = '11111111-1111-4111-8111-111111111111';
const transferId = '55555555-5555-4555-8555-555555555555';
const digest = 'a'.repeat(64);

const terms = {
  transferId, proposalRevision: 1, termsDigest: digest, sourceAccountId: sourceId, sourceAccountName: 'Source Traders',
  destinationAccountId: destinationId, destinationAccountName: null, shopId: northId, shopName: 'North',
  treatment: 'RetainUntilPaidThrough', cutoverAt: null, sourcePaidThroughAt: '2028-02-29T04:30:00Z',
  observedPermitCount: 1, observedPermitDeadline: '2028-02-16T10:00:00Z', disconnectedQueuesUnknown: true,
  expiresAt: '2028-02-21T10:00:00Z', state: 'AwaitingConsents',
};

function item(accepted: string[] = [], viewerPurposes = ['SourceBillingOwner']) {
  return {
    ...terms, createdAt: '2028-02-14T10:00:00Z', stateReason: null,
    consents: ['SourceBillingOwner', 'DestinationBillingOwner', 'ShopOwner'].map(purpose => ({
      purpose, status: accepted.includes(purpose) ? 'Accepted' : 'AwaitingConsent', generation: 0, recordedAt: null,
    })),
    consentsCollected: false, awaitingProviderReview: false, viewerPurposes, viewerAuthorityRevision: 4,
  };
}

const preview = {
  sourceAccountId: sourceId, destinationAccountId: destinationId, termId, shopId: northId, shopName: 'North',
  sourceAffiliationId: '77777777-7777-4777-8777-777777777777', sourcePaidThroughAt: '2028-02-29T04:30:00Z',
  sourceOwnerRevision: 4, evidenceDigest: 'b'.repeat(64), treatment: 'RetainUntilPaidThrough', cutoverAt: null,
  observedPermitCount: 1, observedPermitDeadline: '2028-02-16T10:00:00Z', disconnectedQueuesUnknown: true,
  disconnectedQueuesNotice: 'Disconnected device queues are unknown.', proposedExpiresAt: '2028-02-21T10:00:00Z',
};

function mount(params: Record<string, string>) {
  const scope = signal({ userId: 'u1', shopId: 's1', epoch: 1 });
  const paramMap = new BehaviorSubject(convertToParamMap(params));
  TestBed.configureTestingModule({
    imports: [
      CoverageTransferPageComponent,
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
  const fixture = TestBed.createComponent(CoverageTransferPageComponent);
  fixture.detectChanges();
  return { fixture, http };
}

async function flush(fixture: { detectChanges(): void }) {
  for (let i = 0; i < 4; i++) await Promise.resolve();
  fixture.detectChanges();
}

function find<T extends HTMLElement>(fixture: { nativeElement: HTMLElement }, id: string) {
  return fixture.nativeElement.querySelector<T>(`[data-testid="${id}"]`);
}

/** Answers the request page's two initial reads: the account's transfers and the term's covered shops. */
function loadRequestPage(http: HttpTestingController, items: unknown[]) {
  http.expectOne(r => r.method === 'GET' && r.url.endsWith(`/accounts/${sourceId}/coverage-transfers`))
    .flush({ accountId: sourceId, items });
  http.expectOne(r => r.method === 'GET' && r.url.endsWith('/coverage-replacement-exceptions'))
    .flush({ coveredShops: [{ shopId: northId, shopName: 'North' }] });
}

async function fillAndReview(fixture: ReturnType<typeof mount>['fixture'], http: HttpTestingController) {
  const shop = find<HTMLSelectElement>(fixture, 'transfer-shop')!;
  shop.value = northId; shop.dispatchEvent(new Event('change'));
  const dest = find<HTMLInputElement>(fixture, 'transfer-destination')!;
  dest.value = destinationId; dest.dispatchEvent(new Event('input'));
  fixture.detectChanges();
  find<HTMLButtonElement>(fixture, 'transfer-preview')!.click();
  const req = http.expectOne(r => r.method === 'POST' && r.url.endsWith('/coverage-transfers/preview'));
  expect(req.request.body).toEqual({
    destinationAccountId: destinationId, shopId: northId, treatment: 'RetainUntilPaidThrough', cutoverAt: null,
  });
  req.flush(preview);
  await flush(fixture);
}

describe('CoverageTransferPageComponent', () => {
  beforeEach(() => {
    stubMemoryStorage();
    vi.stubGlobal('navigator', { locks: { request: (_: string, run: () => unknown) => Promise.resolve(run()) } });
  });
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.unstubAllGlobals();
  });

  it('shows the named impact before a request and needs every acknowledgement', async () => {
    const { fixture, http } = mount({ accountId: sourceId, termId });
    loadRequestPage(http, []);
    await flush(fixture);
    expect(find(fixture, 'transfer-no-change')?.textContent).toContain('changes nothing');
    expect(find(fixture, 'transfer-submit')).toBeNull();

    await fillAndReview(fixture, http);
    expect(find(fixture, 'transfer-review-shop')?.textContent).toContain('North');
    expect(find(fixture, 'transfer-review-shop')?.textContent).toContain(destinationId);
    expect(find(fixture, 'transfer-review-paid-through')?.getAttribute('datetime')).toBe('2028-02-29T04:30:00Z');
    expect(find(fixture, 'transfer-unknown-queues')?.textContent).toContain('unknown');

    const reason = find<HTMLTextAreaElement>(fixture, 'transfer-reason')!;
    reason.value = 'Branch sold'; reason.dispatchEvent(new Event('input'));
    const boxes = [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLInputElement>('input[type="checkbox"]')];
    expect(boxes).toHaveLength(5);
    for (const box of boxes.slice(0, 4)) { box.checked = true; box.dispatchEvent(new Event('change')); }
    fixture.detectChanges();
    expect(find<HTMLButtonElement>(fixture, 'transfer-submit')!.disabled).toBe(true);
    boxes[4].checked = true; boxes[4].dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(find<HTMLButtonElement>(fixture, 'transfer-submit')!.disabled).toBe(false);

    // Changing the destination discards the reviewed impact and its acknowledgements.
    const dest = find<HTMLInputElement>(fixture, 'transfer-destination')!;
    dest.value = sourceId; dest.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(find(fixture, 'transfer-review')).toBeNull();
    http.verify();
  });

  it('freezes the request before sending, keeps it through an unknown outcome and retries the same key', async () => {
    const { fixture, http } = mount({ accountId: sourceId, termId });
    loadRequestPage(http, []);
    await flush(fixture);
    await fillAndReview(fixture, http);
    const reason = find<HTMLTextAreaElement>(fixture, 'transfer-reason')!;
    reason.value = 'Branch sold'; reason.dispatchEvent(new Event('input'));
    for (const box of (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLInputElement>('input[type="checkbox"]')) {
      box.checked = true; box.dispatchEvent(new Event('change'));
    }
    fixture.detectChanges();
    find<HTMLButtonElement>(fixture, 'transfer-submit')!.click();
    await flush(fixture);
    const sent = http.expectOne(r => r.method === 'POST' && r.url.endsWith(`/subscriptions/${termId}/coverage-transfers`));
    const original = sent.request.body;
    expect(original).toMatchObject({
      destinationAccountId: destinationId, shopId: northId, expectedSourceOwnerRevision: 4,
      expectedEvidenceDigest: 'b'.repeat(64), acknowledgedHistoricalBillsStay: true,
    });
    sent.flush(null, { status: 504, statusText: 'Timeout' });
    await flush(fixture);
    expect(find(fixture, 'transfer-pending')?.textContent).toContain('North');
    fixture.destroy();

    // After a reload the same owner sees the frozen decision and can only resend it unchanged.
    const restored = TestBed.createComponent(CoverageTransferPageComponent);
    restored.detectChanges();
    loadRequestPage(http, []);
    await flush(restored);
    expect(find(restored, 'transfer-request-form')).toBeNull();
    find<HTMLButtonElement>(restored, 'transfer-retry')!.click();
    await flush(restored);
    const retried = http.expectOne(r => r.method === 'POST');
    expect(retried.request.body).toEqual(original);
    retried.flush(item());
    await flush(restored);
    loadRequestPage(http, [item()]);
    await flush(restored);

    expect(find(restored, 'transfer-pending')).toBeNull();
    expect(find(restored, 'transfer-item-shop')?.textContent).toBe('North');
    expect(find(restored, 'transfer-state')?.getAttribute('data-state')).toBe('AwaitingConsents');
    expect(find(restored, 'transfer-progress')?.textContent).toContain('0 of 3');
    http.verify();
  });

  it('gives exactly one billing-owner purpose, bound to the displayed revision, and shows partial progress', async () => {
    const { fixture, http } = mount({ accountId: destinationId });
    const incoming = item(['SourceBillingOwner'], ['DestinationBillingOwner']);
    http.expectOne(r => r.method === 'GET').flush({ accountId: destinationId, items: [incoming] });
    await flush(fixture);
    expect(find(fixture, 'transfer-parties')?.textContent).toContain('Source Traders');
    expect(find(fixture, 'transfer-consent-SourceBillingOwner')?.getAttribute('data-status')).toBe('Accepted');
    expect(find(fixture, 'transfer-accept-SourceBillingOwner')).toBeNull();

    find<HTMLButtonElement>(fixture, 'transfer-accept-DestinationBillingOwner')!.click();
    await flush(fixture);
    const sent = http.expectOne(r => r.method === 'POST');
    expect(sent.request.url).toContain(`/coverage-transfers/${transferId}/consents/destination-billing-owner/accept`);
    expect(sent.request.body).toMatchObject({
      expectedProposalRevision: 1, expectedTermsDigest: digest, expectedAuthorityRevision: 4,
    });
    const both = { ...item(['SourceBillingOwner', 'DestinationBillingOwner'], ['DestinationBillingOwner']), destinationAccountName: 'Destination Company' };
    sent.flush(both);
    await flush(fixture);
    http.expectOne(r => r.method === 'GET').flush({ accountId: destinationId, items: [both] });
    await flush(fixture);

    expect(find(fixture, 'transfer-progress')?.textContent).toContain('2 of 3');
    expect(find(fixture, 'transfer-state')?.getAttribute('data-state')).toBe('AwaitingConsents');
    expect(find(fixture, 'transfer-parties')?.textContent).toContain('Destination Company');
    expect(find(fixture, 'transfer-revoke-DestinationBillingOwner')).not.toBeNull();
    http.verify();
  });

  it('lets the named shop owner accept, and releases the frozen decision only on a definite refusal', async () => {
    const { fixture, http } = mount({ accountId: sourceId, shopId: northId, transferId });
    const view = { ...terms, consentStatus: 'AwaitingConsent', delivery: 'None', authorityRevision: 7 };
    http.expectOne(r => r.method === 'GET' && r.url.includes(`/coverage-transfer-shops/${northId}/${transferId}/consent`)).flush(view);
    await flush(fixture);
    expect(find(fixture, 'transfer-owner-shop')?.textContent).toBe('North');
    expect(find(fixture, 'transfer-unknown-queues')).not.toBeNull();

    find<HTMLButtonElement>(fixture, 'transfer-accept-ShopOwner')!.click();
    await flush(fixture);
    const refused = http.expectOne(r => r.method === 'POST');
    expect(refused.request.url).toContain('/consent/accept');
    expect(refused.request.body.expectedAuthorityRevision).toBe(7);
    refused.flush({ code: 'Coverage.TransferChanged' }, { status: 409, statusText: 'Conflict' });
    await flush(fixture);
    http.expectOne(r => r.method === 'GET').flush(view);
    await flush(fixture);
    expect(find(fixture, 'transfer-pending')).toBeNull();

    find<HTMLButtonElement>(fixture, 'transfer-accept-ShopOwner')!.click();
    await flush(fixture);
    http.expectOne(r => r.method === 'POST').flush({ ...view, consentStatus: 'Accepted', delivery: 'Pending' });
    await flush(fixture);
    http.expectOne(r => r.method === 'GET').flush({ ...view, consentStatus: 'Accepted', delivery: 'Pending' });
    await flush(fixture);
    expect(find(fixture, 'transfer-owner-status')?.getAttribute('data-delivery')).toBe('Pending');
    expect(find(fixture, 'transfer-revoke-ShopOwner')).not.toBeNull();
    http.verify();
  });
});
