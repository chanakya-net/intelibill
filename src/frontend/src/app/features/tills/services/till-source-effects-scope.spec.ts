import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Actions } from '@ngrx/effects';
import { Action, Store, provideStore } from '@ngrx/store';
import { Observable, Subject, firstValueFrom } from 'rxjs';
import { IDBFactory } from 'fake-indexeddb';
import { AuthService } from '../../../core/auth/auth.service';
import { authInterceptor } from '../../../core/interceptors/auth.interceptor';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { scopeResetMetaReducer } from '../../../core/context/shop-context.actions';
import { AppReloadService } from '../../../core/services/app-reload.service';
import { ExpensesEffects } from '../../expenses/state/expenses.effects';
import { ExpensesActions } from '../../expenses/state/expenses.actions';
import { expensesReducer, ExpensesState } from '../../expenses/state/expenses.reducer';
import { ExpenseService, RecordExpenseRequest } from '../../expenses/services/expense.service';
import { SalesEffects } from '../../sales/state/sales.effects';
import { SalesActions } from '../../sales/state/sales.actions';
import { salesReducer, SalesState } from '../../sales/state/sales.reducer';
import {
  SaleService,
  RecordSaleReturnRequest,
  VoidSaleReturnRequest,
} from '../../sales/services/sale.service';
import { TillDraftService } from './till-draft.service';
const owner = { userId: 'user', shopId: 'shop-a' };
const expense: RecordExpenseRequest = {
  amount: 25,
  categoryName: 'Misc',
  paidTo: 'Vendor',
  description: null,
  expenseDate: '2026-11-08',
  operationKey: 'original-k1',
  tillOwner: owner,
  till: { sessionId: 'original-drawer' },
  tenderLine: { method: 'Cash', appliedAmount: 25 },
};
const returned: RecordSaleReturnRequest = {
  operationKey: 'original-k1',
  tillOwner: owner,
  till: { sessionId: 'original-drawer' },
  items: [],
  payoutDestination: null,
  notes: null,
  dueReductionOverrideAmount: null,
  dueOverrideReason: null,
};
const voided: VoidSaleReturnRequest = {
  operationKey: 'original-k1',
  tillOwner: owner,
  tillParentSaleId: 'sale',
  reason: 'Recovered',
  settlementEvidence: { disposition: 'NoPhysicalMovement', reason: 'No refund' },
};
const cases = [
  {
    kind: 'record expense',
    effect: 'recordExpense$',
    feature: 'expenses',
    identity: 'expense-record:new:original-k1',
    lookup: 'expense-record',
    path: '/expenses',
    response: { id: 'expense' },
    request: ExpensesActions.recordExpenseRequested({ payload: expense }),
  },
  {
    kind: 'correct expense',
    effect: 'correctExpense$',
    feature: 'expenses',
    identity: 'expense-correction:expense:correction',
    lookup: 'expense-correction',
    path: '/expenses/expense/correct',
    response: { id: 'corrected' },
    request: ExpensesActions.correctExpenseRequested({ expenseId: 'expense', payload: expense }),
  },
  {
    kind: 'record return',
    effect: 'recordSaleReturn$',
    feature: 'sales',
    identity: 'sale-return:sale:original-k1',
    lookup: 'sale-return',
    path: '/sales/sale/returns',
    response: { saleId: 'sale', returns: [] },
    request: SalesActions.recordSaleReturnRequested({ saleId: 'sale', payload: returned }),
  },
  {
    kind: 'void return',
    effect: 'voidSaleReturn$',
    feature: 'sales',
    identity: 'sale-return-void:return:void',
    lookup: 'sale-return-void',
    path: '/sales/returns/return/void',
    response: null,
    request: SalesActions.voidSaleReturnRequested({
      saleId: 'sale',
      saleReturnId: 'return',
      payload: voided,
    }),
  },
] as const;
function deferred() {
  let resolve!: () => void;
  const promise = new Promise<void>((r) => {
    resolve = r;
  });
  return { promise, resolve };
}

