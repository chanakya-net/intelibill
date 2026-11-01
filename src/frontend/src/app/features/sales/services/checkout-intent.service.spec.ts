import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import {
  CheckoutIntent,
  CheckoutIntentIndexedDbService,
  CheckoutIntentUpdate,
} from '../../../core/storage/checkout-intent-indexeddb.service';
import { CheckoutIntentService, classifyCheckoutSendFailure } from './checkout-intent.service';
import type { RecordSaleRequest, SaleDto } from './sale.models';
import { SaleService } from './sale.service';

class InMemoryCheckoutIntentStore {
  readonly records = new Map<string, CheckoutIntent>();
  failWrites = false;

  async add(intent: CheckoutIntent): Promise<void> {
    if (this.failWrites) throw new Error('QuotaExceededError');
    this.records.set(intent.intentId, structuredClone(intent));
  }

  async get(intentId: string): Promise<CheckoutIntent | null> {
    return this.records.get(intentId) ?? null;
  }

  async listByScope(userId: string, shopId: string): Promise<readonly CheckoutIntent[]> {
    return [...this.records.values()].filter((intent) => intent.userId === userId && intent.shopId === shopId);
  }

  async update(intentId: string, update: CheckoutIntentUpdate): Promise<CheckoutIntent | null> {
    const current = this.records.get(intentId);
    if (!current) return null;
    const next = update(current);
    if (!next) return null;
    this.records.set(intentId, next);
    return next;
  }
}

const scope = { userId: 'user-1', shopId: 'shop-1' };

function makeRequest(overrides: Partial<RecordSaleRequest> = {}): RecordSaleRequest {
  return {
    idempotencyKey: 'key-1',
    customerId: 'customer-1',
    customerName: 'Asha',
    customerPhone: '+919000000001',
    paymentMethod: 4,
    paidAmount: 50,
    dueAmount: 68,
    items: [
      {
        barcode: 'SKU-1',
        batchNumber: 'B1',
        itemName: 'Item',
        quantity: 1,
        costPrice: 80,
        salesPrice: 100,
        mrp: 120,
        taxRatePercent: 18,
        isPriceIncludingTax: false,
        inventoryBatchId: 'batch-1',
        clientLineKey: 'line-1',
        itemDiscount: { type: 1, value: 5 },
        hsnCode: null,
        lineType: 'Goods',
      },
    ],
    saleDiscount: { type: 0, value: 0 },
    creditNoteAppliedAmount: 0,
    creditNoteRedemptions: [],
    ...overrides,
  };
}

const sale = { saleId: 'sale-1', invoiceNumber: 'INV-1' } as SaleDto;

