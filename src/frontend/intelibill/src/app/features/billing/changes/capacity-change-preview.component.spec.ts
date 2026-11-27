import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import en from '../../../../../public/assets/i18n/en-IN.json';
import corpus from '../../../../../../../../tests/contracts/licensing/proration-v1.json';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { stubMemoryStorage } from '../billing.test-utils';
import { CheckoutService } from '../checkout/checkout.service';
import type { PublishedPlan } from '../plans/billing-plans.service';
import { BillingAccountService } from '../services/billing-account.service';
import type { SubscriptionTermStatus } from '../status/subscription-status.models';
import { CapacityChangeQuoteDrafts } from './capacity-change-quote-drafts';
import { CapacityChangeQuoteService, matchesRequest, type CapacityChangeQuote } from './capacity-change-quote.service';
import { CapacityChangePreviewComponent } from './capacity-change-preview.component';

const account = '33333333-3333-4333-8333-333333333333';
const termId = '44444444-4444-4444-8444-444444444444';
const plan = { versionId: '55555555-5555-4555-8555-555555555555', versionNumber: 2, name: 'Local growth', includedShops: 3,
  membersPerShop: 5, kind: 'multi-shop', extraShopsOffered: true } as PublishedPlan;
const shops = [{ shopId: '66666666-6666-4666-8666-666666666666', name: 'Leap Central' },
  { shopId: '77777777-7777-4777-8777-777777777777', name: 'Leap East' }];
const term = { termId, basis: 'Paid', purchasedCapacity: 3, memberAllowance: 5, paidThrough: '2028-02-29T04:30:00+00:00',
  startsAt: '2028-01-31T04:30:00+00:00' } as SubscriptionTermStatus;

// The rendered contract is the frozen leap-month vector of the shared corpus; the browser never recomputes it.
const leap = corpus.vectors.find(vector => vector.name === 'leap-month-demo')!;

function quote(overrides: Partial<CapacityChangeQuote> = {}): CapacityChangeQuote {
  return {
    schemaVersion: 1, accountId: account, quoteId: '88888888-8888-4888-8888-888888888888', termId, outcome: 'Payable',
    reason: null, actions: ['AcceptIncrease'], targetPlanVersionId: plan.versionId, targetPlanName: 'Local growth',
    period: 'Month', priorCapacity: 3, targetCapacity: 4, priorMemberAllowance: 5, targetMemberAllowance: 5, shopCount: 2,
    shopHash: 'a'.repeat(64), purchasedGrossPaise: leap.purchasedGrossPaise, targetGrossPaise: leap.targetGrossPaise,
    differencePaise: leap.expected!.differencePaise, amountPaise: leap.expected!.chargePaise, currency: 'INR',
    intervalStartsAt: leap.startsAt, paidThrough: leap.endsAt, evaluatedAt: leap.evaluatedAt,
    totalSeconds: leap.expected!.totalSeconds, remainingSeconds: leap.expected!.remainingSeconds,
    anchorLocal: '2028-01-31T10:00:00', suspensionDisclosed: false, createdAt: leap.evaluatedAt,
    expiresAt: '2999-01-01T00:00:00Z', validUntil: '2999-01-01T00:00:00Z', developmentFixture: true,
    snapshotHash: 'b'.repeat(64), ...overrides,
  };
}

function render(role: 'Owner' | 'Manager', create: CapacityChangeQuoteService['create'], plans = [plan]) {
  const scope = signal({ userId: 'u1', shopId: 's1', epoch: 1 });
  const api = { create: vi.fn(create), shops: vi.fn(async () => shops),
    convert: vi.fn(async (_scope: unknown, _term: string, _order: string, _key: string, _hash: string) => {
      throw new HttpErrorResponse({ status: 503 });
    }), status: vi.fn(async (): Promise<unknown> => { throw new HttpErrorResponse({ status: 404 }); }) };
  TestBed.configureTestingModule({
    imports: [TranslocoTestingModule.forRoot({
      langs: { 'en-IN': en }, translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' }, preloadLangs: true,
    })],
    providers: [
      provideRouter([]),
      { provide: ShopContextService, useValue: { scope } },
      { provide: LocalizationService, useValue: { currentLanguage: signal('en-IN') } },
      { provide: BillingAccountService, useValue: { list: () => of([{ accountId: account, name: 'A', role, createdAt: '' }]) } },
      { provide: CheckoutService, useValue: { plans: async () => plans } },
      { provide: CapacityChangeQuoteService, useValue: api },
    ],
  });
  const fixture = TestBed.createComponent(CapacityChangePreviewComponent);
  fixture.componentRef.setInput('account', account);
  fixture.componentRef.setInput('term', term);
  fixture.detectChanges();
  return { fixture, page: fixture.nativeElement as HTMLElement, api, scope };
}

