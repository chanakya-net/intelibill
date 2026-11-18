import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { BehaviorSubject } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import en from '../../../../../public/assets/i18n/en-IN.json';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { stubMemoryStorage } from '../billing.test-utils';
import { CheckoutDrafts } from './checkout-drafts';
import { CheckoutPageComponent } from './checkout-page.component';
import { shopSetHash, type AcceptQuote, type Quote } from './checkout.models';
import { CheckoutService } from './checkout.service';

const id = (value: number) => '00000000-0000-4000-8000-00000000000' + value;
const owner = { userId: id(1), shopId: id(2) };
const scope = { ...owner, account: id(3) };
const order = id(4);
const ordersPath = (account: string) => `${API_BASE_URL}/billing/accounts/${account}/orders`;
const pathFor = (account: string, orderId: string) => `${ordersPath(account)}/${orderId}`;
const path = pathFor(scope.account, order);
const shops = [
  { shopId: id(5), name: 'Market Road', termId: null },
  { shopId: id(6), name: 'Station Road', termId: null },
];
const reviewTime = '2026-11-16T12:00:00Z';
const shopPage = (items: typeof shops, nextCursor: string | null = null) => ({
  accountId: scope.account, orderId: order, items, nextCursor,
});
const acceptPath = path + '/accept';
const notStarted = () => ({
  schemaVersion: 1, accountId: scope.account, orderId: order, attemptId: null, payment: 'NotStarted',
  fulfilment: 'NotFulfilled', access: 'NotApplicable', amountPaise: 27005, currency: 'INR', startedAt: null,
  verifiedAt: null, termId: null, termStartsAt: null, termEndsAt: null, shopsTotal: 2, shopsApplied: 0,
  canStart: false, launch: null,
});

let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
let query: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
let quote: Quote;
let http: HttpTestingController;

beforeEach(async () => {
  stubMemoryStorage();
  vi.stubGlobal('navigator', { locks: { request: async (_: string, run: () => unknown) => run() } });
  vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] });
  vi.setSystemTime(new Date(reviewTime));
  params = new BehaviorSubject(convertToParamMap({ accountId: scope.account }));
  query = new BehaviorSubject(convertToParamMap({ order }));
  quote = {
    accountId: scope.account, orderId: order, revision: 1, schemaVersion: 1,
    planVersionId: id(7), planName: 'Multi-shop', planVersion: 2, period: 'Month',
    termId: null, amountPaise: 27005, currency: 'INR', priceBasis: 'TaxInclusiveFinal',
    capacity: 3, membersPerShop: 8, shopCount: shops.length, shopHash: await shopSetHash(shops.map(shop => shop.shopId)),
    snapshotHash: 'a'.repeat(64), customerTermsVersion: 'terms/3', refundPolicyVersion: 'refund/2',
    billingTimeZone: 'Asia/Kolkata', previewStartsAt: reviewTime, previewEndsAt: '2026-12-16T12:00:00Z',
    expiresAt: '2026-11-16T12:15:00Z', startRule: 'Fulfilment', graceDaysIncluded: 0, developmentFixture: false,
    acceptance: null,
  };
  TestBed.configureTestingModule({
    imports: [TranslocoTestingModule.forRoot({
      langs: { 'en-IN': en }, translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' }, preloadLangs: true,
    })],
    providers: [
      provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
      { provide: ActivatedRoute, useValue: { paramMap: params, queryParamMap: query } },
      { provide: ShopContextService, useValue: { scope: signal(owner) } },
      { provide: LocalizationService, useValue: { currentLanguage: signal('en-IN') } },
    ],
  });
  http = TestBed.inject(HttpTestingController);
});

afterEach(() => {
  try {
    http?.verify();
  } finally {
    TestBed.resetTestingModule();
    vi.useRealTimers();
    vi.unstubAllGlobals();
  }
});

