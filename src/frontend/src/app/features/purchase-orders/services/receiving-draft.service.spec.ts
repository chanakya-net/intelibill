import { HttpErrorResponse } from '@angular/common/http';
import { IDBFactory, IDBDatabase as FakeDatabase } from 'fake-indexeddb';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ReceivingDraftService } from './receiving-draft.service';
import { ReceivingEditableDraft } from './receiving.models';
const owner = { userId: 'user-1', shopId: 'shop-1' };
const draft: ReceivingEditableDraft = {
  referenceNumber: ' reference ',
  notes: 'notes',
  receivedAt: null,
  lines: [
    {
      clientLineId: 'bc9f45c9-f768-4f52-a48b-b5832f571619',
      purchaseOrderLineId: 'line-1',
      barcode: 'product',
      batchNumber: 'lot A',
      quantity: 4,
      totalPurchaseCost: 20,
      mrp: 8,
      salesPrice: 7,
      taxRatePercent: 5,
      taxIncluded: true,
      purchaseTaxIncluded: false,
      expiryDate: '2027-05-21',
      manufacturingDate: null,
    },
  ],
};
describe('ReceivingDraftService', () => {
  let service: ReceivingDraftService;
  beforeEach(() => {
    vi.stubGlobal('indexedDB', new IDBFactory());
    service = new ReceivingDraftService();
  });
  afterEach(() => vi.unstubAllGlobals());
  it('retains every editable value and stable row identity across reload', async () => {
    await service.save(owner, 'po-1', draft, 0);
    expect((await new ReceivingDraftService().load(owner, 'po-1'))?.draft).toEqual(draft);
  });
  it('isolates user, shop and PO keys', async () => {
    await service.save(owner, 'po-1', draft, 0);
    expect(await service.load({ ...owner, userId: 'other' }, 'po-1')).toBeNull();
    expect(await service.load({ ...owner, shopId: 'other' }, 'po-1')).toBeNull();
    expect(await service.load(owner, 'po-2')).toBeNull();
  });
  it('claims one frozen request across simultaneous prepares and refuses stale/pending saves', async () => {
    const [a, b] = await Promise.all([
      service.prepare(owner, 'po-1', draft, 0),
      new ReceivingDraftService().prepare(
        owner,
        'po-1',
        { ...draft, notes: 'changed defaults' },
        0,
      ),
    ]);
    expect(a.pending!.payload).toEqual(b.pending!.payload);
    expect(a.pending!.operationId).toBe(b.pending!.operationId);
    expect(b.pending!.status).toBe('unknown');
    await expect(service.save(owner, 'po-1', draft, a.revision)).rejects.toThrow();
    await service.markUnknown(owner, 'po-1', a.pending!.operationId);
    const replay = await new ReceivingDraftService().prepare(
      owner,
      'po-1',
      { ...draft, lines: [] },
      0,
    );
    expect(JSON.stringify(replay.pending!.payload)).toBe(JSON.stringify(a.pending!.payload));
    await expect(service.discard(owner, 'po-1', replay.revision)).rejects.toThrow();
  });
  it('rejects stale editor revisions without losing the saved fields, including invalid input', async () => {
    const invalid = { ...draft, lines: [{ ...draft.lines[0], quantity: null }] };
    await service.save(owner, 'po-1', invalid, 0);
    await expect(service.save(owner, 'po-1', draft, 0)).rejects.toThrow();
    expect((await service.load(owner, 'po-1'))!.draft).toEqual(invalid);
  });
  it('settles only the matching operation; rejection keeps editable input; confirmation tombstones it', async () => {
    const prepared = await service.prepare(owner, 'po-1', draft, 0);
    await service.settle(owner, 'po-1', 'unrelated', 'confirmed');
    expect((await service.load(owner, 'po-1'))!.pending).toEqual(prepared.pending);
    await service.settle(owner, 'po-1', prepared.pending!.operationId, 'rejected');
    const rejected = (await service.load(owner, 'po-1'))!;
    expect(rejected.pending).toBeNull();
    expect(rejected.draft).toEqual(draft);
    const next = await service.prepare(owner, 'po-1', draft, rejected.revision);
    await service.settle(owner, 'po-1', next.pending!.operationId, 'confirmed');
    const tombstone = (await new ReceivingDraftService().load(owner, 'po-1'))!;
    expect(tombstone.confirmedOperationId).toBe(next.pending!.operationId);
    await expect(service.prepare(owner, 'po-1', draft, tombstone.revision)).rejects.toThrow();
    await expect(service.save(owner, 'po-1', draft, tombstone.revision)).rejects.toThrow();
  });
  it('waits for transaction completion before returning the frozen request', async () => {
    const nativeOpen = indexedDB.open.bind(indexedDB);

    let completed = false;
    vi.spyOn(indexedDB, 'open').mockImplementation((...args: Parameters<IDBFactory['open']>) => {
      const req = nativeOpen(...args);
      req.addEventListener('success', () => {
        const nativeTransaction = req.result.transaction.bind(req.result);
        vi.spyOn(req.result, 'transaction').mockImplementation(
          (...txArgs: Parameters<IDBDatabase['transaction']>) => {
            const transaction = nativeTransaction(...txArgs);
            transaction.addEventListener('complete', () => {
              completed = true;
            });
            return transaction;
          },
        );
      });
      return req;
    });
    await service.prepare(owner, 'po-1', draft, 0);
    expect(completed).toBe(true);
  });
  it('fails closed when storage is unavailable', async () => {
    vi.stubGlobal('indexedDB', undefined);
    await expect(service.prepare(owner, 'po-1', draft, 0)).rejects.toThrow('unavailable');
  });
  it('rejects aborted prepares and preserves a confirmed tombstone when cleanup fails', async () => {
    const saved = await service.save(owner, 'po-1', draft, 0);
    const native = FakeDatabase.prototype.transaction;
    const abortWrites = () =>
      vi.spyOn(FakeDatabase.prototype, 'transaction').mockImplementation(function (
        this: IDBDatabase,
        ...args: Parameters<IDBDatabase['transaction']>
      ) {
        const tx = native.apply(this, args);
        if (args[1] === 'readwrite') queueMicrotask(() => tx.abort());
        return tx;
      });
    let failing = abortWrites();
    await expect(service.prepare(owner, 'po-1', draft, saved.revision)).rejects.toThrow();
    failing.mockRestore();
    expect((await service.load(owner, 'po-1'))!.pending).toBeNull();
    const prepared = await service.prepare(owner, 'po-1', draft, saved.revision);
    await service.settle(owner, 'po-1', prepared.pending!.operationId, 'confirmed');
    const confirmed = (await service.load(owner, 'po-1'))!;
    failing = abortWrites();
    await expect(service.discard(owner, 'po-1', confirmed.revision)).rejects.toThrow();
    failing.mockRestore();
    await expect(service.prepare(owner, 'po-1', draft, confirmed.revision)).rejects.toThrow();
    expect((await service.load(owner, 'po-1'))!.confirmedOperationId).toBe(
      prepared.pending!.operationId,
    );
  });
  it('classifies failure and updates the current unknown record inside one transaction; unmatched settlement is a no-op', async () => {
    const first = await service.prepare(owner, 'po-1', draft, 0);
    const other = new ReceivingDraftService();
    await other.prepare(owner, 'po-1', draft, 0);
    const failed = await service.settleFailure(
      owner,
      'po-1',
      first.pending!.operationId,
      new HttpErrorResponse({ status: 401 }),
      false,
    );
    expect(failed.outcome).toBe('unknown');
    expect(failed.matched).toBe(true);
    expect(failed.record!.pending!.operationId).toBe(first.pending!.operationId);
    const before = await service.load(owner, 'po-1');
    expect(await service.settle(owner, 'po-1', 'unrelated', 'confirmed')).toEqual(before);
    expect(await service.isConfirmed(owner, 'po-1', 'unrelated')).toBe(false);
    expect(await service.clearConfirmed(owner, 'po-1', 'unrelated')).toBe(false);
    expect(
      await service.settleFailure(
        owner,
        'po-1',
        'unrelated',
        new HttpErrorResponse({ status: 400 }),
        false,
      ),
    ).toEqual({ outcome: 'unknown', matched: false, record: before });
    expect(await service.load(owner, 'po-1')).toEqual(before);
  });

  it('retains exact durable proof after another tab cleans the tombstone, without granting authority over a replacement', async () => {
    const prepared = await service.prepare(owner, 'po-1', draft, 0);
    const op = prepared.pending!.operationId;
    const confirmed = await service.settle(owner, 'po-1', op, 'confirmed');
    expect(confirmed!.confirmedOperationId).toBe(op);
    expect(await new ReceivingDraftService().clearConfirmed(owner, 'po-1', op)).toBe(true);
    expect(await service.load(owner, 'po-1')).toBeNull();
    expect(await service.isConfirmed(owner, 'po-1', op)).toBe(true);
    const replacement = await service.save(owner, 'po-1', { ...draft, notes: 'new work' }, 0);
    expect(await service.clearConfirmed(owner, 'po-1', op)).toBe(false);
    expect(await service.settle(owner, 'po-1', op, 'confirmed')).toEqual(replacement);
    expect((await service.load(owner, 'po-1'))!.draft.notes).toBe('new work');
    expect(await service.isConfirmed({ ...owner, shopId: 'other-shop' }, 'po-1', op)).toBe(false);
  });
  it('claims recovery only for the exact current operation and frozen payload, refusing vanished/replaced work', async () => {
    const prepared = await service.prepare(owner, 'po-1', draft, 0);
    const pending = prepared.pending!;
    await expect(
      service.claimRecovery(owner, 'po-1', 'wrong-operation', pending.payload),
    ).rejects.toThrow();
    await expect(
      service.claimRecovery(owner, 'po-1', pending.operationId, {
        ...pending.payload,
        notes: 'changed',
      }),
    ).rejects.toThrow();
    await expect(
      service.claimRecovery(owner, 'different-po', pending.operationId, pending.payload),
    ).rejects.toThrow();
    expect(await service.load(owner, 'po-1')).toEqual(prepared);
    const claimed = await service.claimRecovery(
      owner,
      'po-1',
      pending.operationId,
      pending.payload,
    );
    expect(claimed.pending!.status).toBe('unknown');
    expect(claimed.pending!.payload).toEqual(pending.payload);
    await service.settle(owner, 'po-1', pending.operationId, 'rejected');
    const rejected = (await service.load(owner, 'po-1'))!;
    const replacement = await service.save(
      owner,
      'po-1',
      { ...draft, notes: 'replacement' },
      rejected.revision,
    );
    await expect(
      service.claimRecovery(owner, 'po-1', pending.operationId, pending.payload),
    ).rejects.toThrow();
    expect(await service.load(owner, 'po-1')).toEqual(replacement);
    await service.discard(owner, 'po-1', replacement.revision);
    await expect(
      service.claimRecovery(owner, 'po-1', pending.operationId, pending.payload),
    ).rejects.toThrow();
    expect(await service.load(owner, 'po-1')).toBeNull();
  });
});
