import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { afterEach, expect, it, vi } from 'vitest';
import { AdminSessionService } from '../auth/admin-session.service';
import { ManualPaymentApi } from './manual-payment-api.service';
import { ManualPaymentDrafts } from './manual-payment-drafts';
import { ManualPaymentPageComponent } from './manual-payment-page.component';

const account = '11111111-1111-4111-8111-111111111111';
const order = '22222222-2222-4222-8222-222222222222';
afterEach(() => TestBed.resetTestingModule());

function setup(routing: () => Promise<unknown>) {
  const params = new BehaviorSubject(convertToParamMap({ account, order }));
  const capabilities = signal(['billing']);
  const read = vi.fn(async () => ({ order: { shops: [], amountPaise: 12000 },
    status: { attemptId: 'original', payment: 'PendingVerification', access: 'NotApplicable' }, receipts: [] }));
  TestBed.configureTestingModule({ providers: [provideRouter([]),
    { provide: ActivatedRoute, useValue: { queryParamMap: params } },
    { provide: AdminSessionService, useValue: { adminId: signal('admin'), sessionId: signal('session'), capabilities } },
    { provide: ManualPaymentApi, useValue: { read, routing } },
    { provide: ManualPaymentDrafts, useValue: { read: async () => ({ revision: 0 }) } },
  ] });
  const fixture = TestBed.createComponent(ManualPaymentPageComponent);
  return { fixture, capabilities, params, read, element: () => {
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  } };
}

it('clears operator routing on authority loss and ignores the late original response', async () => {
  let finish!: (value: unknown) => void;
  const routing = vi.fn(() => new Promise(resolve => { finish = resolve; }));
  const page = setup(routing);
  await vi.waitFor(() => { page.element(); expect(routing).toHaveBeenCalledOnce(); });
  page.capabilities.set([]);
  page.element();
  finish({ schemaVersion: 1, accountId: account, orderId: order, attemptId: 'original',
    original: { providerAccountId: 'private-old-account' } });
  await vi.waitFor(() => {
    expect(page.element().querySelector('[data-testid="payment-routing"]')).toBeNull();
    expect(page.element().textContent).not.toContain('private-old-account');
  });
});

it('shows no guessed routing identity when the server denies its read', async () => {
  const page = setup(async () => { throw new Error('Denied'); });
  await vi.waitFor(() => {
    expect(page.element().querySelector('[role="alert"]')).not.toBeNull();
    expect(page.element().querySelector('[data-testid="payment-routing"]')).toBeNull();
  });
});

it('ignores an old account response after switching account and returning', async () => {
  let finish!: (value: unknown) => void;
  const routing = vi.fn(() => new Promise(resolve => { finish = resolve; }));
  const page = setup(routing);
  await vi.waitFor(() => { page.element(); expect(routing).toHaveBeenCalledOnce(); });
  const originalFinish = finish;
  page.params.next(convertToParamMap({ account: '33333333-3333-4333-8333-333333333333', order }));
  page.element();
  page.params.next(convertToParamMap({ account, order }));
  page.element();
  originalFinish({ schemaVersion: 1, accountId: account, orderId: order, attemptId: 'original', original: { providerAccountId: 'stale' } });
  await Promise.resolve();
  expect(page.element().querySelector('[data-testid="payment-routing"]')).toBeNull();
});
