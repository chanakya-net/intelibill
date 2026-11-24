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
import type { PaymentConfirmation } from '../payments/payment-confirmation.models';
import { PaymentConfirmationService } from '../payments/payment-confirmation.service';
import { RefundRequestDrafts } from './refund-request-drafts';
import {
  REFUND_STATUSES, type CreateRefundRequest, type RefundRequestItem, type RefundRequestPage,
} from './refund-request.models';
import { RefundRequestService } from './refund-request.service';
import { RefundRequestsPageComponent } from './refund-requests-page.component';

const account = '11111111-1111-4111-8111-111111111111';
const otherAccount = '22222222-2222-4222-8222-222222222222';
const order = '33333333-3333-4333-8333-333333333333';
const confirmation = '44444444-4444-4444-8444-444444444444';
const now = '2028-01-31T04:30:00Z';

const verifiedPayment: PaymentConfirmation = {
  confirmationId: confirmation, accountId: account, orderId: order, source: 'Online',
  amountPaise: 12000, currency: 'INR', verifiedAt: now, issuedAt: now, issuanceBasis: 'Verification',
};

function makeRow(status: RefundRequestItem['status'], index: number): RefundRequestItem {
  return {
    schemaVersion: 1,
    refundRequestId: `55555555-5555-4555-8555-${String(index + 1).padStart(12, '0')}`,
    accountId: account,
    orderId: order,
    confirmationId: confirmation,
    status,
    amountPaise: 4500,
    paymentAmountPaise: 12000,
    currency: 'INR',
    reason: `Refund reason ${index + 1}`,
    requestedAt: now,
  };
}

function setup(options: {
  items?: readonly RefundRequestItem[];
  withQuery?: boolean;
  create?: (scope: unknown, command: CreateRefundRequest) => Promise<RefundRequestItem>;
  list?: (scope: unknown, after?: string) => Promise<RefundRequestPage>;
} = {}) {
  const paramMap = new BehaviorSubject(convertToParamMap({ accountId: account }));
  const queryParamMap = new BehaviorSubject(convertToParamMap(
    options.withQuery ? { order, confirmation } : {},
  ));
  const scope = signal<{ userId: string; shopId: string } | null>({ userId: 'payer', shopId: 'shop-1' });
  const list = vi.fn(options.list ?? (async (): Promise<RefundRequestPage> => ({
    schemaVersion: 1, accountId: account, items: options.items ?? [], nextCursor: null,
  })));
  const create = vi.fn(options.create ?? (async (_scope, command: CreateRefundRequest): Promise<RefundRequestItem> => ({
    ...makeRow('Requested', 99),
    orderId: command.orderId,
    confirmationId: command.confirmationId,
    amountPaise: command.amountPaise,
    reason: command.reason,
  })));
  const confirmations = {
    list: vi.fn(async () => [verifiedPayment]),
  };

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
      { provide: PaymentConfirmationService, useValue: confirmations },
    ],
  });

  const fixture = TestBed.createComponent(RefundRequestsPageComponent);
  fixture.detectChanges();
  const settle = async () => {
    await new Promise(resolve => setTimeout(resolve, 0));
    fixture.detectChanges();
  };
  return {
    fixture,
    page: fixture.nativeElement as HTMLElement,
    drafts: TestBed.inject(RefundRequestDrafts),
    scope,
    paramMap,
    list,
    create,
    settle,
  };
}

beforeEach(() => {
  stubMemoryStorage();
  Object.defineProperty(navigator, 'locks', {
    value: { request: (_name: string, fn: () => unknown) => Promise.resolve(fn()) },
    configurable: true,
  });
});
afterEach(() => {
  vi.unstubAllGlobals();
  TestBed.resetTestingModule();
});

