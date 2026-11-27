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
import { BillingDocumentService } from '../documents/billing-document.service';

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
let razorpayOpener: (launch: unknown, signal?: AbortSignal) => Promise<RazorpayOutcome> = () => Promise.reject(new Error('unexpected checkout open'));

beforeEach(() => {
  contextScope.set(owner);
  const storage = new Map<string, string>();
  vi.stubGlobal('localStorage', { getItem: (key: string) => storage.get(key) ?? null,
    setItem: (key: string, value: string) => storage.set(key, value) });
  // Shared by every page in a test, as one browser's lock manager is: a held name answers `ifAvailable` with null.
  const held = new Map<string, number>();
  const locks = {
    request: (name: string, ...args: unknown[]) => {
      const run = args.pop() as (lock: { name: string } | null) => unknown;
      const ifAvailable = (args[0] as { ifAvailable?: boolean } | undefined)?.ifAvailable;
      return Promise.resolve().then(async () => {
        if (ifAvailable && held.get(name)) return run(null);
        held.set(name, (held.get(name) ?? 0) + 1);
        try {
          return await run({ name });
        } finally {
          held.set(name, held.get(name)! - 1);
        }
      });
    },
  };
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
      { provide: RAZORPAY_OPENER, useValue: (launch: unknown, signal?: AbortSignal) => razorpayOpener(launch, signal) },
      { provide: BillingDocumentService, useValue: { list: vi.fn(async () => []), content: vi.fn() } },
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
    blocksNewQuote: () => fixture.componentInstance.blocksNewQuote(),
    update: async () => { await new Promise(done => setTimeout(done, 0)); await fixture.whenStable(); fixture.detectChanges(); },
    /** Unloads the page, as a reload or a closed tab does: its locks go with it. */
    close: () => fixture.destroy(),
  };
  return page;
}

