import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { IDBFactory } from 'fake-indexeddb';
import { AuthService } from '../../../core/auth/auth.service';
import { authInterceptor } from '../../../core/interceptors/auth.interceptor';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { AppReloadService } from '../../../core/services/app-reload.service';
import { SupplierLedgerService } from '../../suppliers/services/supplier-ledger.service';
import { TillDraftService } from './till-draft.service';

const owner = { userId: 'user', shopId: 'shop-a' };
const payload = {
  amount: 25,
  paymentDate: '2026-11-08',
  notes: 'Frozen',
  operationKey: 'original-k1',
  tillOwner: owner,
  till: { sessionId: 'original-drawer' },
};
function deferred() {
  let resolve!: () => void;
  const promise = new Promise<void>((r) => {
    resolve = r;
  });
  return { promise, resolve };
}

describe('Till dispatch remains owned by its invocation epoch', () => {
  let context: ShopContextService, drafts: TillDraftService, http: HttpTestingController;
  beforeEach(() => {
    vi.stubGlobal('indexedDB', new IDBFactory());
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
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
  });
  afterEach(() => {
    try {
      http.verify();
    } finally {
      TestBed.resetTestingModule();
      vi.unstubAllGlobals();
    }
  });

  for (const boundary of ['prepare', 'claimRecovery'] as const)
    it.each(['switch', 'sign-out', 'ABA'] as const)(
      `${boundary}: zero HTTP after %s; explicit recovery keeps original key/payload`,
      async (change) => {
        const identity = 'supplier-payment:party:original-k1';
        const { tillOwner, ...body } = payload;
        if (boundary === 'claimRecovery')
          await drafts.prepare(
            owner,
            identity,
            { payload: { ...body, __requestPath: '/suppliers/party/payments' } },
            0,
          );
        const entered = deferred(),
          barrier = deferred();
        // Delay the real durable operation; no fake claim or mock HTTP success replaces production code.
        if (boundary === 'prepare') {
          const prepare = drafts.prepare.bind(drafts);
          vi.spyOn(drafts, 'prepare').mockImplementationOnce(async (...args) => {
            entered.resolve();
            await barrier.promise;
            return prepare(...args);
          });
        } else {
          const claim = drafts.claimRecovery.bind(drafts);
          vi.spyOn(drafts, 'claimRecovery').mockImplementationOnce(async (...args) => {
            entered.resolve();
            await barrier.promise;
            return claim(...args);
          });
        }
        const api = TestBed.inject(SupplierLedgerService);
        let finished = false;
        const result = firstValueFrom(api.makePayment('party', payload))
          .catch((error) => error)
          .finally(() => {
            finished = true;
          });
        await entered.promise;
        if (change === 'sign-out') context.transition(null, 'sign-out', () => {});
        else {
          context.transition({ ...owner, shopId: 'shop-b' }, 'scope', () => {});
          if (change === 'ABA') context.transition(owner, 'scope', () => {});
        }
        barrier.resolve();
        // Drain an unexpected request too, so RED reports the actual unwanted dispatch rather than a timeout.
        const unexpected: string[] = [];
        await vi.waitFor(() => {
          for (const req of http.match(() => true)) {
            unexpected.push(req.request.method);
            req.flush(
              req.request.method === 'GET' ? { response: { id: 'unwanted' } } : { id: 'unwanted' },
            );
          }
          expect(finished).toBe(true);
        });
        const settled = await result;
        expect(unexpected).toEqual([]);
        expect((settled as Error)?.name).toBe('StaleScopeError');
        expect((await drafts.load(owner, identity))?.pending?.payload).toEqual({
          ...body,
          __requestPath: '/suppliers/party/payments',
        });
        expect(await drafts.isConfirmed(owner, identity, 'original-k1')).toBe(false);
        context.transition(owner, 'sign-in', () => {});
        const recovered = firstValueFrom(api.makePayment('party', payload));
        (
          await vi.waitFor(() =>
            http.expectOne(
              (r) => r.method === 'GET' && r.url.endsWith('/supplier-payment/original-k1'),
            ),
          )
        ).flush({}, { status: 404, statusText: 'Not Found' });
        const post = await vi.waitFor(() => http.expectOne((r) => r.method === 'POST'));
        expect(post.request.url.endsWith('/suppliers/party/payments')).toBe(true);
        expect(post.request.body).toEqual(body);
        post.flush({ id: 'confirmed' });
        expect(await recovered).toEqual({ id: 'confirmed' });
        expect(await drafts.isConfirmed(owner, identity, 'original-k1')).toBe(true);
      },
    );
  for (const boundary of ['list', 'load', 'confirmation'] as const)
    it.each(['switch', 'sign-out', 'ABA'] as const)(
      `${boundary}: no HTTP or new preparation after %s during prerequisite read`,
      async (change) => {
        const entered = deferred(),
          barrier = deferred();
        if (boundary === 'list') {
          const real = drafts.list.bind(drafts);
          vi.spyOn(drafts, 'list')
            .mockImplementationOnce(real)
            .mockImplementationOnce(async (...args) => {
              entered.resolve();
              await barrier.promise;
              return real(...args);
            });
        } else if (boundary === 'load') {
          const real = drafts.load.bind(drafts);
          vi.spyOn(drafts, 'load').mockImplementationOnce(async (...args) => {
            entered.resolve();
            await barrier.promise;
            return real(...args);
          });
        } else {
          const real = drafts.confirmation.bind(drafts);
          vi.spyOn(drafts, 'confirmation').mockImplementationOnce(async (...args) => {
            entered.resolve();
            await barrier.promise;
            return real(...args);
          });
        }
        const api = TestBed.inject(SupplierLedgerService);
        let finished = false;
        const result = firstValueFrom(api.makePayment('party', payload))
          .catch((error) => error)
          .finally(() => {
            finished = true;
          });
        await entered.promise;
        if (change === 'sign-out') context.transition(null, 'sign-out', () => {});
        else {
          context.transition({ ...owner, shopId: 'shop-b' }, 'scope', () => {});
          if (change === 'ABA') context.transition(owner, 'scope', () => {});
        }
        barrier.resolve();
        const unexpected: string[] = [];
        await vi.waitFor(() => {
          for (const req of http.match(() => true)) {
            unexpected.push(req.request.method);
            req.flush({ id: 'unwanted' });
          }
          expect(finished).toBe(true);
        });
        expect(unexpected).toEqual([]);
        expect((await result).name).toBe('StaleScopeError');
        expect(await drafts.list(owner)).toEqual([]);
        context.transition(owner, 'sign-in', () => {});
        const recovered = firstValueFrom(api.makePayment('party', payload));
        const post = await vi.waitFor(() => http.expectOne((r) => r.method === 'POST'));
        const { tillOwner, ...body } = payload;
        expect(post.request.body).toEqual(body);
        post.flush({ id: 'confirmed' });
        expect(await recovered).toEqual({ id: 'confirmed' });
      },
    );

  it.each(['switch', 'sign-out', 'ABA'] as const)(
    'recovery lookup404: no POST after %s before its continuation',
    async (change) => {
      const identity = 'supplier-payment:party:original-k1';
      const { tillOwner, ...body } = payload;
      await drafts.prepare(
        owner,
        identity,
        { payload: { ...body, __requestPath: '/suppliers/party/payments' } },
        0,
      );
      const api = TestBed.inject(SupplierLedgerService);
      let finished = false;
      const result = firstValueFrom(api.makePayment('party', payload))
        .catch((error) => error)
        .finally(() => {
          finished = true;
        });
      const lookup = await vi.waitFor(() => http.expectOne((r) => r.method === 'GET'));
      lookup.flush({}, { status: 404, statusText: 'Not Found' });
      if (change === 'sign-out') context.transition(null, 'sign-out', () => {});
      else {
        context.transition({ ...owner, shopId: 'shop-b' }, 'scope', () => {});
        if (change === 'ABA') context.transition(owner, 'scope', () => {});
      }
      const unexpected: string[] = [];
      await vi.waitFor(() => {
        for (const req of http.match(() => true)) {
          unexpected.push(req.request.method);
          req.flush({ id: 'unwanted' });
        }
        expect(finished).toBe(true);
      });
      expect(unexpected).toEqual([]);
      expect((await result).name).toBe('StaleScopeError');
      expect((await drafts.load(owner, identity))?.pending?.payload).toEqual({
        ...body,
        __requestPath: '/suppliers/party/payments',
      });
      context.transition(owner, 'sign-in', () => {});
      const recovered = firstValueFrom(api.makePayment('party', payload));
      (
        await vi.waitFor(() =>
          http.expectOne(
            (r) => r.method === 'GET' && r.url.endsWith('/supplier-payment/original-k1'),
          ),
        )
      ).flush({ response: { id: 'confirmed' } });
      expect(await recovered).toEqual({ id: 'confirmed' });
      http.expectNone((r) => r.method === 'POST');
    },
  );
});
