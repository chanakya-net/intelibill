import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import en from '../../../../../public/assets/i18n/en-IN.json';
import contract from '../../../../../../../../tests/contracts/licensing/capacity-reductions-v1.json';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { stubMemoryStorage } from '../billing.test-utils';
import { shopSetHash } from '../checkout/checkout.models';
import { CheckoutService } from '../checkout/checkout.service';
import type { PublishedPlan } from '../plans/billing-plans.service';
import { BillingAccountService } from '../services/billing-account.service';
import type { SubscriptionTermStatus } from '../status/subscription-status.models';
import { CapacityReductionDrafts } from './capacity-reduction-drafts';
import { CapacityReductionPreviewComponent } from './capacity-reduction-preview.component';
import {
  CapacityReductionService, isValidSchedule, matchesPreviewRequest, type CapacityReductionPreview,
  type CapacityReductionShop, type ScheduledCapacityReduction,
} from './capacity-reduction.service';

const account = '33333333-3333-4333-8333-333333333333';
const termId = '44444444-4444-4444-8444-444444444444';
const soloPlan = {
  versionId: '55555555-5555-4555-8555-555555555555', versionNumber: 1, name: 'Solo Shop', includedShops: 1,
  membersPerShop: 2, kind: 'single-shop', extraShopsOffered: false,
} as PublishedPlan;
const largerPlan = {
  versionId: '55555555-5555-4555-8555-555555555556', versionNumber: 2, name: 'Large Growth', includedShops: 4,
  membersPerShop: 6, kind: 'multi-shop', extraShopsOffered: true,
} as PublishedPlan;
const shopA = '11111111-1111-4111-8111-111111111111';
const shopB = '22222222-2222-4222-8222-222222222222';
const affA = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const affB = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const term = {
  termId, basis: 'Paid', purchasedCapacity: 3, memberAllowance: 5,
  paidThrough: '2028-02-29T04:30:00Z', startsAt: '2028-01-31T04:30:00Z',
} as SubscriptionTermStatus;

const previewShops: readonly CapacityReductionShop[] = [
  { shopId: shopA, name: 'Leap Central', affiliationRequestId: affA, activeMembers: 1, exceedsAllowance: false, excessMembers: 0 },
  { shopId: shopB, name: 'Leap East', affiliationRequestId: affB, activeMembers: 4, exceedsAllowance: true, excessMembers: 2 },
];

const frozenShops: readonly CapacityReductionShop[] = [
  { shopId: shopA, name: 'Leap Central', affiliationRequestId: affA, activeMembers: 1, exceedsAllowance: false, excessMembers: 0, retained: true },
  { shopId: shopB, name: 'Leap East', affiliationRequestId: affB, activeMembers: 4, exceedsAllowance: true, excessMembers: 2, retained: false },
];

function previewSample(overrides: Partial<CapacityReductionPreview> = {}): CapacityReductionPreview {
  return {
    schemaVersion: 1, accountId: account, previewId: '88888888-8888-4888-8888-888888888888', termId,
    sourcePlanVersionId: null, targetPlanId: '77777777-7777-4777-8777-777777777777',
    targetPlanVersionId: soloPlan.versionId, targetPlanVersion: 1, targetPlanName: 'Solo Shop', period: 'Month',
    priorCapacity: 3, targetCapacity: 1, priorMemberAllowance: 5, targetMemberAllowance: 2, targetMode: 'Independent',
    slotsRemoved: 2, unusedSlots: 1, minimumShopsToRemove: 1, sourceShopCount: 2,
    sourceShopHash: 'a'.repeat(64), sourceHash: 'b'.repeat(64), observationDigest: 'c'.repeat(64),
    hasMemberConflicts: true, conflictedShopCount: 1, amountPaise: 6000, currency: 'INR',
    priceBasis: 'TaxInclusiveFinal', customerTermsVersion: 'terms-1', refundPolicyVersion: 'refund-1',
    billingTimeZone: 'Asia/Kolkata', calendarVersion: 'calendar-month-year/1', renewalPolicyVersion: 'renewal/1',
    anchorLocal: '2028-01-31T10:00:00', ordinal: 1, currentStartsAt: term.startsAt, currentPaidThrough: term.paidThrough,
    renewalStartsAt: term.paidThrough, renewalEndsAt: '2028-03-31T04:30:00Z', suspensionDisclosed: false,
    createdAt: '2028-02-14T04:30:00Z', expiresAt: '2999-01-01T00:00:00Z', developmentFixture: true,
    snapshotHash: 'd'.repeat(64), ...overrides,
  };
}

