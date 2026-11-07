import { HttpErrorResponse } from '@angular/common/http';
import { IDBFactory } from 'fake-indexeddb';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SupplierReturnDraftService } from './supplier-return-draft.service';
const owner = { userId: 'u', shopId: 's' };
const draft = {
  actionKind: 'shipment' as const,
  payload: {
    supplierId: 'supplier',
    reason: 'Damaged goods',
    creditAccepted: false,
    supplierReference: null,
    lines: [
      {
        receiptValueId: 'receipt',
        expectedReceiptVersion: 1,
        expectedBatchVersion: 1,
        quantity: 3,
        proposedCreditAmount: 30,
      },
    ],
  },
};
describe('SupplierReturnDraftService', () => {
  let service: SupplierReturnDraftService;
  beforeEach(() => {
    vi.stubGlobal('indexedDB', new IDBFactory());
    service = new SupplierReturnDraftService();
  });
  afterEach(() => vi.unstubAllGlobals());
  it('freezes exact request before reload and keeps it after uncertain lookup404', async () => {
    const a = await service.prepare(owner, 'shipment:r', draft, 0);
    const key = a.pending!.operationKey;
    await service.settleFailure(
      owner,
      'shipment:r',
      key,
      new HttpErrorResponse({ status: 0 }),
      false,
    );
    await service.settleFailure(
      owner,
      'shipment:r',
      key,
      new HttpErrorResponse({ status: 404 }),
      true,
    );
    const reloaded = await new SupplierReturnDraftService().load(owner, 'shipment:r');
    expect(reloaded!.pending!.payload).toEqual(a.pending!.payload);
    expect(reloaded!.pending!.status).toBe('unknown');
    await expect(service.save(owner, 'shipment:r', draft, reloaded!.revision)).rejects.toThrow();
  });
  it('initial validation preserves editable fields', async () => {
    const a = await service.prepare(owner, 'shipment:r', draft, 0);
    const result = await service.settleFailure(
      owner,
      'shipment:r',
      a.pending!.operationKey,
      new HttpErrorResponse({ status: 400 }),
      false,
    );
    expect(result.record!.pending).toBeNull();
    expect(result.record!.draft).toEqual(draft);
  });
  it('later validation cannot unlock a previously uncertain operation', async () => {
    const a = await service.prepare(owner, 'shipment:r', draft, 0);
    await service.markUnknown(owner, 'shipment:r', a.pending!.operationKey);
    const result = await service.settleFailure(
      owner,
      'shipment:r',
      a.pending!.operationKey,
      new HttpErrorResponse({ status: 400 }),
      true,
    );
    expect(result.record!.pending!.status).toBe('unknown');
  });
  it('two tabs claim one request and refuse stale saves', async () => {
    const [a, b] = await Promise.all([
      service.prepare(owner, 'shipment:r', draft, 0),
      service.prepare(
        owner,
        'shipment:r',
        { ...draft, payload: { ...draft.payload, reason: 'different' } },
        0,
      ),
    ]);
    expect(a.pending!.payload).toEqual(b.pending!.payload);
    await expect(service.save(owner, 'shipment:r', draft, 0)).rejects.toThrow();
  });
  it('confirmation survives another tab cleaning the draft', async () => {
    const a = await service.prepare(owner, 'shipment:r', draft, 0);
    const key = a.pending!.operationKey;
    await service.settle(owner, 'shipment:r', key, 'confirmed');
    await service.clearConfirmed(owner, 'shipment:r', key);
    expect(await service.isConfirmed(owner, 'shipment:r', key)).toBe(true);
    await expect(
      service.claimRecovery(owner, 'shipment:r', key, a.pending!.payload),
    ).rejects.toThrow();
  });
  it('isolates shops users and action identity', async () => {
    await service.save(owner, 'shipment:r', draft, 0);
    expect(await service.load({ ...owner, shopId: 'other' }, 'shipment:r')).toBeNull();
    expect(await service.load({ ...owner, userId: 'other' }, 'shipment:r')).toBeNull();
    expect(await service.load(owner, 'credit:r')).toBeNull();
  });
  it('storage unavailable refuses prepare before HTTP can send', async () => {
    vi.stubGlobal('indexedDB', undefined);
    await expect(service.prepare(owner, 'shipment:r', draft, 0)).rejects.toThrow();
  });
});