function render() {
  const fixture = TestBed.createComponent(CheckoutPageComponent);
  fixture.detectChanges();
  const root = fixture.nativeElement as HTMLElement;
  return {
    fixture,
    element: (id: string) => root.querySelector<HTMLElement>(`[data-testid="checkout-${id}"]`),
    covered: () => Array.from(root.querySelectorAll('[data-testid="checkout-covered-shop"] span'), item => item.textContent),
    consent: () => root.querySelector<HTMLInputElement>('input[type="checkbox"]')!,
    button: (label: string) => Array.from(root.querySelectorAll('button')).find(button => button.textContent?.trim() === label),
    alert: () => root.querySelector('[role="alert"]')?.textContent,
    update: async () => { await fixture.whenStable(); fixture.detectChanges(); },
  };
}

async function load(page: ReturnType<typeof render>) {
  http.expectOne(path).flush(quote);
  await page.update();
}

async function showCoverage(page: ReturnType<typeof render>) {
  http.expectOne(path + '/shops').flush(shopPage(shops));
  await vi.waitFor(async () => {
    await page.update();
    expect(page.covered()).toEqual(shops.map(shop => shop.name));
  });
}

async function submit(page: ReturnType<typeof render>) {
  page.consent().click();
  await page.update();
  page.button(en.billing.checkout.accept)!.click();
  await page.update();
  return http.expectOne({ method: 'POST', url: acceptPath });
}

