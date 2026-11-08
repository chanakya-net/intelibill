import { ShopContextService } from '../../../core/context/shop-context.service';
import { TillService } from './till.service';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { IDBFactory } from 'fake-indexeddb';
import { beforeEach, afterEach, describe, it, expect, vi } from 'vitest';
import { firstValueFrom } from 'rxjs';
import { TillSourceService } from './till-source.service';
const owner = { userId: 'u', shopId: 's' };
const tick = () => new Promise((r) => setTimeout(r, 15));
describe('TillSourceService', () => {
  let source: TillSourceService, http: HttpTestingController;
  beforeEach(() => {
    vi.stubGlobal('indexedDB', new IDBFactory());
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(ShopContextService).initialize(owner);
    source = TestBed.inject(TillSourceService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    vi.unstubAllGlobals();
    TestBed.resetTestingModule();
  });
  it('keeps disabled legacy body byte shape and performs no durability work', async () => {
    vi.stubGlobal('indexedDB', undefined);
    const body = { amount: 2 };
    const result = firstValueFrom(source.post('/expenses', 'expense-record', 'new', 'one', body));
    (await vi.waitFor(() => http.expectOne((r) => r.url.endsWith('/expenses')))).flush({ id: 'e' });
    expect(await result).toEqual({ id: 'e' });
  });
  it('requires durable owner for till evidence rather than submitting unowned data', async () => {
    await expect(
      firstValueFrom(
        source.post('/expenses', 'expense-record', 'new', 'one', {
          amount: 2,
          till: { sessionId: 'd' },
        }),
      ),
    ).rejects.toThrow();
    http.expectNone(() => true);
  });
  it('freezes enabled source fields before network and strips UI-only owner metadata', async () => {
    const body = {
      amount: 2,
      till: { sessionId: 'd' },
      tenderLine: { method: 'Upi', appliedAmount: 2 },
      tillOwner: owner,
    };
    const result = firstValueFrom(source.post('/expenses', 'expense-record', 'new', 'one', body));
    await tick();
    const req = await vi.waitFor(() => http.expectOne((r) => r.url.endsWith('/expenses')));
    expect(req.request.body.tillOwner).toBeUndefined();
    expect(req.request.body.till).toEqual({ sessionId: 'd' });
    expect(req.request.body.tenderLine).toEqual(body.tenderLine);
    expect(req.request.body.operationKey).toBeTruthy();
    req.flush({ id: 'e' });
    await result;
  });
  it('recovers an unresolved enabled source before a newly disabled form can take a legacy branch', async () => {
    const payload = {
      amount: 2,
      till: { sessionId: 'original' },
      tenderLine: { method: 'Cash', appliedAmount: 2 },
      tillOwner: owner,
    };
    const first = firstValueFrom(
      source.post('/expenses', 'expense-record', 'new', 'a', payload),
    ).catch((e) => e);
    await tick();
    const sent = await vi.waitFor(() => http.expectOne((r) => r.url.endsWith('/expenses')));
    const key = sent.request.body.operationKey;
    sent.error(new ProgressEvent('error'));
    await first;
    await expect(
      firstValueFrom(
        source.post('/expenses', 'expense-record', 'new', 'b', { amount: 999, tillOwner: owner }),
      ),
    ).rejects.toMatchObject({ message: 'tills.pendingRecovery', operationKey: key });
    http.expectNone(() => true);
    const api = TestBed.inject(TillService);
    const retry = firstValueFrom(
      api.execute(owner, 'expense-record', 'new', 'b', {}, '/expenses', true, undefined, true),
    );
    (
      await vi.waitFor(() =>
        http.expectOne((r) => r.url.includes('/financial-operations/expense-record/')),
      )
    ).flush({}, { status: 404, statusText: 'Not Found' });
    const frozen = await vi.waitFor(() => http.expectOne((r) => r.url.endsWith('/expenses')));
    expect(frozen.request.body.amount).toBe(2);
    expect(frozen.request.body.till.sessionId).toBe('original');
    expect(frozen.request.body.operationKey).toBe(key);
    frozen.flush({ id: 'e' });
    await retry;
  });
});
