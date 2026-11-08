import { ShopContextService } from '../../../core/context/shop-context.service';
import { REQUEST_SCOPE } from '../../../core/context/scoped-request';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { IDBFactory } from 'fake-indexeddb';
import { beforeEach, afterEach, describe, it, expect, vi } from 'vitest';
import { firstValueFrom } from 'rxjs';
import { TillService } from './till.service';
import { TillDraftService } from './till-draft.service';
const owner = { userId: 'u', shopId: 's' };
const tick = () => new Promise((r) => setTimeout(r, 15));
describe('TillService durable HTTP boundary', () => {
  let api: TillService, http: HttpTestingController, drafts: TillDraftService;
  beforeEach(() => {
    vi.stubGlobal('indexedDB', new IDBFactory());
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(ShopContextService).initialize(owner);
    api = TestBed.inject(TillService);
    http = TestBed.inject(HttpTestingController);
    drafts = TestBed.inject(TillDraftService);
  });
  afterEach(() => {
    http.verify();
    vi.unstubAllGlobals();
    TestBed.resetTestingModule();
  });
  it('persists exact session evidence and owner before POST; timeout and lookup404 retry same key', async () => {
    const body = { till: { sessionId: 'drawer' }, countedCash: 100, expectedVersion: 1 };
    const first = firstValueFrom(
      api.execute(owner, 'close', 'drawer', '1', body, '/tills/sessions/drawer/close'),
    ).catch((e) => e);
    await tick();
    const req = await vi.waitFor(() => http.expectOne((r) => r.url.endsWith('/drawer/close')));
    const key = req.request.body.operationKey;
    expect((await drafts.list(owner))[0].pending?.payload['till']).toEqual(body.till);
    req.error(new ProgressEvent('error'));
    await first;
    const second = firstValueFrom(api.execute(owner, 'close', 'drawer', '2', body, '/wrong'));
    await tick();
    (await vi.waitFor(() => http.expectOne((r) => r.url.includes('/operations/close/')))).flush(
      {},
      { status: 404, statusText: 'Not Found' },
    );
    await tick();
    const retry = await vi.waitFor(() => http.expectOne((r) => r.url.endsWith('/drawer/close')));
    expect(retry.request.body.operationKey).toBe(key);
    expect(retry.request.body.countedCash).toBe(100);
    retry.flush({ id: 'saved' });
    expect(await second).toEqual({ id: 'saved' });
  });
  it('storage failure performs zero HTTP', async () => {
    vi.stubGlobal('indexedDB', undefined);
    await expect(
      firstValueFrom(api.execute(owner, 'create', 's', '1', { name: 'Till' }, '/tills')),
    ).rejects.toThrow();
    http.expectNone(() => true);
  });
  it('uses distinct version identities for later cash movements while terminal proof blocks a stale editor', async () => {
    const send = async (cycle: string) => {
      const result = firstValueFrom(
        api.execute(
          owner,
          'cash-movement',
          'd',
          cycle,
          { amount: 10 },
          '/tills/sessions/d/cash-movements',
        ),
      );
      await tick();
      (await vi.waitFor(() => http.expectOne((r) => r.url.endsWith('/cash-movements')))).flush({
        id: 'd',
      });
      await result;
    };
    await send('1');
    await send('2');
    expect((await drafts.list(owner)).length).toBe(2);
    await expect(
      firstValueFrom(
        api.execute(
          owner,
          'cash-movement',
          'd',
          '1',
          { amount: 10 },
          '/tills/sessions/d/cash-movements',
        ),
      ),
    ).rejects.toThrow();
  });
  it('captures the request owner before awaiting persistence', async () => {
    const mutable = { ...owner };
    const result = firstValueFrom(
      api.execute(mutable, 'close', 'd', 'close', { countedCash: 1 }, '/tills/sessions/d/close'),
    );
    mutable.shopId = 'new-shop';
    await tick();
    const req = await vi.waitFor(() => http.expectOne((r) => r.url.endsWith('/d/close')));
    expect(req.request.context.get(REQUEST_SCOPE)).toEqual(owner);
    expect((await drafts.list(owner))[0]?.pending?.payload['countedCash']).toBe(1);
    req.flush({ id: 'd' });
    await result;
  });
  it('blocks a changed cash action until explicit recovery shows and replays the old request', async () => {
    const original = { direction: 1, amount: 200, reason: 'Float transfer', expectedVersion: 1 };
    const first = firstValueFrom(
      api.execute(owner, 'cash-movement', 'd', '1', original, '/tills/sessions/d/cash-movements'),
    ).catch((e) => e);
    const sent = await vi.waitFor(() => http.expectOne((r) => r.url.endsWith('/cash-movements')));
    const key = sent.request.body.operationKey;
    sent.error(new ProgressEvent('lost'));
    await first;
    await expect(
      firstValueFrom(
        api.execute(
          owner,
          'cash-movement',
          'd',
          '2',
          { direction: -1, amount: 300, reason: 'New payment', expectedVersion: 2 },
          '/tills/sessions/d/cash-movements',
        ),
      ),
    ).rejects.toMatchObject({
      message: 'tills.pendingRecovery',
      operationKey: key,
      frozenPayload: expect.objectContaining(original),
    });
    http.expectNone(() => true);
    const recovered = firstValueFrom(
      api.execute(owner, 'cash-movement', 'd', '2', {}, '/different', false, undefined, true),
    );
    (
      await vi.waitFor(() => http.expectOne((r) => r.url.includes('/operations/cash-movement/')))
    ).flush({}, { status: 404, statusText: 'Not Found' });
    const replay = await vi.waitFor(() => http.expectOne((r) => r.url.endsWith('/cash-movements')));
    expect(replay.request.body).toMatchObject({ ...original, operationKey: key });
    replay.flush({ id: 'd' });
    await recovered;
  });
  it('has zero HTTP while durable preparation is still uncommitted', async () => {
    let release!: () => void;
    const barrier = new Promise<void>((r) => (release = r));
    const real = drafts.prepare.bind(drafts);
    vi.spyOn(drafts, 'prepare').mockImplementation(async (...args) => {
      await barrier;
      return real(...args);
    });
    const result = firstValueFrom(
      api.execute(owner, 'create', 's', 'intent', { name: 'Front' }, '/tills'),
    );
    await tick();
    http.expectNone(() => true);
    release();
    const req = await vi.waitFor(() => http.expectOne((r) => r.url.endsWith('/tills')));
    req.flush({ id: 't' });
    await result;
  });
});