describe('CheckoutPageComponent', () => {
  it('discloses forfeited trial time before the payment is accepted', async () => {
    quote = { ...quote, freshStart: { policy: 'paid-fresh-start/1', trialShopCount: 1, trialSourceHash: 'a'.repeat(64) } };
    const page = render();
    await load(page);
    await showCoverage(page);
    expect(page.element('trial-forfeiture')?.textContent).toContain(en.billing.checkout.trialForfeiture);
    expect(page.element('restricted-gap')).toBeNull();
    expect(page.element('preview-dates')?.textContent).toContain('16 Dec 2026');
  });

  it('shows frozen quote facts and requires every shop page plus explicit confirmation', async () => {
    const page = render();
    await load(page);
    const first = http.expectOne(path + '/shops');
    expect(page.element('total')?.textContent).toBe('₹270.05');
    expect(page.element('capacity')?.textContent).toBe('3');
    expect(page.element('member-limit')?.textContent).toBe('8');
    expect(Array.from(page.element('policies')!.querySelectorAll('dd'), item => item.textContent)).toEqual(['terms/3', 'refund/2']);
    expect(page.consent().disabled).toBe(true);
    expect(page.button(en.billing.checkout.accept)?.disabled).toBe(true);

    const cursor = shops[0].shopId;
    first.flush(shopPage(shops.slice(0, 1), cursor));
    await page.update();
    expect(page.button(en.billing.checkout.accept)?.disabled).toBe(true);
    http.expectOne(path + '/shops?after=' + cursor).flush(shopPage(shops.slice(1)));
    await vi.waitFor(async () => {
      await page.update();
      expect(page.covered()).toEqual(shops.map(shop => shop.name));
    });

    expect(page.consent().disabled).toBe(false);
    expect(page.button(en.billing.checkout.accept)?.disabled).toBe(true);
    page.consent().click();
    await page.update();
    expect(page.button(en.billing.checkout.accept)?.disabled).toBe(false);
    http.expectNone(acceptPath);
  });

  it('disables acceptance at the expiry instant', async () => {
    vi.setSystemTime(Date.parse(quote.expiresAt) - 1000);
    const page = render();
    await load(page);
    await showCoverage(page);
    page.consent().click();
    await page.update();
    expect(page.button(en.billing.checkout.accept)?.disabled).toBe(false);
    await vi.advanceTimersByTimeAsync(1000);
    await page.update();
    expect(page.button(en.billing.checkout.accept)?.disabled).toBe(true);
    expect(page.alert()).toContain(en.billing.checkout.errors.expired);
    http.expectNone(acceptPath);
  });

  it('locks the review when the server reports expiry ahead of the device clock', async () => {
    const page = render();
    await load(page);
    await showCoverage(page);
    const request = await submit(page);
    request.flush({ code: 'Billing.CheckoutExpired' }, { status: 409, statusText: 'Conflict' });
    await vi.waitFor(async () => {
      await page.update();
      expect(page.alert()).toContain(en.billing.checkout.errors.expired);
    });
    expect(page.consent().disabled).toBe(true);
    expect(page.button(en.billing.checkout.accept)?.disabled).toBe(true);
    expect(TestBed.inject(CheckoutDrafts).readAccept(scope, order)).toBeNull();
  });

  it('retains a lost acceptance across remount and retries the same body after lookup returns 404', async () => {
    const page = render();
    await load(page);
    await showCoverage(page);
    const request = await submit(page);
    const command = request.request.body as AcceptQuote;
    expect(command).toEqual({
      operationKey: expect.any(String), expectedRevision: quote.revision, snapshotHash: quote.snapshotHash,
      customerTermsVersion: quote.customerTermsVersion, refundPolicyVersion: quote.refundPolicyVersion, confirmed: true,
    });
    const drafts = TestBed.inject(CheckoutDrafts);
    expect(drafts.readAccept(scope, order)).toEqual({ command, settled: false });
    request.error(new ProgressEvent('error'));
    await page.update();
    expect(page.alert()).toContain(en.billing.checkout.errors.unknown);
    page.fixture.destroy();

    const reopened = render();
    http.expectOne(ordersPath(scope.account) + '/operations/accept/' + command.operationKey)
      .flush(null, { status: 404, statusText: 'Not Found' });
    await reopened.update();
    await load(reopened);
    await showCoverage(reopened);
    expect(drafts.readAccept(scope, order)).toEqual({ command, settled: false });
    expect(reopened.button(en.billing.checkout.accept)).toBeUndefined();
    http.expectNone(acceptPath);

    reopened.button(en.billing.checkout.retry)!.click();
    await reopened.update();
    const retry = http.expectOne({ method: 'POST', url: acceptPath });
    expect(retry.request.body).toEqual(command);
    retry.flush({ ...quote, acceptance: { operationKey: command.operationKey, snapshotHash: quote.snapshotHash, acceptedAt: reviewTime } });
    await vi.waitFor(async () => {
      await reopened.update();
      expect(reopened.element('accepted')).toBeTruthy();
    });
    await showCoverage(reopened);
    http.expectOne(path + '/payment').flush(notStarted());
    expect(drafts.readAccept(scope, order)).toEqual({ command, settled: true });
  });

  it('ignores a quote response after navigation changes the billing account', async () => {
    const page = render();
    const previous = http.expectOne(path);
    const otherAccount = id(8);
    const otherOrder = id(9);
    params.next(convertToParamMap({ accountId: otherAccount }));
    query.next(convertToParamMap({ order: otherOrder }));
    await page.update();
    const current = http.expectOne(pathFor(otherAccount, otherOrder));
    previous.flush(quote);
    await page.update();
    expect(page.element('total')).toBeNull();
    http.expectNone(path + '/shops');

    const otherQuote = { ...quote, accountId: otherAccount, orderId: otherOrder, amountPaise: 45000 };
    current.flush(otherQuote);
    await page.update();
    http.expectOne(pathFor(otherAccount, otherOrder) + '/shops')
      .flush({ ...shopPage(shops), accountId: otherAccount, orderId: otherOrder });
    await page.update();
    expect(page.element('total')?.textContent).toBe('₹450.00');
  });
});

describe('CheckoutService.shops', () => {
  it.each([
    ['count', shops.slice(0, 1)],
    ['shop-set hash', [{ ...shops[0], shopId: id(9) }, shops[1]]],
  ] as const)('refuses paged coverage with a mismatched %s', async (_, items) => {
    const coverage = TestBed.inject(CheckoutService).shops(scope, quote, () => true);
    const rejected = expect(coverage).rejects.toThrow('Invalid checkout shop set');
    const cursor = shops[0].shopId;
    http.expectOne(path + '/shops').flush({ ...shopPage([]), items: items.slice(0, 1), nextCursor: cursor });
    await Promise.resolve();
    http.expectOne(path + '/shops?after=' + cursor).flush({ ...shopPage([]), items: items.slice(1) });
    await rejected;
  });
});
