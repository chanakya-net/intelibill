import { Injectable } from '@angular/core';
import {
  FinancialActionDraftStore,
  type FinancialDraftRecord,
} from '../../../core/services/financial-action-draft-store';
import type { CorrectReceiptCostRequest, CorrectReceiptCostResponse } from './inventory.models';

export interface ReceiptCostCorrectionDraft {
  readonly receiptValueId: string;
  readonly batchId: string;
  readonly batchNumber: string;
  readonly originalTotal: number;
  readonly priorTotal: number;
  readonly request: Omit<CorrectReceiptCostRequest, 'operationKey'>;
}

export type ReceiptCostDraftRecord = FinancialDraftRecord<ReceiptCostCorrectionDraft, CorrectReceiptCostRequest>;

export const RECEIPT_COST_CORRECTION = 'receipt-cost-correction';

export function receiptCostTarget(receiptId: string, version: number): string {
  return `${receiptId}:${version}`;
}

export function receiptCostPaise(value: unknown): number | null {
  if (typeof value !== 'number' || !Number.isFinite(value)) return null;
  const paise = Math.round(value * 100);
  return Number.isSafeInteger(paise) && paise / 100 === value ? paise : null;
}

function usableId(value: unknown): boolean {
  return typeof value === 'string' && /^[\da-f]{8}(?:-[\da-f]{4}){3}-[\da-f]{12}$/i.test(value) &&
    value !== '00000000-0000-0000-0000-000000000000';
}

export function matchesReceiptCostOutcome(
  draft: ReceiptCostCorrectionDraft,
  result: CorrectReceiptCostResponse | null | undefined,
): result is CorrectReceiptCostResponse {
  const prior = receiptCostPaise(draft.priorTotal);
  const revised = receiptCostPaise(draft.request.revisedTotalCost);
  return !!result && prior !== null && revised !== null && prior !== revised &&
    result.receiptValueId === draft.receiptValueId && result.batchId === draft.batchId &&
    usableId(result.adjustmentId) && usableId(result.supplierLedgerEntryId) &&
    Number.isInteger(result.receiptVersion) && result.receiptVersion > 0 &&
    result.receiptVersion <= 0xffffffff && result.receiptVersion !== draft.request.expectedVersion &&
    typeof result.performedAt === 'string' && Number.isFinite(Date.parse(result.performedAt)) &&
    receiptCostPaise(result.priorTotalCost) === prior && receiptCostPaise(result.revisedTotalCost) === revised &&
    receiptCostPaise(result.delta) === revised - prior;
}

@Injectable({ providedIn: 'root' })
export class ReceiptCostCorrectionDraftService extends FinancialActionDraftStore<
  ReceiptCostCorrectionDraft, CorrectReceiptCostRequest
> {
  constructor() {
    super({
      diagnosticName: 'ReceiptCostCorrection',
      namespace: 'intelibill-receipt-cost-corrections',
      conflict: () => new Error('Receipt cost correction changed in another tab or is frozen.'),
      payload: (draft, operationKey) => ({ ...draft.request, operationKey }),
      externalKey: () => `receipt-cost-${crypto.randomUUID()}`,
      pendingPrefix: (targetId) => targetId.slice(0, targetId.lastIndexOf(':') + 1),
      notify: true,
    });
  }
}
