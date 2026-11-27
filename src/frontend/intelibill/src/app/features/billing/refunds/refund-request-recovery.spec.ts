import { HttpErrorResponse } from '@angular/common/http';
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
import type { CheckoutScope } from '../checkout/checkout.models';
import type { PaymentConfirmation } from '../payments/payment-confirmation.models';
import { PaymentConfirmationService } from '../payments/payment-confirmation.service';
import { InvalidRefundRequestDraft, RefundRequestDrafts } from './refund-request-drafts';
import type { CreateRefundRequest, RefundRequestItem, RefundRequestPage } from './refund-request.models';
import { installRefundSpecGlobals, restoreRefundSpecGlobals } from './refund-request.test-utils';
import { RefundRequestService } from './refund-request.service';
import { RefundRequestsPageComponent } from './refund-requests-page.component';

const account = '11111111-1111-4111-8111-111111111111';
const order = '33333333-3333-4333-8333-333333333333';
const confirmation = '44444444-4444-4444-8444-444444444444';
const now = '2028-01-31T04:30:00Z';
const draftScope: CheckoutScope = { userId: 'payer', shopId: 'shop-1', account };
const storageKey = 'intelibill.billing.refund-request.v1:' + JSON.stringify(['payer', 'shop-1', account, confirmation]);

const verifiedPayment: PaymentConfirmation = {
  confirmationId: confirmation, accountId: account, orderId: order, source: 'Online',
  amountPaise: 12000, currency: 'INR', verifiedAt: now, issuedAt: now, issuanceBasis: 'Verification',
};

function makeCreated(command: CreateRefundRequest): RefundRequestItem {
  return {
    schemaVersion: 1,
    refundRequestId: '55555555-5555-4555-8555-000000000099',
    accountId: account,
    orderId: command.orderId,
    confirmationId: command.confirmationId,
    status: 'Requested',
    amountPaise: command.amountPaise,
    paymentAmountPaise: 12000,
    currency: 'INR',
    reason: command.reason,
    requestedAt: now,
  };
}

function typeValue(el: HTMLInputElement | HTMLTextAreaElement, value: string) {
  el.value = value;
  el.dispatchEvent(new Event('input', { bubbles: true }));
}

function configure(create: ReturnType<typeof vi.fn>) {
  const paramMap = new BehaviorSubject(convertToParamMap({ accountId: account }));
  const queryParamMap = new BehaviorSubject(convertToParamMap({ order, confirmation }));
  const scope = signal<{ userId: string; shopId: string } | null>({ userId: 'payer', shopId: 'shop-1' });
  const list = vi.fn(async (): Promise<RefundRequestPage> => ({
    schemaVersion: 1, accountId: account, items: [], nextCursor: null,
  }));
  TestBed.configureTestingModule({
    imports: [
      TranslocoTestingModule.forRoot({
        langs: { 'en-IN': en },
        translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' },
        preloadLangs: true,
      }),
    ],
    providers: [
      provideRouter([]),
      { provide: ActivatedRoute, useValue: { paramMap, queryParamMap } },
      { provide: ShopContextService, useValue: { scope } },
      { provide: LocalizationService, useValue: { currentLanguage: () => 'en-IN' } },
      { provide: RefundRequestService, useValue: { list, create } },
      { provide: PaymentConfirmationService, useValue: { list: vi.fn(async () => [verifiedPayment]) } },
    ],
  });
}

function mount() {
  const fixture = TestBed.createComponent(RefundRequestsPageComponent);
  fixture.detectChanges();
  const settle = async () => {
    await new Promise(resolve => setTimeout(resolve, 0));
    fixture.detectChanges();
  };
  return { fixture, page: fixture.nativeElement as HTMLElement, settle };
}

beforeEach(() => {
  stubMemoryStorage();
  installRefundSpecGlobals();
});
afterEach(() => {
  restoreRefundSpecGlobals();
  vi.unstubAllGlobals();
  TestBed.resetTestingModule();
});

