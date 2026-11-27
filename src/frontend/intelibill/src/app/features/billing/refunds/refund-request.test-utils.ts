import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { BehaviorSubject } from 'rxjs';
import { vi } from 'vitest';
import en from '../../../../../public/assets/i18n/en-IN.json';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import type { CheckoutScope } from '../checkout/checkout.models';
import type { PaymentConfirmation } from '../payments/payment-confirmation.models';
import { PaymentConfirmationService } from '../payments/payment-confirmation.service';
import type { CreateRefundRequest, RefundRequestItem, RefundRequestPage } from './refund-request.models';
import { RefundRequestService } from './refund-request.service';
import { RefundRequestsPageComponent } from './refund-requests-page.component';

export const account = '11111111-1111-4111-8111-111111111111';
export const order = '33333333-3333-4333-8333-333333333333';
export const otherOrder = '33333333-3333-4333-8333-333333333334';
export const confirmation = '44444444-4444-4444-8444-444444444444';
export const savedKey = '66666666-6666-4666-8666-666666666666';
export const now = '2028-01-31T04:30:00Z';
export const draftScope: CheckoutScope = { userId: 'payer', shopId: 'shop-1', account };
export const storageKey =
  'intelibill.billing.refund-request.v1:' + JSON.stringify(['payer', 'shop-1', account, confirmation]);

export const verifiedPayment: PaymentConfirmation = {
  confirmationId: confirmation, accountId: account, orderId: order, source: 'Online',
  amountPaise: 12000, currency: 'INR', verifiedAt: now, issuedAt: now, issuanceBasis: 'Verification',
};

export function refundRow(command: Pick<CreateRefundRequest, 'amountPaise' | 'reason'>, index = 99): RefundRequestItem {
  return {
    schemaVersion: 1,
    refundRequestId: `55555555-5555-4555-8555-${String(index).padStart(12, '0')}`,
    accountId: account, orderId: order, confirmationId: confirmation, status: 'Requested',
    amountPaise: command.amountPaise, paymentAmountPaise: 12000, currency: 'INR',
    reason: command.reason, requestedAt: now,
  };
}

export function refundPage(items: readonly RefundRequestItem[] = [], nextCursor: string | null = null): RefundRequestPage {
  return { schemaVersion: 1, accountId: account, items, nextCursor };
}

/** Stores a command exactly as a page that already sent it once would have left it. */
export function saveDraft(reason: string, amountPaise = 4500, deliveries: number | undefined = 1): string {
  const payload = JSON.stringify({
    schemaVersion: 1,
    command: { operationKey: savedKey, orderId: order, confirmationId: confirmation, amountPaise, reason },
    deliveries,
  });
  localStorage.setItem(storageKey, payload);
  return payload;
}

export function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej; });
  return { promise, resolve, reject };
}

export interface RefundPageBackend {
  create: (scope: CheckoutScope, command: CreateRefundRequest) => Promise<RefundRequestItem>;
  list?: (scope: CheckoutScope, after?: string) => Promise<RefundRequestPage>;
  listConfirmations?: (scope: CheckoutScope, order: string) => Promise<readonly PaymentConfirmation[]>;
}

export function configureRefundPage(backend: RefundPageBackend) {
  const paramMap = new BehaviorSubject(convertToParamMap({ accountId: account }));
  const queryParamMap = new BehaviorSubject(convertToParamMap({ order, confirmation }));
  const scope = signal<{ userId: string; shopId: string } | null>({ userId: 'payer', shopId: 'shop-1' });
  const create = vi.fn(backend.create);
  const list = vi.fn(backend.list ?? (async () => refundPage()));
  const listConfirmations = vi.fn(backend.listConfirmations ?? (async () => [verifiedPayment]));
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
      { provide: PaymentConfirmationService, useValue: { list: listConfirmations } },
    ],
  });
  return { create, list, listConfirmations, paramMap, queryParamMap, scope };
}

/** Mounts one page instance: a tab, or the same tab after a reload. */
export function mountRefundPage() {
  const fixture = TestBed.createComponent(RefundRequestsPageComponent);
  fixture.detectChanges();
  const page = fixture.nativeElement as HTMLElement;
  const settle = async () => {
    await new Promise(resolve => setTimeout(resolve, 0));
    fixture.detectChanges();
  };
  const find = <T extends HTMLElement>(testId: string) => page.querySelector<T>(`[data-testid="${testId}"]`);
  const submit = async (amount: string, reason: string) => {
    for (const [testId, value] of [['refund-request-amount', amount], ['refund-request-reason', reason]]) {
      const input = find<HTMLInputElement>(testId)!;
      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
    }
    fixture.detectChanges();
    find<HTMLButtonElement>('refund-request-submit')!.click();
    await settle();
  };
  return { fixture, page, settle, find, submit, rows: () => page.querySelectorAll('[data-testid="refund-request-row"]') };
}

let originalLocks: PropertyDescriptor | undefined;
let locksInstalled = false;

export function installRefundSpecGlobals() {
  if (!locksInstalled) {
    originalLocks = Object.getOwnPropertyDescriptor(navigator, 'locks');
    locksInstalled = true;
  }
  Object.defineProperty(navigator, 'locks', {
    configurable: true,
    value: {
      request: (_name: string, optionsOrFn: unknown, maybeFn?: () => unknown) => {
        const fn = typeof optionsOrFn === 'function' ? (optionsOrFn as () => unknown) : maybeFn;
        return Promise.resolve(fn ? fn() : undefined);
      },
    },
  });
}

export function restoreRefundSpecGlobals() {
  if (!locksInstalled) return;
  if (originalLocks) Object.defineProperty(navigator, 'locks', originalLocks);
  else Reflect.deleteProperty(navigator, 'locks');
  originalLocks = undefined;
  locksInstalled = false;
}
