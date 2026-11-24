import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, type TestRequest } from '@angular/common/http/testing';
import { signal, type Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, ROUTES, convertToParamMap, provideRouter, type Route } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { expect, vi } from 'vitest';
import { appConfig } from '../../app.config';
import { AdminSessionService } from '../auth/admin-session.service';

export const account = '11111111-aaaa-4111-8111-1111111111bb';
export const otherAccount = '22222222-2222-4222-8222-222222222222';
export const request = '66666666-6666-4666-8666-000000000001';
export const admin = '77777777-7777-4777-8777-777777777777';
const order = '33333333-3333-4333-8333-333333333333';
const confirmation = '44444444-4444-4444-8444-444444444444';
const term = '88888888-8888-4888-8888-888888888888';
const paidThrough = '2028-02-29T04:30:00Z';
const accessEnds = '2028-03-07T04:30:00Z';
export const approvalPath = `/payments/refunds/${account}/${request}/approve`;
export const base = `/api/admin/accounts/${account}/refund-requests`;

export const item = {
  refundRequestId: request, accountId: account, orderId: order, confirmationId: confirmation,
  actorId: '55555555-5555-4555-8555-555555555555', paymentSource: 'Online', status: 'Requested', amountPaise: 6000,
  paymentAmountPaise: 12000, currency: 'INR', reason: 'Duplicate shop setup before go-live',
  requestedAt: '2028-01-31T04:30:00Z', providerId: 'development', providerAccountId: 'dev-account',
  environment: 'Development', configurationVersion: 'sim/1',
} as const;

const access = {
  treatment: 'Preserve', termId: term, effectiveAt: null,
  shops: [
    { shopId: '99999999-9999-4999-8999-000000000001', shopName: 'Refund Central', restricted: false },
    { shopId: '99999999-9999-4999-8999-000000000002', shopName: 'Refund East', restricted: true },
  ],
  previousPaidThrough: paidThrough, previousAccessEndsAt: accessEnds, newPaidThrough: paidThrough, newAccessEndsAt: accessEnds,
};

export const preview = {
  schemaVersion: 1, accountId: account, refundRequestId: request, orderId: order, confirmationId: confirmation,
  status: 'Requested', verifiedAmountPaise: 12000, requestedAmountPaise: 6000, occupiedAmountPaise: 0,
  remainingAmountPaise: 12000, proposedAmountPaise: 4500, proposedRemainingAmountPaise: 7500, currency: 'INR',
  paymentSource: 'Online', providerId: 'development', providerAccountId: 'dev-account', environment: 'Development',
  configurationVersion: 'sim/1', refundPolicyVersion: 'refund-2028', receiptVersion: 0, impactDigest: 'a'.repeat(64), access,
};

export const receipt = (operationKey: string, overrides: Record<string, unknown> = {}) => ({
  schemaVersion: 1, approvalId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', reservationId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
  accessDecisionId: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc', accountId: account, refundRequestId: request, orderId: order,
  confirmationId: confirmation, operationKey, status: 'Pending', approvedAmountPaise: 4500, requestedAmountPaise: 6000,
  verifiedAmountPaise: 12000, remainingBeforePaise: 12000, remainingAfterPaise: 7500, currency: 'INR', receiptVersion: 1,
  adminId: admin, mfaVerifiedAt: '2028-01-31T04:29:00Z', decidedAt: '2028-01-31T04:30:00Z',
  reason: 'Duplicate branch purchased before go-live', access, ...overrides,
});

/** In-memory IndexedDB double; <c>records</c> exposes exactly what would survive a reload. */
export function installStorage() {
  const records = new Map<string, unknown>();
  vi.stubGlobal('indexedDB', {
    open() {
      const database = {
        createObjectStore: () => {}, close: () => {},
        transaction() {
          const tx = { oncomplete: () => {}, onabort: () => {}, error: null,
            objectStore: () => ({
              get(key: string) {
                const read = { result: structuredClone(records.get(key)), onsuccess: () => {} };
                queueMicrotask(() => { read.onsuccess(); tx.oncomplete(); });
                return read;
              },
              put(value: unknown, key: string) { records.set(key, structuredClone(value)); },
            }) };
          return tx;
        },
      };
      const opening = { result: database, onupgradeneeded: () => {}, onsuccess: () => {}, onerror: () => {} };
      queueMicrotask(() => { opening.onupgradeneeded(); opening.onsuccess(); });
      return opening;
    },
  });
  return records;
}