describe('merchant refund request draft recovery', () => {
  it.each([
    { label: 'route-level 404', retryError: new HttpErrorResponse({ status: 404 }) },
    { label: 'pre-replay 403 denial', retryError: new HttpErrorResponse({ status: 403, error: { code: 'Billing.DelegationDenied' } }) },
  ])('preserves the original key across lost answer and $label, then recovers one request', async ({ retryError }) => {
    let attempt = 0;
    let savedAtTransportEntry: string | null = null;
    const observedKeys: string[] = [];
    const create = vi.fn(async (_scope: CheckoutScope, command: CreateRefundRequest) => {
      attempt++;
      observedKeys.push(command.operationKey);
      if (attempt === 1) {
        savedAtTransportEntry = localStorage.getItem(storageKey);
        throw new HttpErrorResponse({ status: 0 });
      }
      if (attempt === 2) throw retryError;
      return makeCreated(command);
    });

    configure(create);
    const first = mount();
    await first.settle();
    typeValue(first.page.querySelector('[data-testid="refund-request-amount"]') as HTMLInputElement, '45.00');
    typeValue(first.page.querySelector('[data-testid="refund-request-reason"]') as HTMLTextAreaElement, 'Lost answer recovery');
    first.fixture.detectChanges();
    (first.page.querySelector('[data-testid="refund-request-submit"]') as HTMLButtonElement).click();
    await first.settle();

    expect(savedAtTransportEntry).not.toBeNull();
    const originalKey = JSON.parse(savedAtTransportEntry!).command.operationKey as string;
    expect(localStorage.getItem(storageKey)).toContain(originalKey);
    first.fixture.destroy();

    // Recreate page (reload): automatic resend hits route-level 404 or pre-replay 403 and must preserve the key.
    const reloaded = mount();
    await reloaded.settle();
    expect(localStorage.getItem(storageKey)).toContain(originalKey);
    expect(reloaded.page.querySelector('[data-testid="refund-request-pending"]')).not.toBeNull();
    expect(reloaded.page.querySelector('[data-testid="refund-pending-amount"]')?.textContent).toContain('45.00');
    expect((reloaded.page.querySelector('[data-testid="refund-request-amount"]') as HTMLInputElement).value).toBe('45.00');

    // Explicit recovery sends the original key and completes with one request.
    (reloaded.page.querySelector('[data-testid="refund-request-retry"]') as HTMLButtonElement).click();
    await reloaded.settle();
    expect(observedKeys).toEqual([originalKey, originalKey, originalKey]);
    expect(localStorage.getItem(storageKey)).toBeNull();
    expect(reloaded.page.querySelectorAll('[data-testid="refund-request-row"]')).toHaveLength(1);
  });

  it('keeps ordinary first-attempt authoritative refusals actionable and discards the draft', async () => {
    const create = vi.fn(async () => {
      throw new HttpErrorResponse({ status: 400, error: { code: 'Billing.RefundAmountInvalid' } });
    });
    configure(create);
    const { fixture, page, settle } = mount();
    await settle();

    typeValue(page.querySelector('[data-testid="refund-request-amount"]') as HTMLInputElement, '45.00');
    typeValue(page.querySelector('[data-testid="refund-request-reason"]') as HTMLTextAreaElement, 'Will be rejected');
    fixture.detectChanges();
    (page.querySelector('[data-testid="refund-request-submit"]') as HTMLButtonElement).click();
    await settle();

    expect(localStorage.getItem(storageKey)).toBeNull();
    expect(page.querySelector('[data-testid="refund-request-error"]')?.textContent).toContain('positive amount');
    expect((page.querySelector('[data-testid="refund-request-submit"]') as HTMLButtonElement).disabled).toBe(false);
  });

  it('surfaces an existing pending intent accurately when a second tab submits different values', async () => {
    const create = vi.fn(async (_scope: CheckoutScope, command: CreateRefundRequest) => {
      if (create.mock.calls.length === 1) throw new HttpErrorResponse({ status: 503 });
      return makeCreated(command);
    });
    configure(create);
    const tab1 = mount();
    const tab2 = mount();
    document.body.appendChild(tab1.page);
    await tab1.settle();
    await tab2.settle();

    typeValue(tab1.page.querySelector('[data-testid="refund-request-amount"]') as HTMLInputElement, '45.00');
    typeValue(tab1.page.querySelector('[data-testid="refund-request-reason"]') as HTMLTextAreaElement, 'First tab partial refund');
    tab1.fixture.detectChanges();
    (tab1.page.querySelector('[data-testid="refund-request-submit"]') as HTMLButtonElement).click();
    await tab1.settle();
    expect(create).toHaveBeenCalledTimes(1);

    typeValue(tab2.page.querySelector('[data-testid="refund-request-amount"]') as HTMLInputElement, '80.00');
    typeValue(tab2.page.querySelector('[data-testid="refund-request-reason"]') as HTMLTextAreaElement, 'Second tab different reason');
    tab2.fixture.detectChanges();
    (tab2.page.querySelector('[data-testid="refund-request-submit"]') as HTMLButtonElement).click();
    await tab2.settle();

    expect(create).toHaveBeenCalledTimes(1);
    expect(tab2.page.querySelector('[data-testid="refund-request-submitted"]')).toBeNull();
    expect(tab2.page.querySelector('[data-testid="refund-pending-amount"]')?.textContent).toContain('45.00');
    expect(tab2.page.querySelector('[data-testid="refund-pending-reason"]')?.textContent).toBe('First tab partial refund');
    expect((tab2.page.querySelector('[data-testid="refund-request-amount"]') as HTMLInputElement).value).toBe('45.00');
    expect((tab2.page.querySelector('[data-testid="refund-request-reason"]') as HTMLTextAreaElement).value).toBe('First tab partial refund');

    (tab2.page.querySelector('[data-testid="refund-request-retry"]') as HTMLButtonElement).click();
    await tab2.settle();
    expect(create).toHaveBeenCalledTimes(2);
    expect(create.mock.calls[1][1]).toEqual(create.mock.calls[0][1]);
  });

  it.each([
    { label: 'malformed JSON', payload: '{not-valid-json' },
    {
      label: 'newer schemaVersion',
      payload: JSON.stringify({
        schemaVersion: 2,
        command: {
          operationKey: '66666666-6666-4666-8666-666666666666',
          orderId: order, confirmationId: confirmation, amountPaise: 4500, reason: 'Future version',
        },
      }),
    },
    {
      label: 'null reason',
      payload: JSON.stringify({
        schemaVersion: 1,
        command: {
          operationKey: '66666666-6666-4666-8666-666666666666',
          orderId: order, confirmationId: confirmation, amountPaise: 4500, reason: null,
        },
      }),
    },
  ])('preserves unreadable local data ($label) across reload without an automatic send', async ({ payload }) => {
    localStorage.setItem(storageKey, payload);
    const create = vi.fn();
    configure(create);
    const drafts = TestBed.inject(RefundRequestDrafts);
    expect(() => drafts.read(draftScope, confirmation, order)).toThrow(InvalidRefundRequestDraft);

    const { page, settle } = mount();
    await settle();

    expect(create).not.toHaveBeenCalled();
    expect(localStorage.getItem(storageKey)).toBe(payload);
    expect(page.querySelector('[data-testid="refund-request-unreadable"]')).not.toBeNull();
    expect((page.querySelector('[data-testid="refund-request-submit"]') as HTMLButtonElement).disabled).toBe(true);

    (page.querySelector('[data-testid="refund-request-clear-unreadable"]') as HTMLButtonElement).click();
    await settle();
    expect(localStorage.getItem(storageKey)).toBeNull();
    expect(page.querySelector('[data-testid="refund-request-unreadable"]')).toBeNull();
  });
});
