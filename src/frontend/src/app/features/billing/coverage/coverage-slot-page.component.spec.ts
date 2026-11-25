import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
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
import { CoverageSlotPageComponent } from './coverage-slot-page.component';
import type { Assignment, OwnerConsent, SlotTerm } from './coverage-slot.service';

const accountId = '33333333-3333-4333-8333-333333333333';
const termId = '44444444-4444-4444-8444-444444444444';
const northId = '11111111-1111-4111-8111-111111111111';
const southId = '22222222-2222-4222-8222-222222222222';
const assignmentId = '55555555-5555-4555-8555-555555555555';

function makeTerm(overrides: Partial<SlotTerm> = {}): SlotTerm {
  return {
    termId, capacity: 3, unusedSlots: 1, paidThrough: '2028-02-29T04:30:00Z',
    eligibleShops: [{ shopId: northId, shopName: 'North' }, { shopId: southId, shopName: 'South' }],
    assignments: [], hasMore: false, ...overrides,
  };
}

function makeAssignment(overrides: Partial<Assignment> = {}): Assignment {
  return {
    assignmentId, accountId, termId, shopId: northId, shopName: 'North',
    state: 'Applied', reason: null, activation: 'Acknowledged', merchantReceipt: 'Applied',
    coverageEndsAt: '2028-02-29T04:30:00Z', ...overrides,
  };
}