async function settle(fixture: { detectChanges(): void; whenStable(): Promise<unknown> }) {
  for (let i = 0; i < 5; i++) {
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
  Object.defineProperty(navigator, 'locks', { configurable: true,
    value: { request: async (_: string, run: () => unknown) => run() } });
});
afterEach(() => {
  TestBed.resetTestingModule();
  vi.unstubAllGlobals();
  if (originalLocks) Object.defineProperty(navigator, 'locks', originalLocks);
  else Reflect.deleteProperty(navigator, 'locks');
});

describe('capacity change preview', () => {
  it('saves the exact request before sending and renders the frozen leap-month quote', async () => {
    let savedBeforeSend = false;
    const { fixture, page, api } = render('Owner', async () => {
      savedBeforeSend = [...storage.values()].some(value => value.includes(plan.versionId));
      return quote();
    });
    await settle(fixture);
    (page.querySelector('[data-testid="capacity-change-open"]') as HTMLButtonElement).click();
    await settle(fixture);
    (page.querySelector('[data-testid="capacity-change-submit"]') as HTMLButtonElement).click();
    await settle(fixture);

    expect(savedBeforeSend).toBe(true);
    expect(page.querySelector('[data-testid="capacity-purchase"]')).not.toBeNull();
    const command = vi.mocked(api.create).mock.calls[0][2];
    expect(command).toMatchObject({ targetPlanVersionId: plan.versionId, targetCapacity: 4, observedCapacity: 3,
      observedPaidThrough: term.paidThrough });
    expect(page.querySelector('[data-testid="capacity-amount"]')?.textContent).toContain('108.62');
    expect(page.querySelector('[data-testid="capacity-seconds"]')?.textContent).toContain('1296000 of 2505600 seconds');
    expect(page.querySelector('[data-testid="capacity-paid-through"]')?.textContent).toContain('29 Feb 2028, 10:00:00 IST');
    expect(page.querySelector('[data-testid="capacity-interval-start"]')?.textContent).toContain('31 Jan 2028, 10:00:00 IST');
    expect(page.querySelector('[data-testid="capacity-old"]')?.textContent).toContain('3');
    expect(page.querySelector('[data-testid="capacity-new"]')?.textContent).toContain('4');
    expect(page.querySelector('[data-testid="capacity-shops"]')?.textContent).toContain('Leap Central');
    expect(page.querySelector('[data-testid="capacity-shops"]')?.textContent).toContain('Leap East');
  });

  it('recovers the one frozen quote after a reload by resending the saved request unchanged', async () => {
    const drafts = new CapacityChangeQuoteDrafts();
    const command = { operationKey: crypto.randomUUID(), targetPlanVersionId: plan.versionId, targetCapacity: 4,
      observedCapacity: 3, observedPaidThrough: term.paidThrough };
    await drafts.prepare({ userId: 'u1', shopId: 's1', account }, termId, command);
    const { fixture, page, api } = render('Owner', async () => quote());
    await settle(fixture);
    expect(api.create).toHaveBeenCalledTimes(1);
    expect(vi.mocked(api.create).mock.calls[0][2]).toEqual(command);
    expect(page.querySelector('[data-testid="capacity-change-quote"]')?.getAttribute('data-quote-id')).toBe(quote().quoteId);
  });

  it('shows nothing to a billing manager and never requests a quote', async () => {
    const { fixture, page, api } = render('Manager', async () => quote());
    await settle(fixture);
    expect(page.querySelector('[data-testid="capacity-change-open"]')).toBeNull();
    expect(api.create).not.toHaveBeenCalled();
  });

  it('offers renewal or courtesy without any payable amount for a nonpositive difference', async () => {
    const drafts = new CapacityChangeQuoteDrafts();
    await drafts.prepare({ userId: 'u1', shopId: 's1', account }, termId, { operationKey: crypto.randomUUID(),
      targetPlanVersionId: plan.versionId, targetCapacity: 3, observedCapacity: 3, observedPaidThrough: term.paidThrough });
    const { fixture, page } = render('Owner', async () => quote({ outcome: 'AtRenewalOrCourtesy', reason: 'NonPositiveDifference',
      actions: ['ScheduleAtRenewal', 'RequestCourtesy'], amountPaise: null, targetCapacity: 3, differencePaise: 0 }));
    await settle(fixture);
    expect(page.querySelector('[data-testid="capacity-amount"]')).toBeNull();
    expect(page.querySelector('[data-testid="capacity-outcome"]')?.textContent).toContain('courtesy grant');
    expect(page.querySelector('[data-testid="capacity-reason"]')?.textContent).toContain('does not cost more');
  });

  it('discards an answer that arrives after the session changed', async () => {
    const drafts = new CapacityChangeQuoteDrafts();
    await drafts.prepare({ userId: 'u1', shopId: 's1', account }, termId, { operationKey: crypto.randomUUID(),
      targetPlanVersionId: plan.versionId, targetCapacity: 4, observedCapacity: 3, observedPaidThrough: term.paidThrough });
    let answer!: (value: CapacityChangeQuote) => void;
    const { fixture, page, scope } = render('Owner', () => new Promise(resolve => { answer = resolve; }));
    await settle(fixture);
    scope.set({ userId: 'u2', shopId: 's1', epoch: 2 });
    await settle(fixture);
    answer(quote());
    await settle(fixture);
    expect(page.querySelector('[data-testid="capacity-change-quote"]')).toBeNull();
  });

  it('requires an explicit new preview once the quote expired and maps refusals to known messages', async () => {
    const drafts = new CapacityChangeQuoteDrafts();
    await drafts.prepare({ userId: 'u1', shopId: 's1', account }, termId, { operationKey: crypto.randomUUID(),
      targetPlanVersionId: plan.versionId, targetCapacity: 4, observedCapacity: 3, observedPaidThrough: term.paidThrough });
    const expired = render('Owner', async () => quote({ validUntil: '2000-01-01T00:00:00Z' }));
    await settle(expired.fixture);
    expect(expired.page.querySelector('[data-testid="capacity-expired"]')).not.toBeNull();
    TestBed.resetTestingModule();

    const refused = render('Owner', async () => {
      throw new HttpErrorResponse({ status: 409, error: { code: 'Billing.CapacityChangeSourceChanged' } });
    });
    await settle(refused.fixture);
    expect(refused.page.querySelector('[data-testid="capacity-change-failed"]')?.textContent).toContain('This subscription changed');
    expect(storage.size).toBe(0);
    refused.fixture.componentRef.setInput('term', { ...term, purchasedCapacity: 4 });
    await settle(refused.fixture);
    expect(refused.api.create).toHaveBeenCalledTimes(1);
    expect(refused.page.querySelector('[data-testid="capacity-change-open"]')).not.toBeNull();
  });
});