function flatten(routes: readonly Route[]): Route[] {
  return routes.flatMap(route => [route, ...flatten(route.children ?? [])]);
}

/** The shipping route table's lazily loaded approval page, or null when the app does not route to one. */
export async function loadApprovalPage(): Promise<Type<unknown> | null> {
  TestBed.configureTestingModule({ providers: [appConfig.providers] });
  const route = flatten(TestBed.inject(ROUTES).flat()).find(candidate => candidate.path === 'payments/refunds/:account/:request/approve');
  TestBed.resetTestingModule();
  return route?.loadComponent ? await route.loadComponent() as Type<unknown> : null;
}

export async function open(options: { pending?: unknown; status?: string } = {}) {
  const component = await loadApprovalPage();
  expect(component, 'the approval route must exist').not.toBeNull();
  const records = installStorage();
  const key = [admin, account, request].join('.');
  if (options.pending) records.set(key, { revision: 0, pending: options.pending });
  const params = new BehaviorSubject(convertToParamMap({ account, request }));
  const adminId = signal(admin);
  const sessionId = signal('session-1');
  TestBed.configureTestingModule({ providers: [
    provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
    { provide: ActivatedRoute, useValue: { paramMap: params } },
    { provide: AdminSessionService, useValue: { adminId, sessionId, capabilities: signal(['billing']) } },
  ] });
  const fixture = TestBed.createComponent(component!);
  const http = TestBed.inject(HttpTestingController);
  const page = fixture.nativeElement as HTMLElement;
  const update = () => fixture.detectChanges();
  const take = (method: string, suffix: string): Promise<TestRequest> => vi.waitFor(() => {
    update();
    const [found] = http.match(candidate => candidate.method === method && candidate.url.endsWith(suffix));
    expect(found, `${method} ${suffix}`).toBeDefined();
    return found;
  });
  const post = async (suffix: string) => {
    (await take('GET', '/api/admin/auth/sign-in/context')).flush({ requestToken: 'csrf' });
    return take('POST', suffix);
  };
  const find = <T extends HTMLElement>(id: string) => page.querySelector<T>(`[data-testid="${id}"]`);
  const shown = (id: string) => vi.waitFor(() => { update(); expect(find(id), id).not.toBeNull(); return find(id)!; });
  const set = (selector: string, value: string) => {
    const input = page.querySelector<HTMLInputElement | HTMLTextAreaElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    update();
  };
  const choose = (treatment: string) => {
    const radio = page.querySelector<HTMLInputElement>(`input[name="treatment"][value="${treatment}"]`)!;
    radio.checked = true;
    radio.dispatchEvent(new Event('change', { bubbles: true }));
    update();
  };
  update();
  (await take('GET', base)).flush({ schemaVersion: 1, accountId: account, nextCursor: null,
    items: [{ ...item, status: options.status ?? 'Requested' }] });
  return { fixture, http, page, records, key, params, adminId, sessionId, update, take, post, find, shown, set, choose };
}

export type Harness = Awaited<ReturnType<typeof open>>;

/** Fills the form, previews Preserve for ₹45.00 and confirms the review; returns the preview request body. */
export async function reviewed(harness: Harness) {
  await harness.shown('refund-approval-form');
  harness.set('#refund-approval-amount', '45.00');
  harness.set('#refund-approval-reason', '  Duplicate branch purchased before go-live ');
  harness.choose('Preserve');
  harness.find<HTMLButtonElement>('refund-approval-preview')!.click();
  const previewed = await harness.post('/approval-preview');
  previewed.flush(preview);
  await harness.shown('refund-approval-impact');
  const confirm = harness.page.querySelector<HTMLInputElement>('#refund-approval-confirm')!;
  confirm.checked = true;
  confirm.dispatchEvent(new Event('change', { bubbles: true }));
  harness.update();
  return previewed.request.body as Record<string, unknown>;
}

export const submitButton = (harness: Harness) => harness.find<HTMLButtonElement>('refund-approval-submit');
