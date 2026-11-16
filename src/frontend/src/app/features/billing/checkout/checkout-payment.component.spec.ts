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

let http: HttpTestingController;

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
  TestBed.configureTestingModule({
    imports: [TranslocoTestingModule.forRoot({
      langs: { 'en-IN': en }, translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' }, preloadLangs: true,
    })],
    providers: [
      provideHttpClient(), provideHttpClientTesting(),
      { provide: ShopContextService, useValue: { scope: signal(owner) } },
      { provide: LocalizationService, useValue: { currentLanguage: signal('en-IN') } },
    ],
  });
  http = TestBed.inject(HttpTestingController);
});

afterEach(() => {
  try {
    http.verify();
  } finally {
    TestBed.resetTestingModule();
    vi.useRealTimers();
  }
});

async function render(first: CheckoutPayment) {
  const fixture = TestBed.createComponent(CheckoutPaymentComponent);
  fixture.componentRef.setInput('quote', quote);
  fixture.detectChanges();
  const root = fixture.nativeElement as HTMLElement;
  const page = {
    text: (testId: string) => root.querySelector(`[data-testid="${testId}"]`)?.textContent?.trim() ?? null,
    click: (testId: string) => root.querySelector<HTMLButtonElement>(`[data-testid="${testId}"]`)!.click(),
    update: async () => { await new Promise(done => setTimeout(done, 0)); await fixture.whenStable(); fixture.detectChanges(); },
  };
  http.expectOne({ method: 'GET', url: path }).flush(first);
  await page.update();
  return page;
}

describe('CheckoutPaymentComponent', () => {
  it('shows applying access until every covered shop is acknowledged', async () => {
    const page = await render(notStarted);
    expect(page.text('checkout-accepted')).toBe(en.billing.checkout.accepted);
    page.click('payment-start');
    const start = http.expectOne({ method: 'POST', url: path + '/attempts' });
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
    const verify = http.expectOne({ method: 'POST', url: `${path}/attempts/${attempt}/verify` });
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
    http.expectOne({ method: 'POST', url: path + '/attempts' })
      .flush({ code: 'Billing.PaymentAlreadyStarted' }, { status: 409, statusText: 'Conflict' });
    await vi.waitFor(() => http.expectOne({ method: 'GET', url: path }).flush(status()));
    await page.update();
    expect(page.text('payment-awaiting')).toBe(t.awaiting);
    expect(page.text('payment-problem')).toBeNull();
  });
});
