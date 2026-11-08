import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { IDBFactory } from 'fake-indexeddb';
import { beforeEach, afterEach, describe, it, expect, vi } from 'vitest';
import { firstValueFrom } from 'rxjs';
import { SaleService } from '../../sales/services/sale.service';
import { RecordSaleReturnRequest } from '../../sales/services/sale.models';
import { TillDraftService } from './till-draft.service';
const owner = { userId: 'u', shopId: 's' };
describe('Exact return/void source recovery', () => {
  let api: SaleService, http: HttpTestingController, drafts: TillDraftService;
  beforeEach(() => {
    vi.stubGlobal('indexedDB', new IDBFactory());
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(SaleService);
    http = TestBed.inject(HttpTestingController);
    drafts = TestBed.inject(TillDraftService);
  });
  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
    vi.unstubAllGlobals();
  });
  const payload = () =>
    ({
      tillOwner: owner,
      till: { sessionId: 'd' },
      operationKey: 'one',
      items: [],
      payoutDestination: null,
      notes: null,
      dueReductionOverrideAmount: null,
      dueOverrideReason: null,
    }) as RecordSaleReturnRequest;
  const one = (http: HttpTestingController, suffix: string) =>
    vi.waitFor(() => http.expectOne((r) => r.url.endsWith(suffix)));
  it('recovers the original typed return identity after a later void; GET current SaleDto is allowed to have no returns', async () => {
    const first = firstValueFrom(api.recordSaleReturn('sale', payload())).catch((e) => e);
    const sent = await one(http, '/sale/returns');
    const key = sent.request.body.operationKey;
    sent.error(new ProgressEvent('lost'));
    await first;
    const retry = firstValueFrom(
      api.recordSaleReturn('sale', { ...payload(), operationKey: 'replacement' }),
    );
    (await one(http, `/sale-return/${key}`)).flush({
      response: { saleReturnId: 'original-return', saleId: 'sale' },
    });
    (await one(http, '/sales/sale')).flush({ saleId: 'sale', returns: [] });
    expect(await retry).toEqual({ saleId: 'sale', returns: [] });
    http.expectNone((r) => r.method === 'POST');
    expect(await drafts.isConfirmed(owner, 'sale-return:sale:one', key)).toBe(true);
  });
  it('accepts void204 without expecting a historical SaleDto and later recovers both exact parent identities', async () => {
    const request = {
      tillOwner: owner,
      tillParentSaleId: 'sale',
      reason: 'No recovery',
      operationKey: 'void',
      settlementEvidence: { disposition: 'NoPhysicalMovement' as const, reason: 'No recovery' },
    };
    const first = firstValueFrom(api.voidSaleReturn('return', request)).catch((e) => e);
    const sent = await one(http, '/return/void');
    const key = sent.request.body.operationKey;
    expect(sent.request.body.__parentSaleId).toBeUndefined();
    sent.error(new ProgressEvent('lost'));
    await first;
    const retry = firstValueFrom(api.voidSaleReturn('return', request));
    (await one(http, `/sale-return-void/${key}`)).flush({
      response: { saleReturnId: 'return', saleId: 'sale' },
    });
    expect(await retry).toBeUndefined();
    http.expectNone((r) => r.method === 'POST');
    const clean = firstValueFrom(
      api.voidSaleReturn('another', { ...request, tillParentSaleId: 'sale' }),
    );
    (await one(http, '/another/void')).flush(null, { status: 204, statusText: 'No Content' });
    expect(await clean).toBeUndefined();
  });
});