function scheduleSample(overrides: Partial<ScheduledCapacityReduction> = {}): ScheduledCapacityReduction {
  return {
    schemaVersion: 1, accountId: account, scheduleId: '99999999-9999-4999-8999-999999999999',
    previewId: '88888888-8888-4888-8888-888888888888', termId, state: 'Scheduled', reason: null,
    targetPlanId: '77777777-7777-4777-8777-777777777777', targetPlanVersionId: soloPlan.versionId,
    targetPlanVersion: 1, targetPlanName: 'Solo Shop', period: 'Month', priorCapacity: 3, targetCapacity: 1,
    priorMemberAllowance: 5, targetMemberAllowance: 2, targetMode: 'Independent', sourceShopCount: 2,
    sourceShopHash: 'a'.repeat(64), retainedShopCount: 1, retainedShopHash: 'e'.repeat(64),
    sourceHash: 'b'.repeat(64), observationDigest: 'c'.repeat(64), amountPaise: 6000, currency: 'INR',
    priceBasis: 'TaxInclusiveFinal', customerTermsVersion: 'terms-1', refundPolicyVersion: 'refund-1',
    billingTimeZone: 'Asia/Kolkata', calendarVersion: 'calendar-month-year/1', renewalPolicyVersion: 'renewal/1',
    anchorLocal: '2028-01-31T10:00:00', ordinal: 1, currentStartsAt: term.startsAt, currentPaidThrough: term.paidThrough,
    effectiveAt: term.paidThrough, renewalEndsAt: '2028-03-31T04:30:00Z', resolutionGeneration: 0,
    orderId: null, appliedTermId: null, suspensionDisclosed: false, developmentFixture: true,
    createdAt: '2028-02-14T04:30:00Z', fundedAt: null, appliedAt: null, snapshotHash: 'f'.repeat(64),
    conflicts: null, ...overrides,
  };
}

function render(role: 'Owner' | 'Manager', overrides: Partial<Record<keyof CapacityReductionService, unknown>> = {}) {
  const scope = signal({ userId: 'u1', shopId: 's1', epoch: 1 });
  const api = {
    readSchedule: vi.fn(async () => { throw new HttpErrorResponse({ status: 404 }); }),
    createPreview: vi.fn(async () => previewSample()),
    previewShops: vi.fn(async () => previewShops),
    schedule: vi.fn(async () => scheduleSample()),
    recoverSchedule: vi.fn(async () => { throw new HttpErrorResponse({ status: 404 }); }),
    scheduleShops: vi.fn(async () => frozenShops),
    createOrder: vi.fn(async () => ({ orderId: '12121212-1212-4121-8121-121212121212' })),
    resolve: vi.fn(async () => scheduleSample({ state: 'Applying', resolutionGeneration: 2 })),
    paymentStatus: vi.fn(async () => ({ payment: 'NotStarted', fulfilment: 'NotFulfilled' })),
    ...overrides,
  };
  TestBed.configureTestingModule({
    imports: [TranslocoTestingModule.forRoot({
      langs: { 'en-IN': en }, translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' }, preloadLangs: true,
    })],
    providers: [
      provideRouter([]),
      { provide: ShopContextService, useValue: { scope } },
      { provide: LocalizationService, useValue: { currentLanguage: signal('en-IN') } },
      { provide: BillingAccountService, useValue: { list: () => of([{ accountId: account, name: 'A', role, createdAt: '' }]) } },
      { provide: CheckoutService, useValue: { plans: async () => [largerPlan, soloPlan] } },
      { provide: CapacityReductionService, useValue: api },
    ],
  });
  const fixture = TestBed.createComponent(CapacityReductionPreviewComponent);
  fixture.componentRef.setInput('account', account);
  fixture.componentRef.setInput('term', term);
  fixture.detectChanges();
  return { fixture, page: fixture.nativeElement as HTMLElement, api, scope };
}

async function settle(fixture: { detectChanges(): void; whenStable(): Promise<unknown> }) {
  for (let i = 0; i < 6; i++) {
    await fixture.whenStable();
    await new Promise(resolve => setTimeout(resolve));
    fixture.detectChanges();
  }
}

let storage: Map<string, string>;
let originalLocks: PropertyDescriptor | undefined;
beforeEach(() => {
  storage = stubMemoryStorage();
  originalLocks = Object.getOwnPropertyDescriptor(navigator, 'locks');
  Object.defineProperty(navigator, 'locks', {
    configurable: true,
    value: { request: async (_: string, run: () => unknown) => run() },
  });
});
afterEach(() => {
  TestBed.resetTestingModule();
  vi.unstubAllGlobals();
  if (originalLocks) Object.defineProperty(navigator, 'locks', originalLocks);
  else Reflect.deleteProperty(navigator, 'locks');
});