describe('CheckoutPaymentComponent', () => {
  it('shows unverified routing review without claiming recorded funds or offering a replacement payment', async () => {
    const page = await render(status({ payment: 'PendingVerification', fulfilment: 'NeedsReview', launch: null }));
    expect(page.text('payment-routing-review')).toContain('not yet verified');
    expect(page.text('payment-review')).toBeNull();
    expect(page.text('payment-confirmed')).toBeNull();
    expect(page.text('payment-start')).toBeNull();
    expect(page.blocksNewQuote()).toBe(true);
    page.click('payment-check');
    http.expectOne({ method: 'POST', url: path + '/attempts/' + attempt + '/verify' })
      .flush(status({ payment: 'PendingVerification', fulfilment: 'NeedsReview', launch: null }));
    await page.update();
    expect(page.text('payment-routing-review')).toContain('not yet verified');
  });

  it.each([false, true])('refreshes the original manual review without offering a new payment (restorationHeld=%s)',
    async restorationHeld => {
      const review = status({ attemptId: null, startedAt: null, verifiedAt: '2026-11-16T12:02:00Z',
        payment: 'Verified', fulfilment: 'NeedsReview', launch: null, restorationHeld,
        manualPayment: { payment: 'Verified', verifiedAt: '2026-11-16T12:02:00Z' } });
      const page = await render(review);
      const message = restorationHeld ? 'payment-restoration-hold' : 'payment-review';
      const copy = restorationHeld ? t.restorationHeld : t.reviewPayment;
      expect(page.text(message)).toBe(copy);
      expect(page.blocksNewQuote()).toBe(true);
      expect(page.text('payment-start')).toBeNull();
      page.click('payment-review-check');
      http.expectOne({ method: 'GET', url: path }).flush(review);
      await page.update();
      expect(page.text(message)).toBe(copy);
    });

  it('checks restoration using the original attempt and displays paid dates separately from the adjustment', async () => {
    const page = await render(status({ payment: 'Verified', fulfilment: 'NeedsReview', restorationHeld: true,
      verifiedAt: '2026-11-16T12:02:00Z', launch: null }));
    expect(page.text('payment-confirmed')).toBe(t.confirmed);
    expect(page.text('payment-restoration-hold')).toBe(t.restorationHeld);
    expect(page.text('payment-term')).toBeNull();
    page.click('payment-check-restoration');
    http.expectOne({ method: 'POST', url: path + '/attempts/' + attempt + '/verify' }).flush({
      ...verified('Applying', 0),
      serviceRestoration: { count: 1, grantId: id(8), startsAt: '2026-12-16T12:02:00Z', endsAt: '2026-12-17T12:02:00Z' },
    });
    await page.update();
    expect(page.text('payment-applying')).toContain('0');
    expect(page.text('payment-term')).not.toContain('17 Dec 2026');
    expect(page.text('payment-restoration-adjustment')).toContain('17 Dec 2026');
    expect(page.text('payment-restoration-adjustment')).toContain(id(8));
  });

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
      expect(opener).toHaveBeenCalledWith({ keyId: 'rzp_test_fixture', orderId: 'order_abc123', amount: 27000, currency: 'INR', methods: ['upi'] }, expect.any(AbortSignal));
      const verify = http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` });
      expect(verify.request.body).toEqual({ launcherOutcome: 'Success', launcherProof: proof });
      verify.flush(status({ payment: 'PendingVerification', launch: null }));
      await page.update();
      expect(page.text('payment-pending')).toBe(t.pending);
      expect(page.text('payment-confirmed')).toBeNull();
    });

    it('rechecks the original order after a dismissed window and offers it again only once proven idle', async () => {
      open({ kind: 'cancelled' });
      const page = await render(status({ launch }));
      page.click('payment-razorpay-pay');
      const check = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }));
      await page.update();
      expect(check.request.body).toEqual({ launcherOutcome: null });
      expect(page.text('payment-pending')).toBe(t.pending);
      expect(page.text('payment-razorpay-pay')).toBeNull();
      check.flush(status({ launch }));
      await page.update();
      expect(page.text('payment-razorpay-pay')).not.toBeNull();
      expect(page.text('payment-pending')).toBeNull();
      expect(TestBed.inject(CheckoutDrafts).readPayment({ ...owner, account }, order)?.state).toBe('awaiting');
    });

    it('keeps Pay hidden after a dismissed window while a provider payment is in flight, then verifies the same attempt', async () => {
      open({ kind: 'cancelled' });
      const page = await render(status({ launch }));
      page.click('payment-razorpay-pay');
      const check = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }));
      check.flush(status({ payment: 'PendingVerification', launch: null }));
      await page.update();
      expect(page.text('payment-pending')).toBe(t.pending);
      expect(page.text('payment-razorpay-pay')).toBeNull();

      page.click('payment-check');
      const again = http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` });
      expect(again.request.body).toEqual({ launcherOutcome: null });
      again.flush(verified('Applying', 0));
      await page.update();
      expect(page.text('payment-confirmed')).toBe(t.confirmed);
    });

    it('drops a dismissal that lands after the account scope changed', async () => {
      let finish: (outcome: RazorpayOutcome) => void = () => undefined;
      razorpayOpener = () => new Promise<RazorpayOutcome>(resolve => { finish = resolve; });
      const page = await render(status({ launch }));
      page.click('payment-razorpay-pay');
      await page.update();
      contextScope.set({ userId: id(8), shopId: id(9) });
      await page.update();
      http.expectOne({ method: 'GET', url: path }).flush(notStarted);
      finish({ kind: 'cancelled' });
      await page.update();
      http.expectNone({ method: 'POST', url: `${path}/attempts/${attempt}/verify` });
    });

    it('applies a pending claim another tab saved while its own recheck failed', async () => {
      const drafts = TestBed.inject(CheckoutDrafts);
      const scope = { ...owner, account };
      const tabA = await render(status({ launch }));
      const tabB = await render(status({ launch }));
      expect(tabB.text('payment-razorpay-pay')).not.toBeNull();

      tabB.click('payment-razorpay-check');
      const held = http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` });
      open({ kind: 'success', proof });
      tabA.click('payment-razorpay-pay');
      const claim = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }));
      expect(drafts.readPayment(scope, order)?.state).toBe('pending');
      window.dispatchEvent(new StorageEvent('storage', { key: drafts.paymentKey(scope, order) }));
      claim.flush(status({ payment: 'PendingVerification', launch: null }));
      await tabA.update();

      held.flush({ code: 'Billing.Unavailable' }, { status: 503, statusText: 'Service Unavailable' });
      await tabB.update();
      expect(tabB.text('payment-razorpay-pay')).toBeNull();
      expect(tabB.text('payment-pending')).toBe(t.pending);

      tabB.click('payment-check');
      const again = http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` });
      expect(again.request.body).toEqual({ launcherOutcome: null });
      again.flush(status({ payment: 'PendingVerification', launch: null }));
      await tabB.update();
      expect(tabB.text('payment-razorpay-pay')).toBeNull();
    });
    it.each(['unavailable', 'stale'])(
      'keeps Pay hidden when another tab verified the attempt while its own recheck was held (%s answer)', async answer => {
        const drafts = TestBed.inject(CheckoutDrafts);
        const scope = { ...owner, account };
        const storage = () => window.dispatchEvent(new StorageEvent('storage', { key: drafts.paymentKey(scope, order) }));
        const tabA = await render(status({ launch }));
        const tabB = await render(status({ launch }));
        tabB.click('payment-razorpay-check');
        const held = http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` });

        open({ kind: 'success', proof });
        tabA.click('payment-razorpay-pay');
        const claim = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }));
        expect(drafts.readPayment(scope, order)?.state).toBe('pending');
        storage();
        claim.flush(verified('Applying', 0));
        await tabA.update();
        expect(drafts.readPayment(scope, order)?.state).toBe('verified');
        storage();

        if (answer === 'stale') held.flush(status({ launch }));
        else held.flush({ code: 'Billing.Unavailable' }, { status: 503, statusText: 'Service Unavailable' });
        await tabB.update();
        expect(tabB.text('payment-razorpay-pay')).toBeNull();
        expect(tabB.text('payment-pending')).toBe(t.pending);

        // Tab A rereads after its own storage event; tab B catches up on the terminal draft with its own status read.
        const reads = http.match({ method: 'GET', url: path });
        expect(reads).toHaveLength(2);
        reads.forEach(read => read.flush(verified('Applying', 0)));
        await tabA.update();
        await tabB.update();
        expect(tabB.text('payment-razorpay-pay')).toBeNull();
        expect(tabB.text('payment-confirmed')).toBe(t.confirmed);
        expect(drafts.readPayment(scope, order)?.state).toBe('verified');
      });

    it.each([
      ['unavailable', 'a later bounded refresh'], ['unavailable', 'Check again'],
      ['stale', 'a later bounded refresh'], ['stale', 'Check again'],
    ])('reads once to catch up on another tab\'s result: a failed (%s) catch-up read waits for %s', async (answer, confirm) => {
      const drafts = TestBed.inject(CheckoutDrafts);
      const scope = { ...owner, account };
      const storage = () => window.dispatchEvent(new StorageEvent('storage', { key: drafts.paymentKey(scope, order) }));
      const tabA = await render(status({ launch }));
      const tabB = await render(status({ launch }));
      tabB.click('payment-razorpay-check');
      const held = http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` });

      open({ kind: 'success', proof });
      tabA.click('payment-razorpay-pay');
      const claim = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }));
      storage();
      claim.flush(verified('Applied', 3));
      await tabA.update();
      storage();
      held.flush({ code: 'Billing.Unavailable' }, { status: 503, statusText: 'Service Unavailable' });
      await tabB.update();

      const reads = http.match({ method: 'GET', url: path });
      expect(reads).toHaveLength(2);
      reads[0].flush(verified('Applied', 3));
      if (answer === 'stale') reads[1].flush(status({ launch }));
      else reads[1].flush({ code: 'Billing.Unavailable' }, { status: 503, statusText: 'Service Unavailable' });
      await tabA.update();
      await tabB.update();
      await tabB.update();
      expect(http.match({ method: 'GET', url: path })).toHaveLength(0);
      expect(tabB.text('payment-razorpay-pay')).toBeNull();
      expect(tabB.text('payment-pending')).toBe(t.pending);

      if (confirm === 'Check again') {
        tabB.click('payment-check');
        const again = http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` });
        expect(again.request.body).toEqual({ launcherOutcome: null });
        again.flush(verified('Applied', 3));
      } else {
        await vi.advanceTimersByTimeAsync(5000);
        http.expectOne({ method: 'GET', url: path }).flush(verified('Applied', 3));
      }
      await tabB.update();
      expect(tabB.text('payment-razorpay-pay')).toBeNull();
      expect(tabB.text('payment-confirmed')).toBe(t.confirmed);
    });

    it('keeps a live window\'s hold against another tab\'s idle recheck, then rechecks the original order after a reload', async () => {
      const drafts = TestBed.inject(CheckoutDrafts);
      const saved = () => drafts.readPayment({ ...owner, account }, order)?.state;
      const opener = vi.fn(() => new Promise<RazorpayOutcome>(() => undefined));
      razorpayOpener = opener;
      const tabA = await render(status({ launch }));
      tabA.click('payment-razorpay-pay');
      await vi.waitFor(() => expect(opener).toHaveBeenCalledTimes(1));
      await tabA.update();
      expect(saved()).toBe('pending');

      // Tab B restores the pending draft while A's window is open and the provider order is still idle.
      const tabB = mount();
      http.expectOne({ method: 'GET', url: path }).flush(status({ launch }));
      await tabB.update();
      const idle = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }));
      expect(idle.request.body).toEqual({ launcherOutcome: null });
      idle.flush(status({ launch }));
      await tabB.update();
      expect(saved()).toBe('pending');
      expect(tabB.text('payment-razorpay-pay')).toBeNull();
      expect(tabB.text('payment-pending')).toBe(t.pending);

      // The customer authorizes in A's window, then A reloads before any callback fires.
      tabA.close();
      const reloaded = mount();
      http.expectOne({ method: 'GET', url: path }).flush(status({ launch }));
      await reloaded.update();
      expect(reloaded.text('payment-razorpay-pay')).toBeNull();
      const recheck = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }));
      expect(recheck.request.body).toEqual({ launcherOutcome: null });
      recheck.flush(status({ payment: 'PendingVerification', launch: null }));
      await reloaded.update();
      await tabB.update();
      for (const page of [reloaded, tabB]) {
        expect(page.text('payment-razorpay-pay')).toBeNull();
        expect(page.text('payment-pending')).toBe(t.pending);
      }

      reloaded.click('payment-check');
      http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }).flush(verified('Applied', 3));
      await reloaded.update();
      expect(reloaded.text('payment-confirmed')).toBe(t.confirmed);
      expect(opener).toHaveBeenCalledTimes(1);
    });

    it('releases the hosted window hold when a pending check unmounts the launcher', async () => {
      const drafts = TestBed.inject(CheckoutDrafts);
      const scope = { ...owner, account };
      const aborted = vi.fn();
      const opener = vi.fn((_launch: unknown, signal?: AbortSignal) => new Promise<RazorpayOutcome>(resolve => {
        signal?.addEventListener('abort', () => { aborted(); resolve({ kind: 'cancelled' }); });
      }));
      razorpayOpener = opener;
      const page = await render(status({ launch }));
      page.click('payment-razorpay-pay');
      await vi.waitFor(() => expect(opener).toHaveBeenCalledTimes(1));
      await page.update();

      page.click('payment-razorpay-check');
      http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` })
        .flush(status({ payment: 'PendingVerification', launch: null }));
      await page.update();
      expect(aborted).toHaveBeenCalledTimes(1);
      expect(page.text('payment-pending')).toBe(t.pending);
      expect(page.text('payment-razorpay-pay')).toBeNull();
      expect(drafts.readPayment(scope, order)?.state).toBe('pending');

      page.click('payment-check');
      const fresh = http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` });
      expect(fresh.request.body).toEqual({ launcherOutcome: null });
      fresh.flush(status({ launch }));
      await page.update();

      const release = await drafts.holdWindow(scope, order);
      const observed = { lockAvailable: release !== null, draftState: drafts.readPayment(scope, order)?.state,
        payVisible: page.text('payment-razorpay-pay') !== null };
      release?.();
      expect(observed).toEqual({ lockAvailable: true, draftState: 'awaiting', payVisible: true });
    });

    it('reopens the order once the window that held it was dismissed', async () => {
      let finish: (outcome: RazorpayOutcome) => void = () => undefined;
      razorpayOpener = () => new Promise<RazorpayOutcome>(resolve => { finish = resolve; });
      const tabA = await render(status({ launch }));
      tabA.click('payment-razorpay-pay');
      await tabA.update();
      const tabB = mount();
      http.expectOne({ method: 'GET', url: path }).flush(status({ launch }));
      await tabB.update();
      (await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }))).flush(status({ launch }));
      await tabB.update();
      expect(tabB.text('payment-razorpay-pay')).toBeNull();

      finish({ kind: 'cancelled' });
      const dismissed = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }));
      expect(dismissed.request.body).toEqual({ launcherOutcome: null });
      dismissed.flush(status({ launch }));
      await tabA.update();
      expect(tabA.text('payment-razorpay-pay')).not.toBeNull();
      expect(TestBed.inject(CheckoutDrafts).readPayment({ ...owner, account }, order)?.state).toBe('awaiting');
    });

    it('saves the opened checkout before the window opens, so a reload that lost the callback rechecks first', async () => {
      const drafts = TestBed.inject(CheckoutDrafts);
      const saved = () => drafts.readPayment({ ...owner, account }, order)?.state;
      const statesAtOpen: (string | undefined)[] = [];
      razorpayOpener = () => { statesAtOpen.push(saved()); return new Promise<RazorpayOutcome>(() => undefined); };
      const page = await render(status({ launch }));
      page.click('payment-razorpay-pay');
      await vi.waitFor(() => expect(statesAtOpen).toEqual(['pending']));
      await page.update();
      expect(page.text('payment-razorpay-pay')).not.toBeNull();

      const reloaded = mount();
      http.expectOne({ method: 'GET', url: path }).flush(status({ launch }));
      await reloaded.update();
      expect(reloaded.text('payment-razorpay-pay')).toBeNull();
      expect(reloaded.text('payment-pending')).toBe(t.pending);
      const recheck = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }));
      expect(recheck.request.body).toEqual({ launcherOutcome: null });
      recheck.flush(status({ payment: 'PendingVerification', launch: null }));
      await reloaded.update();
      expect(reloaded.text('payment-razorpay-pay')).toBeNull();

      reloaded.click('payment-check');
      http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }).flush(verified('Applying', 0));
      await reloaded.update();
      expect(reloaded.text('payment-confirmed')).toBe(t.confirmed);
    });

    it('relays a success callback without valid proof as an unverified claim, never as a dismissal', async () => {
      open({ kind: 'unverified' });
      const page = await render(status({ launch }));
      page.click('payment-razorpay-pay');
      const claim = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }));
      expect(claim.request.body).toEqual({ launcherOutcome: 'Success' });
      claim.flush(status({ payment: 'PendingVerification', launch: null }));
      await page.update();
      expect(page.text('payment-pending')).toBe(t.pending);
      expect(page.text('payment-razorpay-pay')).toBeNull();

      page.click('payment-check');
      const again = http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` });
      expect(again.request.body).toEqual({ launcherOutcome: null });
      again.flush(verified('Applying', 0));
      await page.update();
      expect(page.text('payment-confirmed')).toBe(t.confirmed);
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
      expect(opener).toHaveBeenLastCalledWith(expect.objectContaining({ orderId: 'order_abc123' }), expect.any(AbortSignal));
      http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }).flush(status({ payment: 'PendingVerification', launch: null }));
      await tabB.update();
    });

    it('keeps a newer callback on an already-pending draft against a recheck held across tabs', async () => {
      let complete: (outcome: RazorpayOutcome) => void = () => undefined;
      razorpayOpener = () => new Promise<RazorpayOutcome>(done => { complete = done; });
      const drafts = TestBed.inject(CheckoutDrafts);
      const saved = () => drafts.readPayment({ ...owner, account }, order);
      const tabB = await render(status({ launch }));
      tabB.click('payment-razorpay-pay');
      await tabB.update();

      const tabA = mount();
      http.expectOne({ method: 'GET', url: path }).flush(status({ payment: 'PendingVerification', launch: null }));
      await tabA.update();
      expect(saved()?.state).toBe('pending');
      const before = saved()?.revision;
      tabA.click('payment-check');
      const held = http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` });
      expect(held.request.body).toEqual({ launcherOutcome: null });

      complete({ kind: 'success', proof });
      const claim = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }));
      expect(claim.request.body).toEqual({ launcherOutcome: 'Success', launcherProof: proof });
      expect(saved()?.revision).toBeGreaterThan(before!);
      claim.flush(status({ payment: 'PendingVerification', launch: null }));
      await tabB.update();

      held.flush(status({ launch }));
      await tabA.update();
      expect(saved()?.state).toBe('pending');
      expect(tabA.text('payment-pending')).toBe(t.pending);
      expect(tabA.text('payment-razorpay-pay')).toBeNull();

      tabA.click('payment-check');
      http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }).flush(status({ launch }));
      await tabA.update();
      expect(saved()?.state).toBe('awaiting');
      expect(tabA.text('payment-razorpay-pay')).not.toBeNull();
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

    it.each([
      ['idle', status({ launch }), true],
      ['ambiguous or authorized', status({ payment: 'PendingVerification', launch: null }), false],
    ])('releases the hold when the checkout fails to open, then rechecks the original order (%s)', async (_case, answer, payable) => {
      const drafts = TestBed.inject(CheckoutDrafts);
      const scope = { ...owner, account };
      let fail: (error: Error) => void = () => undefined;
      const opener = vi.fn(() => new Promise<RazorpayOutcome>((_, reject) => { fail = reject; }));
      razorpayOpener = opener;
      const tabA = await render(status({ launch }));
      tabA.click('payment-razorpay-pay');
      await vi.waitFor(() => expect(opener).toHaveBeenCalledTimes(1));

      // Tab B restores the pending draft while A still holds the window: its idle recheck cannot reopen the order.
      const tabB = mount();
      http.expectOne({ method: 'GET', url: path }).flush(status({ launch }));
      await tabB.update();
      (await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }))).flush(status({ launch }));
      await tabB.update();
      expect(tabB.text('payment-razorpay-pay')).toBeNull();

      // The hosted script never opens. Tab A stays open, lets go of the hold and checks the same order proof-free.
      fail(new Error('checkout-unavailable'));
      const recheck = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }));
      expect(recheck.request.body).toEqual({ launcherOutcome: null });
      const release = await drafts.holdWindow(scope, order);
      expect(release).not.toBeNull();
      release?.();
      recheck.flush(answer);
      await tabA.update();
      expect(tabA.text('payment-razorpay-pay') !== null).toBe(payable);

      tabB.click('payment-check');
      const idle = http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` });
      expect(idle.request.body).toEqual({ launcherOutcome: null });
      idle.flush(answer);
      await tabB.update();
      expect(tabB.text('payment-razorpay-pay') !== null).toBe(payable);
      expect(tabB.text('payment-pending') !== null).toBe(!payable);
      expect(drafts.readPayment(scope, order)?.state).toBe(payable ? 'awaiting' : 'pending');
      expect(opener).toHaveBeenCalledTimes(1);
    });

    it('never lets a stale scope\'s failed launch release a newer window hold', async () => {
      const drafts = TestBed.inject(CheckoutDrafts);
      let fail: (error: Error) => void = () => undefined;
      razorpayOpener = () => new Promise<RazorpayOutcome>((_, reject) => { fail = reject; });
      const page = await render(status({ launch }));
      page.click('payment-razorpay-pay');
      await page.update();

      contextScope.set({ userId: id(8), shopId: id(9) });
      await page.update();
      http.expectOne({ method: 'GET', url: path }).flush(notStarted);
      contextScope.set(owner);
      await page.update();
      http.expectOne({ method: 'GET', url: path }).flush(status({ launch }));
      (await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }))).flush(status({ launch }));
      await page.update();

      const newer = vi.fn(() => new Promise<RazorpayOutcome>(() => undefined));
      razorpayOpener = newer;
      page.click('payment-razorpay-pay');
      await vi.waitFor(() => expect(newer).toHaveBeenCalledTimes(1));
      fail(new Error('checkout-unavailable'));
      await page.update();
      await page.update();
      http.expectNone({ method: 'POST', url: `${path}/attempts/${attempt}/verify` });
      expect(await drafts.holdWindow({ ...owner, account }, order)).toBeNull();
      expect(drafts.readPayment({ ...owner, account }, order)?.state).toBe('pending');
    });

    it('checks the original order proof-free after a hosted payment failure and keeps Pay hidden while funding is uncertain', async () => {
      const opener = open({ kind: 'failed' });
      const page = await render(status({ launch }));
      page.click('payment-razorpay-pay');
      const check = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }));
      expect(check.request.body).toEqual({ launcherOutcome: null });
      check.flush(status({ payment: 'PendingVerification', launch: null }));
      await page.update();
      expect(page.text('payment-pending')).toBe(t.pending);
      expect(page.text('payment-razorpay-pay')).toBeNull();

      page.click('payment-check');
      http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` }).flush(verified('Applying', 0));
      await page.update();
      expect(page.text('payment-confirmed')).toBe(t.confirmed);
      expect(opener).toHaveBeenCalledTimes(1);
    });
  });
});

describe('payment confirmation availability', () => {
  it('offers confirmations and accounting documents separately only after verified receipt facts exist', async () => {
    const before = await render(notStarted);
    expect(before.text('confirmation-list')).toBeNull();
    expect(before.text('billing-documents-list')).toBeNull();
    before.close();
    const after = await render(verified('Applying', 0));
    expect(after.text('confirmation-list')).toBe('View payment confirmations');
    expect(after.text('billing-documents-heading')).toBe(en.billing.documents.heading);
    expect(after.text('billing-documents-list')).toBe(en.billing.documents.view);
    after.close();
  });
});