function mount(params: Record<string, string>) {
  const scope = signal({ userId: 'u1', shopId: 's1', epoch: 1 });
  const paramMap = new BehaviorSubject(convertToParamMap(params));
  TestBed.configureTestingModule({
    imports: [
      CoverageSlotPageComponent,
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
  const fixture = TestBed.createComponent(CoverageSlotPageComponent);
  fixture.detectChanges();
  return { fixture, http };
}

async function flush(fixture: { detectChanges(): void }) {
  await Promise.resolve();
  await Promise.resolve();
  fixture.detectChanges();
}

describe('CoverageSlotPageComponent cross-tab recovery, deadline reset and truthful status', () => {
  beforeEach(() => {
    stubMemoryStorage();
    vi.stubGlobal('navigator', { locks: { request: (_: string, run: () => unknown) => Promise.resolve(run()) } });
  });
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.unstubAllGlobals();
  });

  it('refuses a second tab confirming South while North is unresolved and recovers North explicitly with its exact key', async () => {
    const tab1 = mount({ accountId, termId });
    tab1.http.expectOne(r => r.method === 'GET' && r.url.endsWith('/slot-assignments')).flush(makeTerm());
    await flush(tab1.fixture);

    const el1: HTMLElement = tab1.fixture.nativeElement;
    const select1 = el1.querySelector<HTMLSelectElement>('[data-testid="slot-shop"]')!;
    select1.value = northId; select1.dispatchEvent(new Event('change'));
    tab1.fixture.detectChanges();
    for (const box of el1.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')) {
      box.checked = true; box.dispatchEvent(new Event('change'));
    }
    tab1.fixture.detectChanges();
    el1.querySelector<HTMLButtonElement>('[data-testid="slot-confirm"]')!.click();
    await flush(tab1.fixture);

    const pendingPost = tab1.http.expectOne(r => r.method === 'POST' && r.url.endsWith('/slot-assignments'));
    const originalBody = pendingPost.request.body;
    expect(originalBody.shopId).toBe(northId);
    pendingPost.flush({ code: 'Billing.Unavailable' }, { status: 503, statusText: 'Unavailable' });
    await flush(tab1.fixture);

    // Tab 2 loaded before Tab 1 saved the pending draft; now Tab 2 selects South and clicks Confirm.
    const savedKey = localStorage.key(0)!;
    const savedRaw = localStorage.getItem(savedKey)!;
    TestBed.resetTestingModule();
    localStorage.removeItem(savedKey);
    const tab2 = mount({ accountId, termId });
    tab2.http.expectOne(r => r.method === 'GET' && r.url.endsWith('/slot-assignments')).flush(makeTerm());
    await flush(tab2.fixture);
    localStorage.setItem(savedKey, savedRaw);

    const el2: HTMLElement = tab2.fixture.nativeElement;
    const select2 = el2.querySelector<HTMLSelectElement>('[data-testid="slot-shop"]')!;
    select2.value = southId; select2.dispatchEvent(new Event('change'));
    tab2.fixture.detectChanges();
    for (const box of el2.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')) {
      box.checked = true; box.dispatchEvent(new Event('change'));
    }
    tab2.fixture.detectChanges();
    el2.querySelector<HTMLButtonElement>('[data-testid="slot-confirm"]')!.click();
    await flush(tab2.fixture);

    // Conflicting click MUST send zero POST requests and surface the named pending intent for North.
    tab2.http.expectNone(r => r.method === 'POST');
    const pendingNotice = el2.querySelector('[data-testid="slot-pending-intent"]');
    expect(pendingNotice?.textContent).toContain('North');

    // Explicit recovery sends the exact original operationKey and North payload.
    el2.querySelector<HTMLButtonElement>('[data-testid="slot-retry-pending"]')!.click();
    await flush(tab2.fixture);
    const retryPost = tab2.http.expectOne(r => r.method === 'POST' && r.url.endsWith('/slot-assignments'));
    expect(retryPost.request.body).toEqual(originalBody);
    retryPost.flush(makeAssignment({ state: 'AwaitingConsent', activation: 'NotApplicable', merchantReceipt: 'None' }));
    await flush(tab2.fixture);
    tab2.http.expectOne(r => r.method === 'GET' && r.url.endsWith('/slot-assignments')).flush(makeTerm());
    await flush(tab2.fixture);
    expect(localStorage.length).toBe(0);
  });

  it('refuses a second owner tab clicking Revoke while Accept is unresolved and recovers Accept explicitly', async () => {
    const granted: OwnerConsent = {
      assignment: makeAssignment({ state: 'AwaitingConsent', activation: 'NotApplicable', merchantReceipt: 'None' }),
      status: 'Granted',
    };
    const tab1 = mount({ accountId, shopId: northId, assignmentId });
    tab1.http.expectOne(r => r.method === 'GET' && r.url.endsWith('/consent')).flush(granted);
    await flush(tab1.fixture);

    tab1.fixture.nativeElement.querySelector('[data-testid="slot-accept"]').click();
    await flush(tab1.fixture);
    const acceptPost = tab1.http.expectOne(r => r.method === 'POST' && r.url.endsWith('/consent/accept'));
    const acceptKey = acceptPost.request.body.operationKey;
    acceptPost.flush({}, { status: 503, statusText: 'Unavailable' });
    await flush(tab1.fixture);

    const savedKey = localStorage.key(0)!;
    const savedRaw = localStorage.getItem(savedKey)!;
    TestBed.resetTestingModule();
    localStorage.removeItem(savedKey);
    const tab2 = mount({ accountId, shopId: northId, assignmentId });
    tab2.http.expectOne(r => r.method === 'GET' && r.url.endsWith('/consent')).flush(granted);
    await flush(tab2.fixture);
    localStorage.setItem(savedKey, savedRaw);

    const el2: HTMLElement = tab2.fixture.nativeElement;
    el2.querySelector<HTMLButtonElement>('[data-testid="slot-revoke"]')!.click();
    await flush(tab2.fixture);

    tab2.http.expectNone(r => r.method === 'POST');
    const notice = el2.querySelector('[data-testid="slot-pending-intent"]');
    expect(notice?.textContent).toContain('North');
    expect(notice?.textContent).toContain('Confirm coverage');

    el2.querySelector<HTMLButtonElement>('[data-testid="slot-retry-pending"]')!.click();
    await flush(tab2.fixture);
    const retryAccept = tab2.http.expectOne(r => r.method === 'POST' && r.url.endsWith('/consent/accept'));
    expect(retryAccept.request.body).toEqual({ operationKey: acceptKey });
    retryAccept.flush({ ...granted, status: 'Accepted' });
    await flush(tab2.fixture);
    tab2.http.expectOne(r => r.method === 'GET' && r.url.endsWith('/consent')).flush({ ...granted, status: 'Accepted' });
    await flush(tab2.fixture);
  });

  it('sends no POST when recovering a pending intent that another tab already settled', async () => {
    const tab1 = mount({ accountId, termId });
    tab1.http.expectOne(r => r.method === 'GET' && r.url.endsWith('/slot-assignments')).flush(makeTerm());
    await flush(tab1.fixture);

    const el1: HTMLElement = tab1.fixture.nativeElement;
    const select1 = el1.querySelector<HTMLSelectElement>('[data-testid="slot-shop"]')!;
    select1.value = northId; select1.dispatchEvent(new Event('change'));
    tab1.fixture.detectChanges();
    for (const box of el1.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')) {
      box.checked = true; box.dispatchEvent(new Event('change'));
    }
    tab1.fixture.detectChanges();
    el1.querySelector<HTMLButtonElement>('[data-testid="slot-confirm"]')!.click();
    await flush(tab1.fixture);
    tab1.http.expectOne(r => r.method === 'POST').flush({}, { status: 503, statusText: 'Unavailable' });
    await flush(tab1.fixture);

    // Another tab clears the saved draft before this tab clicks Retry.
    localStorage.clear();
    el1.querySelector<HTMLButtonElement>('[data-testid="slot-retry-pending"]')!.click();
    await flush(tab1.fixture);
    tab1.http.expectNone(r => r.method === 'POST');
  });

  it('resets reviewed acknowledgements when a refresh changes the effective paid-through deadline', async () => {
    const { fixture, http } = mount({ accountId, termId });
    http.expectOne(r => r.method === 'GET').flush(makeTerm({ paidThrough: '2028-02-29T04:30:00Z' }));
    await flush(fixture);

    const el: HTMLElement = fixture.nativeElement;
    const select = el.querySelector<HTMLSelectElement>('[data-testid="slot-shop"]')!;
    select.value = northId; select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    const boxes = [...el.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')];
    for (const box of boxes) { box.checked = true; box.dispatchEvent(new Event('change')); }
    fixture.detectChanges();
    expect(el.querySelector<HTMLButtonElement>('[data-testid="slot-confirm"]')!.disabled).toBe(false);

    el.querySelector<HTMLButtonElement>('[data-testid="slot-refresh"]')!.click();
    http.expectOne(r => r.method === 'GET').flush(makeTerm({ paidThrough: '2028-02-20T04:30:00Z' }));
    await flush(fixture);

    for (const box of el.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')) {
      expect(box.checked).toBe(false);
    }
    expect(el.querySelector<HTMLButtonElement>('[data-testid="slot-confirm"]')!.disabled).toBe(true);
  });

  it.each([
    ['Applying', 'Activating coverage'],
    ['Acknowledged', 'Active, confirmed by the shop'],
    ['Grace', 'In grace period'],
    ['Suspended', 'Suspended'],
    ['Closed', 'Closed'],
    ['TermEnded', 'Expired'],
    ['NeedsReview', 'Needs review'],
  ] as const)('displays truthful billing status for applied activation %s', async (activation, label) => {
    const { fixture, http } = mount({ accountId, termId });
    http.expectOne(r => r.method === 'GET').flush(makeTerm({ assignments: [makeAssignment({ activation })] }));
    await flush(fixture);
    expect(fixture.nativeElement.querySelector('[data-testid="slot-status"]')?.textContent?.trim()).toBe(label);
  });

  it.each([
    ['Applying', 'Activating coverage'],
    ['AppliedLocally', 'Applied on this shop, awaiting billing confirmation'],
    ['Acknowledged', 'Active, confirmed by the shop'],
    ['Grace', 'In grace period'],
    ['Suspended', 'Suspended'],
    ['Closed', 'Closed'],
    ['Expired', 'Expired'],
    ['NeedsReview', 'Needs review'],
    ['Stale', 'Consent expired with the previous owner authority'],
  ] as const)('displays truthful owner status for %s', async (status, label) => {
    const { fixture, http } = mount({ accountId, shopId: northId, assignmentId });
    http.expectOne(r => r.method === 'GET').flush({ assignment: makeAssignment(), status });
    await flush(fixture);
    expect(fixture.nativeElement.querySelector('[role="status"]')?.textContent?.trim()).toBe(label);
  });
});
