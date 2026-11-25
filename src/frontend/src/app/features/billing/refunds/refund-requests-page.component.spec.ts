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
import { CONFIRMATION_PAGE_SIZE, type PaymentConfirmation } from '../payments/payment-confirmation.models';
import { PaymentConfirmationService } from '../payments/payment-confirmation.service';
import {
  normalizeRefundReason, validateRefundPage, REFUND_STATUSES, type CreateRefundRequest, type RefundRequestItem, type RefundRequestPage,
} from './refund-request.models';
import { installRefundSpecGlobals, restoreRefundSpecGlobals } from './refund-request.test-utils';
import { RefundRequestService } from './refund-request.service';
import { RefundRequestsPageComponent } from './refund-requests-page.component';

const account = '11111111-1111-4111-8111-111111111111';
const otherAccount = '22222222-2222-4222-8222-222222222222';
const order = '33333333-3333-4333-8333-333333333333';
const otherOrder = '33333333-3333-4333-8333-333333333334';
const confirmation = '44444444-4444-4444-8444-444444444444';
const otherConfirmation = '44444444-4444-4444-8444-444444444445';
const thirdConfirmation = '44444444-4444-4444-8444-444444444446';
const now = '2028-01-31T04:30:00Z';

function makeConfirmation(id: string, acc: string, ord: string, amountPaise: number): PaymentConfirmation {
  return {
    confirmationId: id, accountId: acc, orderId: ord, source: 'Online',
    amountPaise, currency: 'INR', verifiedAt: now, issuedAt: now, issuanceBasis: 'Verification',
  };
}