describe('merchant refund requests page', () => {
  it('renders distinct copy for all five refund statuses and states no approval or access change', async () => {
    const rows = REFUND_STATUSES.map((status, index) => makeRow(status, index));
    const { page, settle } = setup({ items: rows });
    await settle();

    const notice = page.querySelector('[data-testid="refund-request-notice"]')?.textContent ?? '';
    expect(notice).toContain('does not approve a refund');
    expect(notice).toContain('move money');
    expect(notice).toContain('access');

    const labels = Array.from(page.querySelectorAll('[data-testid="refund-request-status"]'))
      .map(el => el.textContent?.trim() ?? '');
    const details = Array.from(page.querySelectorAll('[data-testid="refund-request-status-detail"]'))
      .map(el => el.textContent?.trim() ?? '');

    expect(labels).toHaveLength(5);
    expect(new Set(labels).size).toBe(5);
    expect(new Set(details).size).toBe(5);
    expect(details[0]).toContain('not approved');
  });

  it('saves a draft before send and resends the exact operation key after reload', async () => {
    const savedCommand: CreateRefundRequest = {
      operationKey: '66666666-6666-4666-8666-666666666666',
      orderId: order,
      confirmationId: confirmation,
      amountPaise: 4500,
      reason: 'Saved before reload',
    };
    const key = 'intelibill.billing.refund-request.v1:' + JSON.stringify(['payer', 'shop-1', account, confirmation]);
    localStorage.setItem(key, JSON.stringify({ schemaVersion: 1, command: savedCommand }));

    const { page, create, settle } = setup({ withQuery: true });
    await settle();

    expect(create).toHaveBeenCalledTimes(1);
    expect(create.mock.calls[0][1]).toEqual(savedCommand);
    expect(localStorage.getItem(key)).toBeNull();
    expect(page.querySelectorAll('[data-testid="refund-request-row"]')).toHaveLength(1);
  });

  it.each([
    { status: 400, code: 'Billing.RefundAmountInvalid', expected: 'positive amount' },
    { status: 403, code: 'Billing.DelegationDenied', expected: 'permission' },
    { status: 404, code: 'Billing.RefundPaymentNotFound', expected: 'not found' },
    { status: 409, code: 'Billing.OperationPayloadMismatch', expected: 'different details' },
  ])('discards the saved draft when licensing refuses with $status ($code)', async ({ status, code, expected }) => {
    const key = 'intelibill.billing.refund-request.v1:' + JSON.stringify(['payer', 'shop-1', account, confirmation]);
    const savedCommand: CreateRefundRequest = {
      operationKey: '77777777-7777-4777-8777-777777777777',
      orderId: order,
      confirmationId: confirmation,
      amountPaise: 4500,
      reason: 'Will be rejected',
    };
    localStorage.setItem(key, JSON.stringify({ schemaVersion: 1, command: savedCommand }));

    const { page, settle } = setup({
      withQuery: true,
      create: async () => { throw new HttpErrorResponse({ status, error: { code } }); },
    });
    await settle();

    expect(localStorage.getItem(key)).toBeNull();
    expect(page.querySelector('[data-testid="refund-request-error"]')?.textContent).toContain(expected);
  });

  it('drops an in-flight response when the active scope changes', async () => {
    let resolveFirst!: (page: RefundRequestPage) => void;
    let calls = 0;
    const { page, scope, paramMap, settle } = setup({
      list: () => {
        calls++;
        if (calls === 1) {
          return new Promise<RefundRequestPage>(resolve => { resolveFirst = resolve; });
        }
        return Promise.resolve({ schemaVersion: 1, accountId: otherAccount, items: [], nextCursor: null });
      },
    });

    scope.set({ userId: 'other-user', shopId: 'shop-2' });
    paramMap.next(convertToParamMap({ accountId: otherAccount }));
    await settle();

    resolveFirst({ schemaVersion: 1, accountId: account, items: [makeRow('Requested', 1)], nextCursor: null });
    await settle();

    expect(page.querySelectorAll('[data-testid="refund-request-row"]')).toHaveLength(0);
    expect(page.querySelector('[data-testid="refund-request-empty"]')).not.toBeNull();
  });
});
