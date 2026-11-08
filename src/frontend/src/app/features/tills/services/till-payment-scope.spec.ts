import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Store, provideStore } from '@ngrx/store';
import { Actions } from '@ngrx/effects';
import { Subject, firstValueFrom } from 'rxjs';
import { IDBFactory } from 'fake-indexeddb';
import { AuthService } from '../../../core/auth/auth.service';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { scopeResetMetaReducer } from '../../../core/context/shop-context.actions';
import { AppReloadService } from '../../../core/services/app-reload.service';
import { FinancialOperationIntentService } from '../../../core/services/financial-operation-intent.service';
import { SuppliersEffects } from '../../suppliers/state/suppliers.effects';
import { SuppliersActions } from '../../suppliers/state/suppliers.actions';
import { suppliersReducer, SuppliersState } from '../../suppliers/state/suppliers.reducer';
import {
  SupplierLedgerService,
  MakePaymentRequest,
} from '../../suppliers/services/supplier-ledger.service';
import { TillDraftService } from './till-draft.service';
const owner = { userId: 'user', shopId: 'shop-a' };
const other = { ...owner, shopId: 'shop-b' };
const payload: MakePaymentRequest = {
  amount: 25,
  paymentDate: '2026-11-08',
  notes: 'Original',
  tillOwner: owner,
  till: { sessionId: 'original-drawer' } as never,
};
const response = { id: 'original-entry', supplierId: 'party', amount: 25 };
const deferred = () => {
  let resolve!: () => void;
  const promise = new Promise<void>((r) => {
    resolve = r;
  });
  return { promise, resolve };
};
describe('Payment cached confirmation scope completion', () => {
  let actions: Subject<ReturnType<typeof SuppliersActions.retryPaymentRequested>>;
  let context: ShopContextService;
  let drafts: TillDraftService;
  let intents: FinancialOperationIntentService;
  let http: HttpTestingController;
  let store: Store<{ suppliers: SuppliersState }>;
  beforeEach(() => {
    vi.stubGlobal('indexedDB', new IDBFactory());
    const values = new Map<string, string>();
    vi.stubGlobal('localStorage', {
      getItem: (k: string) => values.get(k) ?? null,
      setItem: (k: string, v: string) => {
        values.set(k, v);
      },
    });
    actions = new Subject();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideStore({ suppliers: suppliersReducer }, { metaReducers: [scopeResetMetaReducer] }),
        SuppliersEffects,
        { provide: Actions, useValue: new Actions(actions) },
        { provide: AppReloadService, useValue: { restartAtAppRoot: vi.fn() } },
        {
          provide: AuthService,
          useFactory: () => {
            const ctx = TestBed.inject(ShopContextService);
            return {
              session: () => {
                const scope = ctx.scope();
                return scope ? { user: { id: scope.userId }, activeShopId: scope.shopId } : null;
              },
            };
          },
        },
      ],
    });
    context = TestBed.inject(ShopContextService);
    context.initialize(owner);
    drafts = TestBed.inject(TillDraftService);
    intents = TestBed.inject(FinancialOperationIntentService);
    http = TestBed.inject(HttpTestingController);
    store = TestBed.inject(Store);
  });
  afterEach(() => {
    try {
      http.verify();
    } finally {
      TestBed.resetTestingModule();
      vi.unstubAllGlobals();
    }
  });
  for (const boundary of ['source-list', 'confirmation', 'claim-fallback'] as const)
    it.each(['shop', 'sign-out', 'leave-and-return'] as const)(
      `${boundary}: suppresses old supplier completion after %s and retains exact recovery`,
      async (change) => {
        const intent = intents.prepare(owner, 'supplier-payment', 'party', payload);
        const target = `supplier-payment:party:${intent.operationKey}`;
        const { tillOwner, ...body } = payload;
        await drafts.prepare(
          owner,
          target,
          {
            payload: {
              ...body,
              operationKey: intent.operationKey,
              __requestPath: '/suppliers/party/payments',
            },
          },
          0,
        );
        if (boundary !== 'claim-fallback') {
          await drafts.settle(owner, target, intent.operationKey, 'confirmed', { response });
          await drafts.clearConfirmed(owner, target, intent.operationKey);
        }
        const barrier = deferred();
        const entered = deferred();
        if (boundary === 'source-list') {
          const list = drafts.list.bind(drafts);
          vi.spyOn(drafts, 'list').mockImplementationOnce(async (...args) => {
            entered.resolve();
            await barrier.promise;
            return list(...args);
          });
        } else if (boundary === 'confirmation') {
          const confirmation = drafts.confirmation.bind(drafts);
          vi.spyOn(drafts, 'confirmation').mockImplementationOnce(async (...args) => {
            entered.resolve();
            await barrier.promise;
            return confirmation(...args);
          });
        } else {
          const claim = drafts.claimRecovery.bind(drafts);
          vi.spyOn(drafts, 'claimRecovery').mockImplementationOnce(async (...args) => {
            entered.resolve();
            await barrier.promise;
            return claim(...args);
          });
        }
        const emitted: unknown[] = [];
        const finished = new Promise<void>((resolve, reject) => {
          TestBed.inject(SuppliersEffects).retryPayment$.subscribe({
            next: (action) => {
              emitted.push(action);
              store.dispatch(action);
            },
            error: reject,
            complete: resolve,
          });
        });
        const action = SuppliersActions.retryPaymentRequested({ supplierId: 'party' });
        store.dispatch(action);
        actions.next(action);
        await entered.promise;
        if (boundary === 'claim-fallback') {
          const anotherTab = new TillDraftService();
          await anotherTab.settle(owner, target, intent.operationKey, 'confirmed', { response });
          await anotherTab.clearConfirmed(owner, target, intent.operationKey);
        }
        if (change === 'sign-out') context.transition(null, 'sign-out', () => {});
        else {
          context.transition(other, 'scope', () => {});
          if (change === 'leave-and-return') context.transition(owner, 'scope', () => {});
        }
        const reset = await firstValueFrom(store.select((s) => s.suppliers));
        expect(reset.lastMutationSucceeded).toBe(false);
        barrier.resolve();
        actions.complete();
        await finished;
        expect(emitted).toEqual([]);
        const state = await firstValueFrom(store.select((s) => s.suppliers));
        expect(state.lastMutationSucceeded).toBe(false);
        expect(state.errorMessage).toBe('');
        expect(state.submitting).toBe(false);
        expect(intents.pending(owner, 'supplier-payment', 'party')?.operationKey).toBe(
          intent.operationKey,
        );
        expect(await drafts.isConfirmed(owner, target, intent.operationKey)).toBe(true);
        http.expectNone(() => true);
        // An explicit recovery after re-entering is a new invocation under the current tab epoch.
        context.transition(owner, 'sign-in', () => {});
        const result = await firstValueFrom(
          intents.send(owner, intent, (frozen, operationKey) =>
            TestBed.inject(SupplierLedgerService).makePayment('party', { ...frozen, operationKey }),
          ),
        );
        expect(result).toEqual(response);
        expect(intents.pending(owner, 'supplier-payment', 'party')).toBeNull();
        expect(await drafts.isConfirmed(owner, target, intent.operationKey)).toBe(true);
        http.expectNone(() => true);
      },
    );
});