function makeRow(status: RefundRequestItem['status'], index: number, acc = account): RefundRequestItem {
  return {
    schemaVersion: 1,
    refundRequestId: `55555555-5555-4555-8555-${String(index + 1).padStart(12, '0')}`,
    accountId: acc,
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

function typeValue(el: HTMLInputElement | HTMLTextAreaElement, value: string) {
  el.value = value;
  el.dispatchEvent(new Event('input', { bubbles: true }));
}

function setup(options: {
  items?: readonly RefundRequestItem[];
  withQuery?: boolean;
  query?: Record<string, string>;
  create?: (scope: CheckoutScope, command: CreateRefundRequest) => Promise<RefundRequestItem>;
  list?: (scope: CheckoutScope, after?: string) => Promise<RefundRequestPage>;
  listConfirmations?: (scope: CheckoutScope, ord: string, after?: string) => Promise<readonly PaymentConfirmation[]>;
} = {}) {
  const paramMap = new BehaviorSubject(convertToParamMap({ accountId: account }));
  const queryParamMap = new BehaviorSubject(convertToParamMap(
    options.query ?? (options.withQuery ? { order, confirmation } : {}),
  ));
  const scope = signal<{ userId: string; shopId: string } | null>({ userId: 'payer', shopId: 'shop-1' });
  const list = vi.fn(options.list ?? (async (s: CheckoutScope): Promise<RefundRequestPage> => ({
    schemaVersion: 1, accountId: s.account, items: options.items ?? [], nextCursor: null,
  })));
  const create = vi.fn(options.create ?? (async (s: CheckoutScope, command: CreateRefundRequest): Promise<RefundRequestItem> => ({
    ...makeRow('Requested', 99, s.account),
    orderId: command.orderId,
    confirmationId: command.confirmationId,
    amountPaise: command.amountPaise,
    reason: command.reason,
  })));
  const confirmations = {
    list: vi.fn(options.listConfirmations ?? (async (s: CheckoutScope, ord: string) => [
      makeConfirmation(confirmation, s.account, ord, 12000),
    ])),
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
    fixture, page: fixture.nativeElement as HTMLElement,
    scope, paramMap, queryParamMap, list, create, confirmations, settle,
  };
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

describe('merchant refund requests page', () => {
  it('validates legacy and settled rows together and rejects contradictory access or amounts', () => {
    const legacy = makeRow('Requested', 1);
    const settled = { ...makeRow('Confirmed', 2), approvedAmountPaise: 2000, providerState: 'Confirmed', accessDisposition: 'NeedsReview' };
    const response = { schemaVersion: 1, accountId: account, items: [legacy, settled], nextCursor: null };
    expect(validateRefundPage(response, account).items).toEqual([legacy, settled]);
    for (const change of [{ approvedAmountPaise: 4501 }, { accessDisposition: 'AwaitingConfirmation' }, { providerState: 'Failed' }]) {
      expect(() => validateRefundPage({ ...response, items: [legacy, { ...settled, ...change }] }, account)).toThrow();
    }
  });

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

  it('resets unsubmitted amount and reason across account and same-account receipt changes', async () => {
    const { fixture, page, paramMap, queryParamMap, settle } = setup({
      withQuery: true,
      listConfirmations: async (s, ord) => {
        if (s.account === account) return [makeConfirmation(confirmation, account, order, 12000)];
        return [
          makeConfirmation(otherConfirmation, otherAccount, otherOrder, 25000),
          makeConfirmation(thirdConfirmation, otherAccount, otherOrder, 17550),
        ];
      },
    });
    await settle();

    typeValue(page.querySelector('[data-testid="refund-request-amount"]') as HTMLInputElement, '45.00');
    typeValue(page.querySelector('[data-testid="refund-request-reason"]') as HTMLTextAreaElement, 'Account A private draft');
    fixture.detectChanges();

    paramMap.next(convertToParamMap({ accountId: otherAccount }));
    queryParamMap.next(convertToParamMap({ order: otherOrder, confirmation: otherConfirmation }));
    await settle();

    expect((page.querySelector('[data-testid="refund-request-reason"]') as HTMLTextAreaElement).value).toBe('');
    expect((page.querySelector('[data-testid="refund-request-amount"]') as HTMLInputElement).value).toBe('250.00');

    typeValue(page.querySelector('[data-testid="refund-request-amount"]') as HTMLInputElement, '80.00');
    typeValue(page.querySelector('[data-testid="refund-request-reason"]') as HTMLTextAreaElement, 'Receipt 2 unsubmitted');
    fixture.detectChanges();

    queryParamMap.next(convertToParamMap({ order: otherOrder, confirmation: thirdConfirmation }));
    await settle();

    expect((page.querySelector('[data-testid="refund-request-reason"]') as HTMLTextAreaElement).value).toBe('');
    expect((page.querySelector('[data-testid="refund-request-amount"]') as HTMLInputElement).value).toBe('175.50');
  });

  it('renders refund-request-empty only on a successful empty list, never on initial error or scope loss', async () => {
    const empty = setup({ items: [] });
    await empty.settle();
    expect(empty.page.querySelector('[data-testid="refund-request-empty"]')).not.toBeNull();
    TestBed.resetTestingModule();

    const rejected = setup({ list: async () => { throw new HttpErrorResponse({ status: 503 }); } });
    await rejected.settle();
    expect(rejected.page.querySelector('[data-testid="refund-request-error"]')).not.toBeNull();
    expect(rejected.page.querySelector('[data-testid="refund-request-empty"]')).toBeNull();
    TestBed.resetTestingModule();

    let resolveList!: (p: RefundRequestPage) => void;
    const scopeLoss = setup({ list: () => new Promise<RefundRequestPage>(r => { resolveList = r; }) });
    scopeLoss.scope.set(null);
    await scopeLoss.settle();
    resolveList({ schemaVersion: 1, accountId: account, items: [], nextCursor: null });
    await scopeLoss.settle();
    expect(scopeLoss.page.querySelector('[data-testid="refund-request-empty"]')).toBeNull();
  });

  it('resolves a receipt on page eleven, reports legitimately absent receipts, and cancels stale paging', async () => {
    const page11Id = '44444444-4444-4444-8444-000000000999';
    const pages = Array.from({ length: 10 }, (_, pageIndex) =>
      Array.from({ length: CONFIRMATION_PAGE_SIZE }, (__, rowIndex) =>
        makeConfirmation(`44444444-4444-4444-8444-${String(pageIndex * 100 + rowIndex + 1).padStart(12, '0')}`,
          account, order, 12000)));
    pages.push([makeConfirmation(page11Id, account, order, 34567)]);

    let pageNumber = 0;
    const found = setup({
      query: { order, confirmation: page11Id },
      listConfirmations: async () => pages[pageNumber++] ?? [],
    });
    await found.settle();

    expect(found.confirmations.list).toHaveBeenCalledTimes(11);
    expect((found.page.querySelector('[data-testid="refund-request-amount"]') as HTMLInputElement).value).toBe('345.67');
    TestBed.resetTestingModule();

    let absentCalls = 0;
    const absent = setup({
      query: { order, confirmation: thirdConfirmation },
      listConfirmations: async () => (absentCalls++ === 0 ? pages[0] : [makeConfirmation(confirmation, account, order, 12000)]),
    });
    await absent.settle();
    expect(absent.page.querySelector('[data-testid="refund-request-form"]')).toBeNull();
    expect(absent.page.querySelector('[data-testid="refund-request-error"]')?.textContent).toContain('not found');
    TestBed.resetTestingModule();

    let resolvePage!: (rows: readonly PaymentConfirmation[]) => void;
    let navCalls = 0;
    const stale = setup({
      withQuery: true,
      listConfirmations: () => {
        navCalls++;
        if (navCalls === 1) return new Promise<readonly PaymentConfirmation[]>(r => { resolvePage = r; });
        return Promise.resolve([makeConfirmation(otherConfirmation, account, order, 22200)]);
      },
    });
    stale.queryParamMap.next(convertToParamMap({ order, confirmation: otherConfirmation }));
    await stale.settle();
    resolvePage([makeConfirmation(confirmation, account, order, 12000)]);
    await stale.settle();
    expect((stale.page.querySelector('[data-testid="refund-request-amount"]') as HTMLInputElement).value).toBe('222.00');
  });

  it('blocks embedded C1 controls in form validation while allowing normal multilingual reasons', async () => {
    expect(normalizeRefundReason('bad\u0085reason')).toBeNull();
    expect(normalizeRefundReason('bad\u0080reason')).toBeNull();
    expect(normalizeRefundReason('bad\u009freason')).toBeNull();

    const { fixture, page, create, settle } = setup({ withQuery: true });
    await settle();

    const reasonEl = page.querySelector('[data-testid="refund-request-reason"]') as HTMLTextAreaElement;
    const submitBtn = page.querySelector('[data-testid="refund-request-submit"]') as HTMLButtonElement;
    typeValue(reasonEl, 'Refund with\u0085C1 control');
    fixture.detectChanges();
    expect(submitBtn.disabled).toBe(true);
    (page.querySelector('[data-testid="refund-request-form"]') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await settle();
    expect(create).not.toHaveBeenCalled();

    const hindiReason = 'दुकान सेटअप डुप्लिकेट — कृपया समीक्षा करें';
    typeValue(reasonEl, hindiReason);
    fixture.detectChanges();
    expect(submitBtn.disabled).toBe(false);
    submitBtn.click();
    await settle();
    expect(create).toHaveBeenCalledTimes(1);
    expect(create.mock.calls[0][1].reason).toBe(hindiReason);
  });

  it('drops an in-flight list response when the active scope changes', async () => {
    let resolveFirst!: (page: RefundRequestPage) => void;
    let calls = 0;
    const { page, scope, paramMap, settle } = setup({
      list: () => {
        calls++;
        if (calls === 1) return new Promise<RefundRequestPage>(resolve => { resolveFirst = resolve; });
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

  it('renders separate requested and confirmed amounts and outcome headlines for reserved, manual, historical and older DTOs', async () => {
    const rows: RefundRequestItem[] = [
      { ...makeRow('Confirmed', 0), amountPaise: 6000, approvedAmountPaise: 4500, providerState: 'Confirmed', accessDisposition: 'Applied' },
      { ...makeRow('Pending', 1), amountPaise: 6000, approvedAmountPaise: 4500, providerState: 'Reserved', accessDisposition: 'AwaitingConfirmation' },
      { ...makeRow('Pending', 2), amountPaise: 6000, approvedAmountPaise: 4500, providerState: 'Pending', accessDisposition: 'AwaitingConfirmation' },
      { ...makeRow('Pending', 3), amountPaise: 6000, approvedAmountPaise: 4500, providerState: 'Manual', accessDisposition: 'Applied' },
      { ...makeRow('Pending', 4), amountPaise: 6000, approvedAmountPaise: 4500, providerState: 'Historical', accessDisposition: 'Applied' },
      { ...makeRow('Pending', 5), amountPaise: 6000 }, // Older DTO without outcome fields
    ];
    const { page, settle } = setup({ items: rows });
    await settle();

    const rendered = Array.from(page.querySelectorAll<HTMLElement>('[data-testid="refund-request-row"]'));
    expect(rendered).toHaveLength(6);

    const statusOf = (row: HTMLElement) => row.querySelector('[data-testid="refund-request-status"]')?.textContent?.trim();
    const detailOf = (row: HTMLElement) => row.querySelector('[data-testid="refund-request-status-detail"]')?.textContent?.trim();

    expect(statusOf(rendered[0])).toBe('Refund confirmed');
    expect(rendered[0].querySelector('[data-testid="refund-request-row-amount"]')?.textContent).toContain('60.00');
    expect(rendered[0].querySelector('[data-testid="refund-request-row-approved-amount"]')?.textContent).toContain('45.00');
    expect(rendered[0].textContent).toContain('Requested amount');
    expect(rendered[0].textContent).toContain('Confirmed amount');

    expect(statusOf(rendered[1])).toBe('Refund reserved');
    expect(detailOf(rendered[1])).toBe('Amount reserved for refund.');

    expect(statusOf(rendered[2])).toBe('Pending provider');
    expect(detailOf(rendered[2])).toContain('original payment provider');

    expect(statusOf(rendered[3])).toBe('Manual follow-up');
    expect(detailOf(rendered[3])).toBe('Finance is reviewing the manual refund.');

    expect(statusOf(rendered[4])).toBe('Historical approval');
    expect(detailOf(rendered[4])).toBe('Finance is verifying the historical refund.');

    expect(statusOf(rendered[5])).toBe('Pending provider');
    expect(detailOf(rendered[5])).toContain('original payment provider');
    expect(rendered[5].querySelector('[data-testid="refund-request-row-approved-amount"]')).toBeNull();
  });
});
