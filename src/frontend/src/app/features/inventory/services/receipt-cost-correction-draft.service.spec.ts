import { HttpErrorResponse } from '@angular/common/http';
import { IDBFactory } from 'fake-indexeddb';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  ReceiptCostCorrectionDraftService,
  matchesReceiptCostOutcome,
  receiptCostTarget,
} from './receipt-cost-correction-draft.service';

const owner = { userId: 'owner', shopId: 'shop' };
const draft = {
  receiptValueId: 'receipt',
  batchId: 'batch',
  batchNumber: 'LOT',
  originalTotal: 1000.01,
  priorTotal: 1000.01,
  request: { expectedVersion: 7, revisedTotalCost: 1100.02, reason: 'Supplier invoice' },
};
const result = {
  adjustmentId: 'a0000000-0000-4000-8000-000000000001',
  supplierLedgerEntryId: 'a0000000-0000-4000-8000-000000000002',
  receiptValueId: 'receipt',
  batchId: 'batch',
  priorTotalCost: 1000.01,
  revisedTotalCost: 1100.02,
  delta: 100.01,
  receiptVersion: 8,
  performedAt: '2026-11-21T01:00:00Z',
};
const target = receiptCostTarget(draft.receiptValueId, draft.request.expectedVersion);

describe('ReceiptCostCorrectionDraftService', () => {
  let store: ReceiptCostCorrectionDraftService;
  beforeEach(() => {
    vi.stubGlobal('indexedDB', new IDBFactory());
    store = new ReceiptCostCorrectionDraftService();
  });
  afterEach(() => vi.unstubAllGlobals());

  it('freezes the exact wire request and prevents a different version replacing uncertain work', async () => {
    const first = await store.prepare(owner, target, draft, 0);
    expect(first.pending?.payload).toEqual({ ...draft.request, operationKey: first.pending?.operationKey });
    await store.settleFailure(owner, target, first.pending!.operationKey, new HttpErrorResponse({ status: 0 }), false);
    const otherTab = new ReceiptCostCorrectionDraftService();
    const newer = await otherTab.prepare(owner, receiptCostTarget('receipt', 8), {
      ...draft, request: { ...draft.request, expectedVersion: 8, revisedTotalCost: 1050 },
    }, 0);
    expect(newer.targetId).toBe(target);
    expect(newer.pending?.payload).toEqual(first.pending?.payload);
    expect(newer.pending?.status).toBe('unknown');
    expect(await otherTab.list({ ...owner, shopId: 'other' })).toEqual([]);
    expect(await otherTab.list({ ...owner, userId: 'other' })).toEqual([]);
  });

  it('preserves exact confirmation and permits a subsequent observed version', async () => {
    const first = await store.prepare(owner, target, draft, 0);
    const pending = first.pending!;
    await store.settle(owner, target, pending.operationKey, 'confirmed', { response: result });
    await store.clearConfirmed(owner, target, pending.operationKey);
    await expect(store.claimRecovery(owner, target, pending.operationKey, pending.payload)).rejects.toThrow();
    await expect(store.prepare(owner, target, draft, 0)).rejects.toThrow();
    const later = await store.prepare(owner, receiptCostTarget('receipt', 8), {
      ...draft, request: { ...draft.request, expectedVersion: 8, revisedTotalCost: 1050 },
    }, 0);
    expect(later.pending?.operationKey).not.toBe(pending.operationKey);
  });

  it('matches committed facts in integer paise and refuses malformed or mismatched outcomes', () => {
    expect(matchesReceiptCostOutcome(draft, result)).toBe(true);
    for (const update of [
      { adjustmentId: '' }, { supplierLedgerEntryId: '00000000-0000-0000-0000-000000000000' },
      { receiptValueId: 'other' }, { batchId: 'other' }, { priorTotalCost: 1000 },
      { revisedTotalCost: 1100.03 }, { delta: 100.001 }, { delta: 100 }, { receiptVersion: 7 },
      { receiptVersion: 0 }, { performedAt: 'invalid' },
    ]) expect(matchesReceiptCostOutcome(draft, { ...result, ...update })).toBe(false);
  });

  it('refuses to prepare when storage is unavailable', async () => {
    vi.stubGlobal('indexedDB', undefined);
    await expect(store.prepare(owner, target, draft, 0)).rejects.toThrow();
  });
});
