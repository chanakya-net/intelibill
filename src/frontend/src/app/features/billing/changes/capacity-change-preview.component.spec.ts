import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import en from '../../../../../public/assets/i18n/en-IN.json';
import corpus from '../../../../../../../tests/contracts/licensing/proration-v1.json';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { stubMemoryStorage } from '../billing.test-utils';
import { CheckoutService } from '../checkout/checkout.service';
import type { PublishedPlan } from '../plans/billing-plans.service';
import { BillingAccountService } from '../services/billing-account.service';
import type { SubscriptionTermStatus } from '../status/subscription-status.models';
import { CapacityChangeQuoteDrafts } from './capacity-change-quote-drafts';
import { CapacityChangeQuoteService, type CapacityChangeQuote } from './capacity-change-quote.service';
import { CapacityChangePreviewComponent } from './capacity-change-preview.component';

const account = '33333333-3333-4333-8333-333333333333';
const termId = '44444444-4444-4444-8444-444444444444';
const plan = { versionId: '55555555-5555-4555-8555-555555555555', versionNumber: 2, name: 'Local growth', includedShops: 3,
  membersPerShop: 5, kind: 'multi-shop' } as PublishedPlan;
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

function render(role: 'Owner' | 'Manager', create: CapacityChangeQuoteService['create']) {
  const scope = signal({ userId: 'u1', shopId: 's1', epoch: 1 });
  const api = { create: vi.fn(create), shops: vi.fn(async () => shops) };
  TestBed.configureTestingModule({
    imports: [TranslocoTestingModule.forRoot({
      langs: { 'en-IN': en }, translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' }, preloadLangs: true,
    })],
    providers: [
      provideRouter([]),
      { provide: ShopContextService, useValue: { scope } },
      { provide: LocalizationService, useValue: { currentLanguage: signal('en-IN') } },
      { provide: BillingAccountService, useValue: { list: () => of([{ accountId: account, name: 'A', role, createdAt: '' }]) } },
      { provide: CheckoutService, useValue: { plans: async () => [plan] } },
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
  });
});
