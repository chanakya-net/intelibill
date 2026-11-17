import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import en from '../../../../../public/assets/i18n/en-IN.json';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { CheckoutPaymentComponent } from './checkout-payment.component';
import type { CheckoutPayment } from './checkout-payment.models';
import type { Quote } from './checkout.models';
import { RAZORPAY_OPENER, type RazorpayOutcome } from './razorpay-checkout';
import { CheckoutDrafts } from './checkout-drafts';

const id = (value: number) => '00000000-0000-4000-8000-00000000000' + value;
const owner = { userId: id(1), shopId: id(2) };
const account = id(3);
const order = id(4);
const attempt = id(5);
const path = `${API_BASE_URL}/billing/accounts/${account}/orders/${order}/payment`;
const t = en.billing.checkout.payment;
const quote = {
  accountId: account, orderId: order, revision: 1, snapshotHash: 'a'.repeat(64), shopCount: 3,
  billingTimeZone: 'Asia/Kolkata', acceptance: { operationKey: id(6), snapshotHash: 'a'.repeat(64), acceptedAt: '2026-11-16T12:00:00Z' },
} as unknown as Quote;

const status = (patch: Partial<CheckoutPayment> = {}): CheckoutPayment => ({
  schemaVersion: 1, accountId: account, orderId: order, attemptId: attempt, payment: 'AwaitingPayment',
  fulfilment: 'NotFulfilled', access: 'NotApplicable', amountPaise: 27000, currency: 'INR',
  startedAt: '2026-11-16T12:01:00Z', verifiedAt: null, termId: null, termStartsAt: null, termEndsAt: null,
  shopsTotal: 3, shopsApplied: 0, canStart: false, launch: { flow: 'development-simulator', parameters: {} }, ...patch,
});
const notStarted = status({ attemptId: null, payment: 'NotStarted', startedAt: null, canStart: true, launch: null });
const verified = (access: CheckoutPayment['access'], applied: number) => status({
  payment: 'Verified', fulfilment: 'Fulfilled', access, shopsApplied: applied, launch: null, verifiedAt: '2026-11-16T12:02:00Z',
  termId: id(7), termStartsAt: '2026-11-16T12:02:00Z', termEndsAt: '2026-12-16T12:02:00Z',
});

const contextScope = signal<{ userId: string; shopId: string } | null>(owner);
let http: HttpTestingController;
let razorpayOpener: (launch: unknown) => Promise<RazorpayOutcome> = () => Promise.reject(new Error('unexpected checkout open'));

beforeEach(() => {
  contextScope.set(owner);
  const storage = new Map<string, string>();
  vi.stubGlobal('localStorage', { getItem: (key: string) => storage.get(key) ?? null,
    setItem: (key: string, value: string) => storage.set(key, value) });
  const locks = { request: <T>(_key: string, run: () => T) => Promise.resolve().then(run) };
  Object.defineProperty(navigator, 'locks', { configurable: true, value: locks });
  vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
  TestBed.configureTestingModule({
    imports: [TranslocoTestingModule.forRoot({
      langs: { 'en-IN': en }, translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' }, preloadLangs: true,
    })],
    providers: [
      provideHttpClient(), provideHttpClientTesting(),
      { provide: ShopContextService, useValue: { scope: contextScope } },
      { provide: LocalizationService, useValue: { currentLanguage: signal('en-IN') } },
      { provide: RAZORPAY_OPENER, useValue: (launch: unknown) => razorpayOpener(launch) },
    ],
  });
  http = TestBed.inject(HttpTestingController);
});

afterEach(() => {
  try {
    http.verify();
  } finally {
    TestBed.resetTestingModule();
    Reflect.deleteProperty(navigator, 'locks');
    vi.unstubAllGlobals();
    vi.useRealTimers();
  }
});

async function render(first: CheckoutPayment) {
  const page = mount();
  http.expectOne({ method: 'GET', url: path }).flush(first);
  await page.update();
  return page;
}