const ownerScope = { userId: 'u1', shopId: 's1', account };
const command = { operationKey: '99999999-9999-4999-8999-999999999999', targetPlanVersionId: plan.versionId,
  targetCapacity: 4, observedCapacity: 3, observedPaidThrough: term.paidThrough };

describe('capacity change response validation', () => {
  it('rejects nonpayable amounts and a different paid-through instant', () => {
    expect(matchesRequest(quote(), ownerScope, termId, command)).toBe(true);
    expect(matchesRequest(quote({ outcome: 'AtRenewalOrCourtesy', amountPaise: 0 }), ownerScope, termId, command)).toBe(false);
    expect(matchesRequest(quote({ paidThrough: term.startsAt }), ownerScope, termId, command)).toBe(false);
  });

  it('rejects a frozen shop page for a different account', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const api = TestBed.inject(CapacityChangeQuoteService);
    const requests = TestBed.inject(HttpTestingController);
    const page = api.shops(ownerScope, quote({ shopCount: 0 }), () => true);
    const rejected = expect(page).rejects.toThrow('Invalid capacity quote shop page');
    requests.expectOne(request => request.url.endsWith('/shops'))
      .flush({ accountId: termId, quoteId: quote().quoteId, items: [], nextCursor: null });
    await rejected;
    requests.verify();
  });
});

