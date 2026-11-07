import { HttpErrorResponse } from '@angular/common/http';
import { IDBFactory, IDBObjectStore } from 'fake-indexeddb';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SaleCancellationDraftService } from './sale-cancellation-draft.service';
const owner = { userId: 'u', shopId: 's' };
const draft = {
  payload: {
    expectedVersion: 7,
    reason: 'Duplicate sale',
    goodsRecoveredConfirmed: true,
    cashRefundConfirmed: true,
  },
};
describe('SaleCancellationDraftService', () => {
  let service: SaleCancellationDraftService;
  beforeEach(() => {
    vi.stubGlobal('indexedDB', new IDBFactory());
    service = new SaleCancellationDraftService();
  });
  afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });
  it('freezes exact request before reload and keeps it after uncertain lookup404', async () => {
    const a = await service.prepare(owner, 'sale-r', draft, 0);
    const key = a.pending!.operationKey;
    await service.settleFailure(owner, 'sale-r', key, new HttpErrorResponse({ status: 0 }), false);
    await service.settleFailure(owner, 'sale-r', key, new HttpErrorResponse({ status: 404 }), true);
    const reloaded = await new SaleCancellationDraftService().load(owner, 'sale-r');
    expect(reloaded!.pending!.payload).toEqual(a.pending!.payload);
    expect(reloaded!.pending!.status).toBe('unknown');
    await expect(service.save(owner, 'sale-r', draft, reloaded!.revision)).rejects.toThrow();
  });
  it('initial validation preserves editable fields', async () => {
    const a = await service.prepare(owner, 'sale-r', draft, 0);
    const result = await service.settleFailure(
      owner,
      'sale-r',
      a.pending!.operationKey,
      new HttpErrorResponse({ status: 400 }),
      false,
    );
    expect(result.record!.pending).toBeNull();
    expect(result.record!.draft).toEqual(draft);
  });
  it('later validation cannot unlock a previously uncertain operation', async () => {
    const a = await service.prepare(owner, 'sale-r', draft, 0);
    await service.markUnknown(owner, 'sale-r', a.pending!.operationKey);
    const result = await service.settleFailure(
      owner,
      'sale-r',
      a.pending!.operationKey,
      new HttpErrorResponse({ status: 400 }),
      true,
    );
    expect(result.record!.pending!.status).toBe('unknown');
  });
  it('two tabs claim one request and refuse stale saves', async () => {
    const [a, b] = await Promise.all([
      service.prepare(owner, 'sale-r', draft, 0),
      new SaleCancellationDraftService().prepare(
        owner,
        'sale-r',
        { ...draft, payload: { ...draft.payload, reason: 'different' } },
        0,
      ),
    ]);
    expect(a.pending!.payload).toEqual(b.pending!.payload);
    await expect(service.save(owner, 'sale-r', draft, 0)).rejects.toThrow();
  });
  it('confirmation survives another tab cleaning the draft', async () => {
    const a = await service.prepare(owner, 'sale-r', draft, 0);
    const key = a.pending!.operationKey;
    await service.settle(owner, 'sale-r', key, 'confirmed');
    await service.clearConfirmed(owner, 'sale-r', key);
    expect(await service.isConfirmed(owner, 'sale-r', key)).toBe(true);
    await expect(service.claimRecovery(owner, 'sale-r', key, a.pending!.payload)).rejects.toThrow();
  });
  it('isolates shops users and action identity', async () => {
    await service.save(owner, 'sale-r', draft, 0);
    expect(await service.load({ ...owner, shopId: 'other' }, 'sale-r')).toBeNull();
    expect(await service.load({ ...owner, userId: 'other' }, 'sale-r')).toBeNull();
    expect(await service.load(owner, 'sale-other')).toBeNull();
  });
  it('storage unavailable refuses prepare before HTTP can send', async () => {
    vi.stubGlobal('indexedDB', undefined);
    await expect(service.prepare(owner, 'sale-r', draft, 0)).rejects.toThrow();
  });
  it.each([403, 409])('keeps uncertainty after later refusal %s', async (status) => {
    const a = await service.prepare(owner, 'sale-r', draft, 0);
    await service.markUnknown(owner, 'sale-r', a.pending!.operationKey);
    const failed = await service.settleFailure(
      owner,
      'sale-r',
      a.pending!.operationKey,
      new HttpErrorResponse({ status }),
      false,
    );
    expect(failed.record!.pending!.operationKey).toBe(a.pending!.operationKey);
    expect(failed.outcome).toBe('unknown');
    await expect(service.discard(owner, 'sale-r', failed.record!.revision)).rejects.toThrow();
  });
  it('does not let stale responses clear a replacement operation', async () => {
    const a = await service.prepare(owner, 'sale-r', draft, 0);
    const rejected = await service.settle(owner, 'sale-r', a.pending!.operationKey, 'rejected');
    const b = await service.prepare(owner, 'sale-r', draft, rejected!.revision);
    await service.settle(owner, 'sale-r', a.pending!.operationKey, 'confirmed');
    await service.settleFailure(
      owner,
      'sale-r',
      a.pending!.operationKey,
      new HttpErrorResponse({ status: 400 }),
      false,
    );
    expect((await service.load(owner, 'sale-r'))?.pending?.operationKey).toBe(
      b.pending!.operationKey,
    );
  });
  it('freezes the payload independently of later caller edits', async () => {
    const editable = structuredClone(draft);
    const prepared = await service.prepare(owner, 'sale-r', editable, 0);
    editable.payload.reason = 'Changed in memory';
    expect(prepared.pending!.payload.reason).toBe('Duplicate sale');
    expect((await service.load(owner, 'sale-r'))?.pending?.payload.reason).toBe('Duplicate sale');
  });
  it('rejects blocked storage before a claim can be used', async () => {
    const request = {} as IDBOpenDBRequest;
    vi.stubGlobal('indexedDB', {
      open: () => {
        queueMicrotask(() => request.onblocked?.({} as IDBVersionChangeEvent));
        return request;
      },
    });
    await expect(service.prepare(owner, 'sale-r', draft, 0)).rejects.toThrow('blocked');
  });
 it('aborts an intent when durable storage runs out of quota',async()=>{const write=vi.spyOn(IDBObjectStore.prototype,'put').mockImplementation(()=>{throw new DOMException('Quota exceeded','QuotaExceededError');});await expect(service.prepare(owner,'sale-r',draft,0)).rejects.toThrow('Quota exceeded');write.mockRestore();expect(await service.load(owner,'sale-r')).toBeNull();});
});