describe('CheckoutIntentService', () => {
  let store: InMemoryCheckoutIntentStore;
  let saleService: { recordSale: ReturnType<typeof vi.fn>; getSaleByIdempotencyKey: ReturnType<typeof vi.fn> };
  let service: CheckoutIntentService;

  beforeEach(() => {
    store = new InMemoryCheckoutIntentStore();
    saleService = { recordSale: vi.fn(() => of(sale)), getSaleByIdempotencyKey: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        CheckoutIntentService,
        { provide: CheckoutIntentIndexedDbService, useValue: store },
        { provide: SaleService, useValue: saleService },
      ],
    });
    service = TestBed.inject(CheckoutIntentService);
  });

  afterEach(() => TestBed.resetTestingModule());

  it('prepare_persists_before_transport', async () => {
    const intent = await service.prepare(scope, makeRequest());

    expect(store.records.get(intent.intentId)?.status).toBe('prepared');
    expect(saleService.recordSale).not.toHaveBeenCalled();
    expect(intent.request).toEqual(makeRequest());
    expect(intent).toMatchObject({ userId: 'user-1', shopId: 'shop-1', idempotencyKey: 'key-1' });
  });

  it('storage_failure_sends_nothing', async () => {
    store.failWrites = true;

    await expect(service.prepare(scope, makeRequest())).rejects.toThrow();

    expect(store.records.size).toBe(0);
    expect(saleService.recordSale).not.toHaveBeenCalled();
  });

  it('freezes the request so later cart edits cannot change what is resent', async () => {
    const request = makeRequest();
    const intent = await service.prepare(scope, request);
    (request.items[0] as { quantity: number }).quantity = 9;

    expect(() => {
      (intent.request.items[0] as { quantity: number }).quantity = 9;
    }).toThrow();
    expect(intent.request.items[0].quantity).toBe(1);

    await firstValueFrom(service.send(intent));
    expect(saleService.recordSale).toHaveBeenCalledWith(intent.request);
    expect(saleService.recordSale.mock.calls[0][0].items[0].quantity).toBe(1);
  });

  it('refuses to prepare without a full user/shop scope', async () => {
    await expect(service.prepare({ userId: '', shopId: 'shop-1' }, makeRequest())).rejects.toThrow();
    expect(store.records.size).toBe(0);
  });

  it('lets only one tab claim an intent while its send lease is live', async () => {
    const intent = await service.prepare(scope, makeRequest());

    const first = await service.claimForSend(intent.intentId);
    const second = await service.claimForSend(intent.intentId);

    expect(first?.status).toBe('sending');
    expect(second).toBeNull();
  });

  it('allows an uncertain intent to be claimed again with its original request', async () => {
    const intent = await service.prepare(scope, makeRequest());
    await service.claimForSend(intent.intentId);
    await service.markUncertain(intent.intentId);

    const reclaimed = await service.claimForSend(intent.intentId);

    expect(reclaimed?.status).toBe('sending');
    expect(reclaimed?.request).toEqual(intent.request);
    expect(reclaimed?.idempotencyKey).toBe('key-1');
  });

  it('never claims a confirmed intent again', async () => {
    const intent = await service.prepare(scope, makeRequest());
    await service.confirm(intent.intentId, sale);

    expect(await service.claimForSend(intent.intentId)).toBeNull();
    expect(store.records.get(intent.intentId)).toMatchObject({ status: 'confirmed', saleId: 'sale-1', invoiceNumber: 'INV-1' });
  });

  it('keeps a confirmation from being downgraded by a late rejection', async () => {
    const intent = await service.prepare(scope, makeRequest());
    await service.confirm(intent.intentId, sale);

    expect(await service.reject(intent.intentId, 'Sale.Invalid')).toBeNull();
    expect(store.records.get(intent.intentId)?.status).toBe('confirmed');
  });

  it('loads only unresolved intents for the given scope', async () => {
    const mine = await service.prepare(scope, makeRequest({ idempotencyKey: 'a' }));
    const confirmed = await service.prepare(scope, makeRequest({ idempotencyKey: 'b' }));
    await service.confirm(confirmed.intentId, sale);
    await service.prepare({ userId: 'user-2', shopId: 'shop-1' }, makeRequest({ idempotencyKey: 'c' }));
    await service.prepare({ userId: 'user-1', shopId: 'shop-2' }, makeRequest({ idempotencyKey: 'd' }));

    const unresolved = await service.loadUnresolved(scope);

    expect(unresolved.map((intent) => intent.intentId)).toEqual([mine.intentId]);
  });

  it('recover confirms the intent when the server already committed it', async () => {
    const intent = await service.prepare(scope, makeRequest());
    await service.markUncertain(intent.intentId);
    saleService.getSaleByIdempotencyKey.mockReturnValue(of(sale));

    const recovery = await firstValueFrom(service.recover(scope, intent.intentId));

    expect(recovery.kind).toBe('replayed');
    expect(saleService.getSaleByIdempotencyKey).toHaveBeenCalledWith('key-1');
    expect(store.records.get(intent.intentId)?.status).toBe('confirmed');
    expect(saleService.recordSale).not.toHaveBeenCalled();
  });

  it('recover leaves the intent pending when no sale is visible yet', async () => {
    const intent = await service.prepare(scope, makeRequest());
    await service.markUncertain(intent.intentId);
    saleService.getSaleByIdempotencyKey.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));

    const recovery = await firstValueFrom(service.recover(scope, intent.intentId));

    expect(recovery.kind).toBe('pending');
    expect(store.records.get(intent.intentId)?.status).toBe('uncertain');
  });

  it('recover refuses an intent from another scope', async () => {
    const intent = await service.prepare(scope, makeRequest());

    await expect(firstValueFrom(service.recover({ userId: 'user-1', shopId: 'shop-2' }, intent.intentId))).rejects.toThrow();
    expect(saleService.getSaleByIdempotencyKey).not.toHaveBeenCalled();
  });
});

describe('classifyCheckoutSendFailure', () => {
  it.each([0, 408, 429, 500, 502, 503, 504])('treats HTTP %s as uncertain', (status) => {
    expect(classifyCheckoutSendFailure(new HttpErrorResponse({ status }))).toBe('uncertain');
  });

  it.each([400, 401, 403, 404, 409, 422])('treats HTTP %s as a definitive rejection', (status) => {
    expect(classifyCheckoutSendFailure(new HttpErrorResponse({ status }))).toBe('rejected');
  });

  it('treats a non-HTTP failure as uncertain', () => {
    expect(classifyCheckoutSendFailure(new Error('timeout'))).toBe('uncertain');
  });
});