describe('capacity change outcomes and draft recovery', () => {
  it.each([
    ['Payable', ['AcceptIncrease'], true, false, false],
    ['RenewalRequired', ['Renew'], true, true, true],
    ['TrialConversion', ['PurchasePaid'], false, true, false],
    ['NewTermRequired', ['PurchaseNewTerm'], false, true, false],
  ] as const)('renders the renewal date and actions for %s', async (outcome, actions, anchor, checkout, suspended) => {
    await new CapacityChangeQuoteDrafts().prepare(ownerScope, termId, command);
    const { fixture, page } = render('Owner', async () => quote({ outcome, actions, suspensionDisclosed: suspended,
      amountPaise: outcome === 'Payable' ? leap.expected!.chargePaise : null }));
    await settle(fixture);
    const panel = page.querySelector('[data-testid="capacity-change-quote"]')!;
    expect(panel).not.toBeNull();
    expect(!!panel.querySelector('[data-testid="capacity-anchor"]')).toBe(anchor);
    expect(!!panel.querySelector('[data-testid="capacity-checkout-link"]')).toBe(checkout);
    expect(!!panel.querySelector('[data-testid="capacity-suspension"]')).toBe(suspended);
    expect(panel.getAttribute('aria-labelledby')).toBe('capacity-quote-heading-' + termId);
    expect(panel.querySelector('h3')?.id).toBe('capacity-quote-heading-' + termId);
  });

  it('retains the exact draft on a 503 and recovers it on reload', async () => {
    const drafts = new CapacityChangeQuoteDrafts();
    await drafts.prepare(ownerScope, termId, command);
    const failed = render('Owner', async () => { throw new HttpErrorResponse({ status: 503 }); });
    await settle(failed.fixture);
    expect(failed.page.querySelector('[data-testid="capacity-change-failed"]')).not.toBeNull();
    expect(drafts.read(ownerScope, termId)?.command).toEqual(command);
    TestBed.resetTestingModule();
    const retried = render('Owner', async () => quote());
    await settle(retried.fixture);
    expect(retried.api.create).toHaveBeenCalledWith(ownerScope, termId, command);
    expect(retried.page.querySelector('[data-testid="capacity-change-quote"]')).not.toBeNull();
  });

  it('keeps the same purchase after a lost conversion and a missing order on reload', async () => {
    const drafts = new CapacityChangeQuoteDrafts();
    await drafts.prepare(ownerScope, termId, command);
    const first = render('Owner', async () => quote());
    await settle(first.fixture);
    (first.page.querySelector('[data-testid="capacity-purchase"]') as HTMLButtonElement).click();
    await settle(first.fixture);
    const original = drafts.read(ownerScope, termId)!.purchase!;
    expect(first.api.convert).toHaveBeenCalledWith(ownerScope, termId, original.quoteId, original.operationKey, original.snapshotHash);
    await drafts.discard(ownerScope, termId, command.operationKey);
    expect(drafts.read(ownerScope, termId)?.purchase).toEqual(original);
    TestBed.resetTestingModule();
    const reloaded = render('Owner', async () => quote());
    await settle(reloaded.fixture);
    expect(reloaded.api.create).not.toHaveBeenCalled();
    expect(reloaded.page.querySelector('[data-testid="capacity-order"]')?.textContent).toContain(original.quoteId);
    expect(reloaded.page.querySelector('[data-testid="capacity-purchase"]')).toBeNull();
    (reloaded.page.querySelector('section button') as HTMLButtonElement).click();
    await settle(reloaded.fixture);
    expect(reloaded.api.convert.mock.calls).toEqual(first.api.convert.mock.calls);
  });

  async function reloadPurchase(payment: string | number, fulfilment = 'NotFulfilled') {
    const drafts = new CapacityChangeQuoteDrafts();
    await drafts.prepare(ownerScope, termId, command);
    const original = await drafts.purchase(ownerScope, termId, command.operationKey, quote().quoteId, quote().snapshotHash);
    const result = render('Owner', async () => quote({ quoteId: crypto.randomUUID() }));
    if (typeof payment === 'number') result.api.status.mockRejectedValue(new HttpErrorResponse({ status: payment }));
    else result.api.status.mockResolvedValue({ payment, fulfilment, attemptId: crypto.randomUUID(), manualPayment: false });
    await settle(result.fixture);
    expect(result.api.status).toHaveBeenCalledWith(ownerScope, original.quoteId);
    expect(result.page.querySelector('[data-testid="capacity-order"]')?.textContent).toContain(original.quoteId);
    const restart = [...result.page.querySelectorAll('button')].find(button => button.textContent.includes(en.billing.changes.again));
    return { ...result, drafts, restart, original };
  }

  it('starts over after definitive failure with fresh preview and purchase keys', async () => {
    const { fixture, page, api, drafts, restart, original } = await reloadPurchase('Failed');
    expect(restart).toBeDefined();
    restart!.click();
    await settle(fixture);
    expect(drafts.read(ownerScope, termId)).toBeNull();
    (page.querySelector('[data-testid="capacity-change-submit"]') as HTMLButtonElement).click();
    await settle(fixture);
    (page.querySelector('[data-testid="capacity-purchase"]') as HTMLButtonElement).click();
    await settle(fixture);
    expect(api.create.mock.calls[0][2].operationKey).not.toBe(command.operationKey);
    expect(api.convert.mock.calls[0][2]).not.toBe(original.quoteId);
    expect(api.convert.mock.calls[0][3]).not.toBe(original.operationKey);
  });

  it.each([['Verified', 'Pending'], ['Verified', 'NeedsReview'], ['AwaitingPayment', 'NotFulfilled'],
    [404, 'NotFulfilled'], [503, 'NotFulfilled']] as const)('retains purchase on %s / %s', async (payment, fulfilment) => {
    const { drafts, original, restart } = await reloadPurchase(payment, fulfilment);
    expect(restart).toBeUndefined();
    expect(drafts.read(ownerScope, termId)?.purchase).toEqual(original);
  });

  it('clears corrupted storage when the owner requests a new preview', async () => {
    storage.set(new CapacityChangeQuoteDrafts().key(ownerScope, termId), '{corrupt');
    const { fixture, page, api } = render('Owner', async () => quote());
    await settle(fixture);
    (page.querySelector('[data-testid="capacity-change-failed"] button') as HTMLButtonElement).click();
    await settle(fixture);
    expect(storage.size).toBe(0);
    expect(page.querySelector('[data-testid="capacity-change-form"]')).not.toBeNull();
    expect(api.create).not.toHaveBeenCalled();
  });

  it('filters ineligible plans and selects the fixed included capacity when switching', async () => {
    const fixed = { ...plan, versionId: command.operationKey, extraShopsOffered: false };
    const ineligible = { ...fixed, versionId: termId, includedShops: 1, kind: 'single-shop' } as PublishedPlan;
    const flexible = { ...plan, includedShops: 1 };
    const { fixture, page } = render('Owner', async () => quote(), [flexible, ineligible, fixed]);
    fixture.componentRef.setInput('term', { ...term, purchasedCapacity: 1 });
    await settle(fixture);
    (page.querySelector('[data-testid="capacity-change-open"]') as HTMLButtonElement).click();
    await settle(fixture);
    const select = page.querySelector('select')!;
    expect([...select.options].map(option => option.value)).toEqual([flexible.versionId, fixed.versionId]);
    expect(page.querySelector('input')?.value).toBe('2');
    select.value = fixed.versionId;
    select.dispatchEvent(new Event('change'));
    await settle(fixture);
    expect(page.querySelector('input')?.value).toBe('3');
    expect(page.querySelector('input')?.readOnly).toBe(true);
    expect(page.querySelector('form')?.getAttribute('aria-labelledby')).toBe('capacity-change-heading-' + termId);
    expect(page.querySelector('h3')?.id).toBe('capacity-change-heading-' + termId);
  });
});