describe('Till source effects isolate detached outcomes from reset reducers', () => {
  let actions: Subject<Action>, context: ShopContextService, drafts: TillDraftService;
  let http: HttpTestingController, store: Store<{ expenses: ExpensesState; sales: SalesState }>;
  beforeEach(() => {
    vi.stubGlobal('indexedDB', new IDBFactory());
    actions = new Subject();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideStore(
          { expenses: expensesReducer, sales: salesReducer },
          { metaReducers: [scopeResetMetaReducer] },
        ),
        ExpensesEffects,
        SalesEffects,
        { provide: Actions, useValue: new Actions(actions) },
        { provide: AppReloadService, useValue: { restartAtAppRoot: vi.fn() } },
        {
          provide: AuthService,
          useValue: {
            getAccessToken: () => 'token',
            hasRefreshToken: () => false,
            session: () =>
              context.scope()
                ? { user: { id: 'user' }, activeShopId: context.scope()!.shopId }
                : null,
          },
        },
      ],
    });
    context = TestBed.inject(ShopContextService);
    context.initialize(owner);
    drafts = TestBed.inject(TillDraftService);
    http = TestBed.inject(HttpTestingController);
    store = TestBed.inject(Store);
  });
  afterEach(() => {
    try {
      http.verify();
    } finally {
      actions.complete();
      TestBed.resetTestingModule();
      vi.unstubAllGlobals();
    }
  });
  const one = (http: HttpTestingController, method: string, suffix: string) =>
    vi.waitFor(() => http.expectOne((r) => r.method === method && r.url.endsWith(suffix)));
  function effects(test: (typeof cases)[number]): Observable<Action> {
    return test.feature === 'expenses'
      ? TestBed.inject(ExpensesEffects)[test.effect as 'recordExpense$' | 'correctExpense$']
      : TestBed.inject(SalesEffects)[test.effect as 'recordSaleReturn$' | 'voidSaleReturn$'];
  }
  function send(test: (typeof cases)[number]) {
    if (test.kind === 'record expense')
      return firstValueFrom(TestBed.inject(ExpenseService).recordExpense(expense));
    if (test.kind === 'correct expense')
      return firstValueFrom(TestBed.inject(ExpenseService).correctExpense('expense', expense));
    if (test.kind === 'record return')
      return firstValueFrom(TestBed.inject(SaleService).recordSaleReturn('sale', returned));
    return firstValueFrom(TestBed.inject(SaleService).voidSaleReturn('return', voided));
  }

  for (const test of cases)
    for (const boundary of ['list', 'settle', 'settlement-error'] as const)
      it.each(['switch', 'sign-out', 'ABA'] as const)(
        `${test.kind}/${boundary}: no stale success or failure after %s`,
        async (change) => {
          const entered = deferred(),
            barrier = deferred(),
            durableFinished = deferred();
          if (boundary === 'list') {
            const real = drafts.list.bind(drafts);
            vi.spyOn(drafts, 'list').mockImplementationOnce(async (...args) => {
              entered.resolve();
              await barrier.promise;
              try {
                return await real(...args);
              } finally {
                durableFinished.resolve();
              }
            });
          } else {
            const real = drafts.settle.bind(drafts);
            vi.spyOn(drafts, 'settle').mockImplementationOnce(async (...args) => {
              entered.resolve();
              await barrier.promise;
              try {
                if (boundary === 'settlement-error')
                  throw new Error('Storage temporarily unavailable');
                return await real(...args);
              } finally {
                durableFinished.resolve();
              }
            });
          }
          const emitted: Action[] = [];
          const finished = new Promise<void>((resolve, reject) =>
            effects(test).subscribe({
              next: (action) => {
                emitted.push(action);
                store.dispatch(action);
              },
              error: reject,
              complete: resolve,
            }),
          );
          store.dispatch(test.request);
          actions.next(test.request);
          if (boundary !== 'list') (await one(http, 'POST', test.path)).flush(test.response);
          await entered.promise;
          if (change === 'sign-out') context.transition(null, 'sign-out', () => {});
          else {
            context.transition({ ...owner, shopId: 'shop-b' }, 'scope', () => {});
            if (change === 'ABA') context.transition(owner, 'scope', () => {});
          }
          const reset = await firstValueFrom(store.select((s) => s[test.feature]));
          expect(reset.submitting).toBe(false);
          expect(reset.lastMutationType).toBeNull();
          barrier.resolve();
          actions.complete();
          await durableFinished.promise;
          await finished;
          expect(emitted).toEqual([]);
          expect(await firstValueFrom(store.select((s) => s[test.feature]))).toEqual(reset);
          http.expectNone(() => true);
          if (boundary !== 'list') {
            await vi.waitFor(async () => {
              const saved = await drafts.load(owner, test.identity);
              if (boundary === 'settle') expect(saved?.confirmedOperationKey).toBe('original-k1');
              else expect(saved?.pending?.status).toBe('unknown');
            });
            if (boundary === 'settle') {
              expect(await drafts.confirmation(owner, test.identity, 'original-k1')).toMatchObject({
                response: test.response,
              });
              return;
            }
            // A fresh invocation recovers the retained exact key; no replacement POST is needed.
            context.transition(owner, 'sign-in', () => {});
            const recovered = send(test);
            const proof =
              test.kind === 'record return'
                ? { saleId: 'sale', saleReturnId: 'return' }
                : test.kind === 'void return'
                  ? { saleId: 'sale', saleReturnId: 'return' }
                  : test.response;
            (await one(http, 'GET', `/${test.lookup}/original-k1`)).flush({ response: proof });
            if (test.kind === 'record return')
              (await one(http, 'GET', '/sales/sale')).flush(test.response);
            await recovered;
            expect(await drafts.isConfirmed(owner, test.identity, 'original-k1')).toBe(true);
            http.expectNone((r) => r.method === 'POST');
          }
        },
      );

  for (const test of cases)
    it(`${test.kind}: retains genuine current-scope failures`, async () => {
      const emitted: Action[] = [];
      const finished = new Promise<void>((resolve, reject) =>
        effects(test).subscribe({
          next: (action) => {
            emitted.push(action);
            store.dispatch(action);
          },
          error: reject,
          complete: resolve,
        }),
      );
      store.dispatch(test.request);
      actions.next(test.request);
      (await one(http, 'POST', test.path)).flush(
        { detail: 'Current operation refused' },
        { status: 400, statusText: 'Bad Request' },
      );
      actions.complete();
      await finished;
      expect(emitted).toHaveLength(1);
      expect(emitted[0].type.endsWith('Failed')).toBe(true);
      const state = await firstValueFrom(store.select((s) => s[test.feature]));
      expect(state.submitting).toBe(false);
      expect(
        'returnPreviewErrorMessage' in state ? state.returnPreviewErrorMessage : state.errorMessage,
      ).toBe('Current operation refused');
    });
});