describe('capacity reduction preview', () => {
  it('filters to smaller plans, never preselects shops, blocks conflicted shops, and schedules on explicit choice', async () => {
    let previewSavedBeforeSend = false;
    let scheduleSavedBeforeSend = false;
    const { fixture, page, api } = render('Owner', {
      createPreview: vi.fn(async () => {
        previewSavedBeforeSend = [...storage.values()].some(v => v.includes(soloPlan.versionId));
        return previewSample();
      }),
      schedule: vi.fn(async () => {
        scheduleSavedBeforeSend = [...storage.values()].some(v => v.includes(shopA));
        return scheduleSample();
      }),
    });
    await settle(fixture);
    (page.querySelector('[data-testid="reduction-open"]') as HTMLButtonElement).click();
    await settle(fixture);

    const select = page.querySelector('[data-testid="reduction-plan"]') as HTMLSelectElement;
    expect([...select.options].map(o => o.value)).toEqual([soloPlan.versionId]);
    (page.querySelector('[data-testid="reduction-preview-submit"]') as HTMLButtonElement).click();
    await settle(fixture);

    expect(previewSavedBeforeSend).toBe(true);
    expect(page.querySelector('[data-testid="reduction-conflict-summary"]')?.textContent).toContain('1 shop(s)');

    const boxes = [...page.querySelectorAll<HTMLInputElement>('[data-testid="reduction-shop-checkbox"]')];
    expect(boxes.length).toBe(2);
    expect(boxes.every(b => !b.checked)).toBe(true);
    expect(boxes[0].disabled).toBe(false);
    expect(boxes[1].disabled).toBe(true);
    expect((page.querySelector('[data-testid="reduction-schedule-submit"]') as HTMLButtonElement).disabled).toBe(true);

    boxes[0].click();
    (page.querySelector('[data-testid="reduction-confirm-check"]') as HTMLInputElement).click();
    await settle(fixture);

    const submit = page.querySelector('[data-testid="reduction-schedule-submit"]') as HTMLButtonElement;
    expect(submit.disabled).toBe(false);
    submit.click();
    await settle(fixture);

    expect(scheduleSavedBeforeSend).toBe(true);
    expect(api.schedule).toHaveBeenCalledWith(expect.anything(), termId, expect.objectContaining({
      retainedShopIds: [shopA], confirmed: true,
    }));
    expect(page.querySelector('[data-testid="reduction-state"]')?.textContent).toContain('Scheduled for renewal');
  });

  it('hides reduction controls from billing managers and issues no reduction calls', async () => {
    const { fixture, page, api } = render('Manager');
    await settle(fixture);
    expect(page.querySelector('[data-testid="reduction-open"]')).toBeNull();
    expect(api.readSchedule).not.toHaveBeenCalled();
    expect(api.createPreview).not.toHaveBeenCalled();
  });

  it('shows conflicts in NeedsResolution and submits resolve with expected generation', async () => {
    const blocked = scheduleSample({
      state: 'NeedsResolution', reason: 'MemberCapacityExceeded', resolutionGeneration: 1,
      orderId: '12121212-1212-4121-8121-121212121212',
      conflicts: [{ shopId: shopA, shopName: 'Leap Central', reason: 'MemberCapacityExceeded', activeMembers: 3, targetAllowance: 2 }],
    });
    const { fixture, page, api } = render('Owner', {
      readSchedule: vi.fn(async () => blocked),
    });
    await settle(fixture);
    expect(page.querySelector('[data-testid="reduction-conflict"]')?.textContent).toContain('Leap Central: 3 / 2');
    (page.querySelector('[data-testid="reduction-resolve-submit"]') as HTMLButtonElement).click();
    await settle(fixture);
    expect(api.resolve).toHaveBeenCalledWith(expect.anything(), termId, blocked.scheduleId, expect.objectContaining({
      expectedGeneration: 1, snapshotHash: blocked.snapshotHash,
    }));
    expect(page.querySelector('[data-testid="reduction-state"]')?.textContent).toContain('Applying at renewal');
  });

  it('discards an in-flight preview answer if the active session scope changes', async () => {
    const drafts = new CapacityReductionDrafts();
    await drafts.savePreview({ userId: 'u1', shopId: 's1', account }, termId, {
      operationKey: crypto.randomUUID(), targetPlanVersionId: soloPlan.versionId, targetCapacity: 1,
      observedCapacity: 3, observedMemberAllowance: 5, observedPaidThrough: term.paidThrough,
    });
    let resolvePreview!: (p: CapacityReductionPreview) => void;
    const { fixture, page, scope } = render('Owner', {
      createPreview: vi.fn(() => new Promise<CapacityReductionPreview>(r => { resolvePreview = r; })),
    });
    await settle(fixture);
    scope.set({ userId: 'u2', shopId: 's1', epoch: 2 });
    await settle(fixture);
    resolvePreview(previewSample());
    await settle(fixture);
    expect(page.querySelector('[data-testid="reduction-preview"]')).toBeNull();
  });

  it('matches the shared capacity-reductions-v1 contract and rejects tampered shop pages', async () => {
    const scope = { userId: 'u1', shopId: 's1', account };
    const req = {
      operationKey: '99999999-9999-4999-8999-999999999999', targetPlanVersionId: soloPlan.versionId,
      targetCapacity: 1, observedCapacity: 3, observedMemberAllowance: 5, observedPaidThrough: term.paidThrough,
    };
    expect(contract.policy).toBe('capacity-reduction/1');
    expect(contract.states).toEqual(['Scheduled', 'Funded', 'Applying', 'Applied', 'NeedsResolution', 'NeedsReview']);
    expect(matchesPreviewRequest(previewSample(), scope, termId, req)).toBe(true);
    expect(isValidSchedule(scheduleSample(), scope, termId)).toBe(true);

    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const service = TestBed.inject(CapacityReductionService);
    const http = TestBed.inject(HttpTestingController);
    const hash = await shopSetHash([shopA, shopB]);
    const promise = service.previewShops(scope, previewSample({ sourceShopHash: hash }), () => true);
    const rejected = expect(promise).rejects.toThrow('Invalid capacity reduction shop page');
    http.expectOne(r => r.url.endsWith('/shops')).flush({
      accountId: termId, referenceId: previewSample().previewId, items: [], nextCursor: null,
    });
    await rejected;
    http.verify();
  });

  it('clears a stale schedule draft and renders expired error when fallback schedule submission fails with 409', async () => {
    const scope = { userId: 'u1', shopId: 's1', account };
    const drafts = new CapacityReductionDrafts();
    const saved = await drafts.saveSchedule(scope, termId, {
      operationKey: '99999999-9999-4999-8999-999999999998',
      previewId: previewSample().previewId,
      previewSnapshotHash: previewSample().snapshotHash,
      retainedShopIds: [shopA],
      customerTermsVersion: 'terms-1',
      refundPolicyVersion: 'refund-1',
      confirmed: true,
    });
    const { fixture, page, api } = render('Owner', {
      recoverSchedule: vi.fn(async () => { throw new HttpErrorResponse({ status: 404 }); }),
      schedule: vi.fn(async () => {
        throw new HttpErrorResponse({ status: 409, error: { code: 'Billing.CapacityReductionExpired' } });
      }),
    });
    await settle(fixture);

    expect(api.recoverSchedule).toHaveBeenCalledWith(scope, termId, saved.operationKey);
    expect(api.schedule).toHaveBeenCalledWith(scope, termId, saved);
    expect(drafts.read(scope, termId)?.schedule).toBeUndefined();
    expect(page.querySelector('[data-testid="reduction-failed"]')?.textContent)
      .toContain(en.billing.reductions.errors.expired);
  });

  it('rejects an invalid schedule payload on fallback recovery without clearing the draft or rendering scheduled', async () => {
    const scope = { userId: 'u1', shopId: 's1', account };
    const drafts = new CapacityReductionDrafts();
    await drafts.saveSchedule(scope, termId, {
      operationKey: '99999999-9999-4999-8999-999999999997',
      previewId: previewSample().previewId,
      previewSnapshotHash: previewSample().snapshotHash,
      retainedShopIds: [shopA],
      customerTermsVersion: 'terms-1',
      refundPolicyVersion: 'refund-1',
      confirmed: true,
    });
    const { fixture, page } = render('Owner', {
      recoverSchedule: vi.fn(async () => { throw new HttpErrorResponse({ status: 404 }); }),
      schedule: vi.fn(async () => scheduleSample({ termId: '00000000-0000-4000-8000-000000000001' })),
    });
    await settle(fixture);

    expect(page.querySelector('[data-testid="reduction-scheduled"]')).toBeNull();
    expect(page.querySelector('[data-testid="reduction-failed"]')).not.toBeNull();
    expect(drafts.read(scope, termId)?.schedule).toBeDefined();
  });
});