/** A component instance whose first status read is left to the caller, as a second tab would issue it. */
function mount() {
  const fixture = TestBed.createComponent(CheckoutPaymentComponent);
  fixture.componentRef.setInput('quote', quote);
  fixture.detectChanges();
  const root = fixture.nativeElement as HTMLElement;
  const page = {
    text: (testId: string) => root.querySelector(`[data-testid="${testId}"]`)?.textContent?.trim() ?? null,
    click: (testId: string) => root.querySelector<HTMLButtonElement>(`[data-testid="${testId}"]`)!.click(),
    update: async () => { await new Promise(done => setTimeout(done, 0)); await fixture.whenStable(); fixture.detectChanges(); },
  };
  return page;
}

describe('CheckoutPaymentComponent', () => {
  it('shows a manual settlement while retaining an uncertain online operation', async () => {
    const page = await render(notStarted);
    page.click('payment-start');
    const delivery = await vi.waitFor(() => http.expectOne({ method: 'POST', url: path + '/attempts' }));
    const command = delivery.request.body;
    delivery.error(new ProgressEvent('timeout'));
    await page.update();

    const proof = verified('Applying', 0);
    page.click('payment-check');
    http.expectOne({ method: 'GET', url: path }).flush({
      ...proof, attemptId: null, startedAt: null, manualPayment: { payment: 'Verified', verifiedAt: proof.verifiedAt },
    });
    const retry = await vi.waitFor(() => http.expectOne({ method: 'POST', url: path + '/attempts' }));
    expect(retry.request.body).toEqual(command);
    retry.flush({}, { status: 409, statusText: 'Conflict' });
    await page.update();
    expect(page.text('payment-manual-confirmed')).toBe(t.manualConfirmed);
    expect(page.text('payment-applying')).toContain('0');
    expect(page.text('payment-pending')).toBe(t.pending);
    expect(page.text('payment-start')).toBeNull();
  });

  it('retains the original operation after timeout and lookup404', async () => {
    const page = await render(notStarted);
    page.click('payment-start');
    const first = await vi.waitFor(() => http.expectOne({ method: 'POST', url: path + '/attempts' }));
    const command = first.request.body;
    first.error(new ProgressEvent('timeout'));
    await page.update();
    expect(page.text('payment-pending')).toBe(t.pending);
    expect(page.text('payment-start')).toBeNull();

    page.click('payment-check');
    http.expectOne({ method: 'GET', url: path }).flush({}, { status: 404, statusText: 'Not Found' });
    await vi.waitFor(() => {
      const retry = http.expectOne({ method: 'POST', url: path + '/attempts' });
      expect(retry.request.body).toEqual(command);
      retry.flush(status({ payment: 'PendingVerification', launch: null }));
    });
    await page.update();
    expect(page.text('payment-pending')).toBe(t.pending);
  });

  it('shows applying access until every covered shop is acknowledged', async () => {
    const page = await render(notStarted);
    expect(page.text('checkout-accepted')).toBe(en.billing.checkout.accepted);
    page.click('payment-start');
    const start = await vi.waitFor(() => http.expectOne({ method: 'POST', url: path + '/attempts' }));
    expect(start.request.body).toEqual({ operationKey: expect.any(String), expectedRevision: 1, snapshotHash: 'a'.repeat(64) });
    start.flush(status());
    await page.update();
    expect(page.text('checkout-accepted')).toBeNull();
    expect(page.text('payment-simulator-notice')).toBe(t.simulator.notice);

    page.click('payment-simulator-capture');
    await page.update();
    http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/simulator/capture` }).flush(status());
    await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }).flush(verified('Applying', 0)));
    await page.update();
    expect(page.text('payment-confirmed')).toBe(t.confirmed);
    expect(page.text('payment-applying')).toBe('Applying access to 0 of 3 shops');
    expect(page.text('payment-simulator')).toBeNull();

    await vi.advanceTimersByTimeAsync(5000);
    http.expectOne({ method: 'GET', url: path }).flush(verified('Applying', 1));
    await page.update();
    expect(page.text('payment-applying')).toBe('Applying access to 1 of 3 shops');
    await vi.advanceTimersByTimeAsync(5000);
    http.expectOne({ method: 'GET', url: path }).flush(verified('Applied', 3));
    await page.update();
    expect(page.text('payment-applied')).toBe('Access applied to all 3 shops');
    await vi.advanceTimersByTimeAsync(5000);
    http.expectNone(path);
  });

  it('keeps a browser success report pending until the server verifies captured funds', async () => {
    const page = await render(status());
    page.click('payment-simulator-return');
    const verify = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }));
    expect(verify.request.body).toEqual({ launcherOutcome: 'Success' });
    verify.flush(status({ payment: 'PendingVerification', launch: null }));
    await page.update();
    expect(page.text('payment-pending')).toBe(t.pending);
    expect(page.text('payment-confirmed')).toBeNull();
    expect(page.text('payment-simulator')).toBeNull();

    page.click('payment-check');
    const check = http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` });
    expect(check.request.body).toEqual({ launcherOutcome: null });
    check.flush(status({ payment: 'PendingVerification', launch: null }));
    await page.update();
    expect(page.text('payment-pending')).toBe(t.pending);
  });

  it('refuses a launcher flow the client does not know', async () => {
    const page = await render(status({ launch: { flow: 'https://checkout.example/script.js', parameters: {} } }));
    expect(page.text('payment-launcher-unavailable')).toBe(t.launcherUnavailable);
    expect(page.text('payment-simulator')).toBeNull();
  });

  it('shows no simulator controls for a started attempt the runtime no longer launches', async () => {
    const page = await render(status({ launch: null }));
    expect(page.text('payment-awaiting')).toBe(t.awaiting);
    expect(page.text('payment-launcher-unavailable')).toBe(t.launcherUnavailable);
    expect(page.text('payment-simulator')).toBeNull();
    expect(page.text('payment-simulator-capture')).toBeNull();
  });

  it('keeps the accepted quote without a launcher when online payment is unavailable', async () => {
    const page = await render({ ...notStarted, canStart: false });
    expect(page.text('checkout-accepted')).toBe(en.billing.checkout.accepted);
    expect(page.text('payment-unavailable')).toBe(t.unavailable);
    expect(page.text('payment-start')).toBeNull();
  });

  it('adopts the attempt another tab already started instead of paying twice', async () => {
    const page = await render(notStarted);
    page.click('payment-start');
    (await vi.waitFor(() => http.expectOne({ method: 'POST', url: path + '/attempts' })))
      .flush({ code: 'Billing.PaymentAlreadyStarted' }, { status: 409, statusText: 'Conflict' });
    await vi.waitFor(() => http.expectOne({ method: 'GET', url: path }).flush(status()));
    await page.update();
    expect(page.text('payment-awaiting')).toBe(t.awaiting);
    expect(page.text('payment-problem')).toBeNull();
  });

  describe('Razorpay launcher', () => {
    const launch = { flow: 'razorpay-standard', parameters: { keyId: 'rzp_test_fixture', orderId: 'order_abc123', amount: '27000', currency: 'INR', methods: 'upi' } };
    const proof = { razorpay_payment_id: 'pay_abc', razorpay_order_id: 'order_abc123', razorpay_signature: 'f'.repeat(64) };

    function open(outcome: RazorpayOutcome) {
      const opener = vi.fn((_launch: unknown) => Promise.resolve(outcome));
      razorpayOpener = opener;
      return opener;
    }

    it('relays a successful callback as an unverified claim and shows pending until the server verifies', async () => {
      const opener = open({ kind: 'success', proof });
      const page = await render(status({ launch }));
      expect(page.text('payment-razorpay-notice')).toBe(t.razorpay.notice);
      page.click('payment-razorpay-pay');
      await page.update();
      expect(opener).toHaveBeenCalledWith({ keyId: 'rzp_test_fixture', orderId: 'order_abc123', amount: 27000, currency: 'INR', methods: ['upi'] });
      const verify = http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` });
      expect(verify.request.body).toEqual({ launcherOutcome: 'Success', launcherProof: proof });
      verify.flush(status({ payment: 'PendingVerification', launch: null }));
      await page.update();
      expect(page.text('payment-pending')).toBe(t.pending);
      expect(page.text('payment-confirmed')).toBeNull();
    });

    it('sends nothing when the checkout window is dismissed and keeps the order payable', async () => {
      open({ kind: 'cancelled' });
      const page = await render(status({ launch }));
      page.click('payment-razorpay-pay');
      await page.update();
      expect(page.text('payment-razorpay-pay')).not.toBeNull();
      expect(page.text('payment-pending')).toBeNull();
    });
    it('drops a callback that lands after the account scope changed', async () => {
      let finish: (outcome: RazorpayOutcome) => void = () => undefined;
      razorpayOpener = () => new Promise<RazorpayOutcome>(resolve => { finish = resolve; });
      const page = await render(status({ launch }));
      page.click('payment-razorpay-pay');
      await page.update();

      contextScope.set({ userId: id(8), shopId: id(9) });
      await page.update();
      http.expectOne({ method: 'GET', url: path }).flush(notStarted);
      finish({ kind: 'success', proof });
      await page.update();
      http.expectNone({ method: 'POST', url: `${path}/attempts/${attempt}/verify` });
    });

    it('holds a callback that arrives while another request runs and verifies the original attempt', async () => {
      let finish: (outcome: RazorpayOutcome) => void = () => undefined;
      razorpayOpener = () => new Promise<RazorpayOutcome>(resolve => { finish = resolve; });
      const page = await render(status({ launch }));
      page.click('payment-razorpay-pay');
      await page.update();
      page.click('payment-razorpay-check');
      const again = http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` });
      finish({ kind: 'success', proof });
      await page.update();
      again.flush(status({ launch }));
      await page.update();
      const relayed = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }));
      expect(relayed.request.body).toEqual({ launcherOutcome: 'Success', launcherProof: proof });
      relayed.flush(status({ payment: 'PendingVerification', launch: null }));
      await page.update();
      expect(page.text('payment-pending')).toBe(t.pending);
    });

    it('rechecks the same attempt without a new checkout when the callback was lost', async () => {
      const page = await render(status({ launch }));
      page.click('payment-razorpay-check');
      const check = http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` });
      expect(check.request.body).toEqual({ launcherOutcome: null });
      check.flush(verified('Applying', 0));
      await page.update();
      expect(page.text('payment-confirmed')).toBe(t.confirmed);
    });

    it('keeps a newer pending claim against an older awaiting read, then reopens only on a fresh recheck', async () => {
      const opener = open({ kind: 'success', proof });
      const drafts = TestBed.inject(CheckoutDrafts);
      const saved = () => drafts.readPayment({ ...owner, account }, order)?.state;
      const tabA = await render(status({ launch }));
      expect(saved()).toBe('awaiting');

      const tabB = mount();
      const held = http.expectOne({ method: 'GET', url: path });
      tabA.click('payment-razorpay-pay');
      const claim = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }));
      claim.flush(status({ payment: 'PendingVerification', launch: null }));
      await tabA.update();
      expect(saved()).toBe('pending');

      held.flush(status({ launch }));
      await tabB.update();
      expect(saved()).toBe('pending');
      expect(tabB.text('payment-pending')).toBe(t.pending);
      expect(tabB.text('payment-razorpay-pay')).toBeNull();

      tabB.click('payment-check');
      const fresh = http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` });
      expect(fresh.request.body).toEqual({ launcherOutcome: null });
      fresh.flush(status({ launch }));
      await tabB.update();
      expect(saved()).toBe('awaiting');
      expect(tabB.text('payment-pending')).toBeNull();
      tabB.click('payment-razorpay-pay');
      await tabB.update();
      expect(opener).toHaveBeenLastCalledWith(expect.objectContaining({ orderId: 'order_abc123' }));
      http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }).flush(status({ payment: 'PendingVerification', launch: null }));
      await tabB.update();
    });

    it('never reopens a pending attempt from a routine status read', async () => {
      const page = await render(status({ payment: 'PendingVerification', launch: null }));
      await vi.advanceTimersByTimeAsync(5000);
      http.expectOne({ method: 'GET', url: path }).flush(status({ launch }));
      await page.update();
      expect(page.text('payment-pending')).toBe(t.pending);
      expect(TestBed.inject(CheckoutDrafts).readPayment({ ...owner, account }, order)?.state).toBe('pending');
    });

    it('offers the launcher again once the server confirms the recovered order is still unpaid', async () => {
      const page = await render(status({ payment: 'PendingVerification', launch: null }));
      page.click('payment-check');
      http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }).flush(status({ launch }));
      await page.update();
      expect(page.text('payment-razorpay-pay')).not.toBeNull();
      expect(page.text('payment-pending')).toBeNull();
    });
  });
});
