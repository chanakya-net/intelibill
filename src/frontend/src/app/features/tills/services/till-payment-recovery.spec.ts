import { ShopContextService } from '../../../core/context/shop-context.service';
import { provideRouter } from '@angular/router';
import { CustomersPageComponent } from '../../customers/pages/customers-page.component';
import { customersReducer } from '../../customers/state/customers.reducer';
import { TillPostingFieldsComponent } from '../components/till-posting-fields.component';
import { TillPendingActionsComponent } from '../components/till-pending-actions.component';
import { TillContextService } from './till-context.service';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { IDBFactory } from 'fake-indexeddb';
import { firstValueFrom } from 'rxjs';
import { provideStore } from '@ngrx/store';
import { provideEffects } from '@ngrx/effects';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { AuthService } from '../../../core/auth/auth.service';
import { SuppliersEffects } from '../../suppliers/state/suppliers.effects';
import { suppliersReducer } from '../../suppliers/state/suppliers.reducer';
import { MakePaymentOverlayComponent } from '../../suppliers/components/make-payment-overlay.component';
import { SupplierDetailComponent } from '../../suppliers/components/supplier-detail.component';
import {
  FinancialOperationIntentService,
  FinancialOperationType,
} from '../../../core/services/financial-operation-intent.service';
import { CustomerService } from '../../customers/services/customer.service';
import { SupplierLedgerService } from '../../suppliers/services/supplier-ledger.service';
import { TillService } from './till.service';
import { TillDraftService } from './till-draft.service';
const owner = { userId: 'user', shopId: 'shop' };
const body = {
  amount: 25,
  paymentDate: '2026-11-08',
  notes: 'Original',
  tillOwner: owner,
  till: { sessionId: 'drawer', businessDate: '2026-11-08', cashierId: 'user' },
  tenderLine: { method: 'Cash', appliedAmount: 25 },
  futureField: { keep: true },
};
describe('Real payment intent and IndexedDB recovery', () => {
  let http: HttpTestingController;
  let intents: FinancialOperationIntentService;
  let values: Map<string, string>;
  let storage: { getItem: (k: string) => string | null; setItem: (k: string, v: string) => void };
  beforeEach(() => {
    vi.stubGlobal('indexedDB', new IDBFactory());
    values = new Map();
    storage = {
      getItem: (k) => values.get(k) ?? null,
      setItem: (k, v) => {
        values.set(k, v);
      },
    };
    vi.stubGlobal('localStorage', storage);
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { 'en-IN': {} },
          translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
        }),
      ],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideStore({ suppliers: suppliersReducer, customers: customersReducer }),
        provideEffects(SuppliersEffects),
        {
          provide: TillContextService,
          useValue: {
            owner: () => owner,
            sessions: () => [],
            posting: () => ({ till: body.till }),
          },
        },
        {
          provide: AuthService,
          useValue: { session: () => ({ user: { id: owner.userId }, activeShopId: owner.shopId }) },
        },
      ],
    });
    TestBed.inject(ShopContextService).initialize(owner);
    http = TestBed.inject(HttpTestingController);
    intents = TestBed.inject(FinancialOperationIntentService);
  });
  afterEach(() => {
    try {
      http.verify();
    } finally {
      TestBed.resetTestingModule();
      vi.unstubAllGlobals();
    }
  });
  function send(
    type: FinancialOperationType,
    intent: ReturnType<typeof intents.prepare<typeof body>>,
  ) {
    return firstValueFrom(
      intents.send<typeof body, unknown>(owner, intent, (payload, operationKey) =>
        type === 'customer-payment'
          ? TestBed.inject(CustomerService).recordCustomerPayment('party', {
              ...payload,
              operationKey,
            } as never)
          : TestBed.inject(SupplierLedgerService).makePayment('party', {
              ...payload,
              operationKey,
            } as never),
      ),
    );
  }
  it.each(['customer-payment', 'supplier-payment'] as const)(
    'retains %s outer key through lost HTTP, reload, panel recovery and next payment',
    async (type) => {
      const original = intents.prepare(owner, type, 'party', body);
      const lost = send(type, original).catch((e) => e);
      const post = await vi.waitFor(() => http.expectOne((r) => r.method === 'POST'));
      expect(post.request.body.operationKey).toBe(original.operationKey);
      expect(post.request.body.futureField).toEqual(body.futureField);
      post.error(new ProgressEvent('lost'));
      await lost;
      // New store/service instance represents a reopened panel in another tab.
      const drafts = new TillDraftService();
      const pending = (await drafts.list(owner))[0];
      const panel = TestBed.runInInjectionContext(() => new TillPendingActionsComponent());
      const recovered = panel.retry(pending);
      const lookup = await vi.waitFor(() => http.expectOne((r) => r.method === 'GET'));
      expect(lookup.request.url).toContain(original.operationKey);
      const response = { entryId: 'entry', receiptId: 'receipt', amount: 25 };
      lookup.flush({ operationKey: original.operationKey, response });
      await recovered;
      expect(panel.error()).toBe(false);
      expect(panel.recovered()).toBe(original.operationKey);
      expect(intents.pending(owner, type, 'party')).toBeNull();
      const checked = firstValueFrom(intents.check(owner, original));
      const outerLookup = http.expectOne((r) => r.method === 'GET');
      expect(outerLookup.request.url).toContain(original.operationKey);
      outerLookup.flush({ operationKey: original.operationKey, response });
      expect(await checked).toEqual(response);
      // A stale outer editor can retry safely after panel confirmation/tombstone cleanup.
      await drafts.clearConfirmed(owner, pending.targetId, original.operationKey);
      expect(await send(type, original)).toEqual(response);
      http.expectNone((r) => r.method === 'POST');
      const next = intents.prepare(owner, type, 'party', { ...body, amount: 30 });
      expect(next.operationKey).not.toBe(original.operationKey);
      const sending = send(type, next);
      const nextPost = await vi.waitFor(() => http.expectOne((r) => r.method === 'POST'));
      expect(nextPost.request.body.operationKey).toBe(next.operationKey);
      nextPost.flush({ ...response, entryId: 'next' });
      await sending;
      expect(intents.pending(owner, type, 'party')).toBeNull();
    },
  );
  it('recovers the actual customer form after reload and allows a new payment with a new key', async () => {
    const createPage = () => {
      const page = TestBed.runInInjectionContext(() => new CustomersPageComponent());
      page.paymentTill = TestBed.runInInjectionContext(() => new TillPostingFieldsComponent());
      page.selectedAccountCustomer.set({ customerId: 'party' } as never);
      page.paymentForm.patchValue({ amount: 25, paymentDate: '2026-11-08', method: 'Cash' });
      return page;
    };
    const page = createPage();
    page.onRecordPayment();
    const post = await vi.waitFor(() => http.expectOne((r) => r.method === 'POST'));
    const intent = intents.pending(owner, 'customer-payment', 'party')!;
    expect(post.request.body.operationKey).toBe(intent.operationKey);
    post.error(new ProgressEvent('lost'));
    await vi.waitFor(() => expect(page.submittingPayment()).toBe(false));
    const reopened = createPage();
    expect(reopened.pendingPayment()?.operationKey).toBe(intent.operationKey);
    const pending = (await new TillDraftService().list(owner))[0];
    const panel = TestBed.runInInjectionContext(() => new TillPendingActionsComponent());
    const recovered = panel.retry(pending);
    (await vi.waitFor(() => http.expectOne((r) => r.method === 'GET'))).flush({
      operationKey: intent.operationKey,
      response: { entryId: 'original-entry', receiptId: 'original-receipt' },
    });
    await recovered;
    expect(reopened.pendingPayment()).toBeNull();
    reopened.paymentForm.patchValue({ amount: 30 });
    reopened.onRecordPayment();
    const nextPost = await vi.waitFor(() => http.expectOne((r) => r.method === 'POST'));
    expect(nextPost.request.body.operationKey).not.toBe(intent.operationKey);
    nextPost.flush({ entryId: 'new-entry', receiptId: 'new-receipt' });
    (await vi.waitFor(() => http.expectOne((r) => r.url.endsWith('/party/account')))).flush({
      sales: [],
      receipts: [],
      ledgerEntries: [],
    });
    expect(reopened.pendingPayment()).toBeNull();
  });
  it.each(['overlay', 'detail'] as const)(
    'recovers the actual supplier %s entrypoint through the real facade/effects and permits another payment',
    async (entrypoint) => {
      const component = TestBed.runInInjectionContext(() =>
        entrypoint === 'overlay'
          ? new MakePaymentOverlayComponent()
          : new SupplierDetailComponent(),
      );
      if (component instanceof MakePaymentOverlayComponent)
        component.supplier = { supplierId: 'party' } as never;
      else component.currentSupplierId.set('party');
      component.onPaymentSubmitted(body as never);
      const post = await vi.waitFor(() => http.expectOne((r) => r.method === 'POST'));
      const original = intents.pending(owner, 'supplier-payment', 'party')!;
      expect(post.request.body.operationKey).toBe(original.operationKey);
      post.error(new ProgressEvent('lost'));
      await vi.waitFor(() =>
        expect(intents.pending(owner, 'supplier-payment', 'party')?.status).toBe('unknown'),
      );
      if (component instanceof SupplierDetailComponent) component.currentSupplierId.set(null);
      const pending = (await new TillDraftService().list(owner))[0];
      const panel = TestBed.runInInjectionContext(() => new TillPendingActionsComponent());
      const recovering = panel.retry(pending);
      const lookup = await vi.waitFor(() => http.expectOne((r) => r.method === 'GET'));
      lookup.flush({ operationKey: original.operationKey, response: { id: 'entry', amount: 25 } });
      await recovering;
      expect(panel.error()).toBe(false);
      expect(intents.pending(owner, 'supplier-payment', 'party')).toBeNull();
      const next = TestBed.runInInjectionContext(() =>
        entrypoint === 'overlay'
          ? new MakePaymentOverlayComponent()
          : new SupplierDetailComponent(),
      );
      if (next instanceof MakePaymentOverlayComponent)
        next.supplier = { supplierId: 'party' } as never;
      else next.currentSupplierId.set('party');
      next.onPaymentSubmitted({ ...body, amount: 30 } as never);
      const nextPost = await vi.waitFor(() => http.expectOne((r) => r.method === 'POST'));
      expect(nextPost.request.body.operationKey).not.toBe(original.operationKey);
      if (next instanceof SupplierDetailComponent) next.currentSupplierId.set(null);
      nextPost.flush({ id: 'new-entry', amount: 30 });
      await vi.waitFor(() =>
        expect(intents.pending(owner, 'supplier-payment', 'party')).toBeNull(),
      );
    },
  );
  it('returns another tab’s verified response when confirmation wins the recovery claim race', async () => {
    const intent = intents.prepare(owner, 'customer-payment', 'party', body);
    const lost = send('customer-payment', intent).catch((e) => e);
    (await vi.waitFor(() => http.expectOne((r) => r.method === 'POST'))).error(
      new ProgressEvent('lost'),
    );
    await lost;
    const drafts = TestBed.inject(TillDraftService);
    const pending = (await drafts.list(owner))[0];
    let release!: () => void;
    const barrier = new Promise<void>((resolve) => {
      release = resolve;
    });
    const claim = drafts.claimRecovery.bind(drafts);
    const spy = vi.spyOn(drafts, 'claimRecovery').mockImplementationOnce(async (...args) => {
      await barrier;
      return claim(...args);
    });
    const retry = send('customer-payment', intent);
    await vi.waitFor(() => expect(spy).toHaveBeenCalledTimes(1));
    const panel = TestBed.runInInjectionContext(() => new TillPendingActionsComponent());
    const recovering = panel.retry(pending);
    const response = { entryId: 'same-entry', receiptId: 'same-receipt', amount: 25 };
    (await vi.waitFor(() => http.expectOne((r) => r.method === 'GET'))).flush({
      operationKey: intent.operationKey,
      response,
    });
    await recovering;
    await new TillDraftService().clearConfirmed(owner, pending.targetId, intent.operationKey);
    release();
    expect(await retry).toEqual(response);
    expect(intents.pending(owner, 'customer-payment', 'party')).toBeNull();
    http.expectNone(() => true);
  });
  it('does not send a different outer key against an unresolved equal-amount payment', async () => {
    const original = intents.prepare(owner, 'customer-payment', 'party', body);
    const lost = send('customer-payment', original).catch((e) => e);
    (await vi.waitFor(() => http.expectOne((r) => r.method === 'POST'))).error(
      new ProgressEvent('lost'),
    );
    await lost;
    const competing = intents.prepare(owner, 'customer-payment', 'party', body);
    const rejected = send('customer-payment', competing).catch((e) => e);
    await expect(rejected).resolves.toMatchObject({
      message: 'tills.pendingRecovery',
      operationKey: original.operationKey,
    });
    http.expectNone(() => true);
  });
  it('retains a legacy key-only proof and outer intent when its response lookup is missing; never posts again', async () => {
    const intent = intents.prepare(owner, 'customer-payment', 'party', body);
    const drafts = new TillDraftService();
    const identity = `customer-payment:party:${intent.operationKey}`;
    await drafts.prepare(
      owner,
      identity,
      { payload: { ...body, operationKey: intent.operationKey } },
      0,
    );
    await drafts.settle(owner, identity, intent.operationKey, 'confirmed');
    await drafts.clearConfirmed(owner, identity, intent.operationKey);
    const retry = send('customer-payment', intent).catch((e) => e);
    const lookup = await vi.waitFor(() => http.expectOne((r) => r.method === 'GET'));
    lookup.flush({}, { status: 404, statusText: 'Not Found' });
    await retry;
    expect(intents.pending(owner, 'customer-payment', 'party')?.operationKey).toBe(
      intent.operationKey,
    );
    expect(await drafts.isConfirmed(owner, identity, intent.operationKey)).toBe(true);
    http.expectNone((r) => r.method === 'POST');
  });
  it.each(['customer', 'overlay', 'detail'] as const)(
    'replays released pre-Till %s intent unchanged after a current till is selected',
    async (entrypoint) => {
      const type = entrypoint === 'customer' ? 'customer-payment' : 'supplier-payment';
      const legacy = {
        amount: 25,
        paymentDate: '2026-11-08',
        notes: 'Frozen before till enablement',
        method: 'Cash',
        futureField: { preserve: true },
      };
      const intent = intents.prepare(owner, type, 'party', legacy);
      // Released legacy retries require only their existing localStorage intent, even if IDB is unavailable.
      vi.stubGlobal('indexedDB', undefined);
      if (entrypoint === 'customer') {
        const page = TestBed.runInInjectionContext(() => new CustomersPageComponent());
        page.paymentTill = TestBed.runInInjectionContext(() => new TillPostingFieldsComponent());
        page.selectedAccountCustomer.set({ customerId: 'party' } as never);
        page.onRetryPendingPayment();
      } else if (entrypoint === 'overlay') {
        const overlay = TestBed.runInInjectionContext(() => new MakePaymentOverlayComponent());
        overlay.supplier = { supplierId: 'party' } as never;
        overlay.onRetryPendingPayment();
      } else {
        const detail = TestBed.runInInjectionContext(() => new SupplierDetailComponent());
        detail.currentSupplierId.set('party');
        detail.onRetryPendingPayment();
        detail.currentSupplierId.set(null);
      }
      const post = await vi.waitFor(() => http.expectOne((r) => r.method === 'POST'));
      expect(post.request.body).toEqual({ ...legacy, operationKey: intent.operationKey });
      expect(post.request.body.till).toBeUndefined();
      expect(post.request.body.settlementEvidence).toBeUndefined();
      post.flush({ entryId: 'legacy-entry', receiptId: 'legacy-receipt' });
      if (entrypoint === 'customer')
        (await vi.waitFor(() => http.expectOne((r) => r.url.endsWith('/party/account')))).flush({
          sales: [],
          receipts: [],
          ledgerEntries: [],
        });
      await vi.waitFor(() => expect(intents.pending(owner, type, 'party')).toBeNull());
    },
  );
  it('refreshes outer pending state when another tab confirms through storage', () => {
    const intent = intents.prepare(owner, 'customer-payment', 'party', body);
    const revision = intents.revision();
    const key = 'intelibill.financial-operation-intents.v1';
    values.set(key, '{}');
    window.dispatchEvent(new StorageEvent('storage', { key, newValue: '{}' }));
    expect(intents.revision()).toBeGreaterThan(revision);
    expect(intents.pending(owner, 'customer-payment', 'party')).toBeNull();
    expect(intent.operationKey).toBeTruthy();
  });
  it('only clears an outer intent for the exact owner, type, target and key', () => {
    const intent = intents.prepare(owner, 'customer-payment', 'party', body);
    for (const [scope, type, target, key] of [
      [{ ...owner, shopId: 'other' }, 'customer-payment', 'party', intent.operationKey],
      [owner, 'supplier-payment', 'party', intent.operationKey],
      [owner, 'customer-payment', 'other', intent.operationKey],
      [owner, 'customer-payment', 'party', 'other'],
    ] as const)
      intents.confirmMatching(scope, type, target, key);
    expect(intents.pending(owner, 'customer-payment', 'party')?.operationKey).toBe(
      intent.operationKey,
    );
  });
  it('returns the exact saved response after outer cleanup fails and another tab deletes the confirmed row', async () => {
    const original = intents.prepare(owner, 'supplier-payment', 'party', body);
    const sending = send('supplier-payment', original);
    const post = await vi.waitFor(() => http.expectOne((r) => r.method === 'POST'));
    const real = storage.setItem;
    storage.setItem = () => {
      throw new Error('quota');
    };
    post.flush({ entryId: 'confirmed', amount: 25 });
    await sending;
    expect(intents.pending(owner, 'supplier-payment', 'party')?.operationKey).toBe(
      original.operationKey,
    );
    storage.setItem = real;
    const drafts = new TillDraftService();
    const row = (await drafts.list(owner))[0];
    await drafts.clearConfirmed(owner, row.targetId, row.confirmedOperationKey!);
    expect(await send('supplier-payment', original)).toEqual({ entryId: 'confirmed', amount: 25 });
    http.expectNone(() => true);
    expect(intents.pending(owner, 'supplier-payment', 'party')).toBeNull();
  });
});
