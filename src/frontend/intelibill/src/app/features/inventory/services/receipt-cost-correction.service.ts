import { HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { problemCode } from '@intelibill/api';
import { firstValueFrom } from 'rxjs';
import type { ShopScope } from '../../../core/context/shop-scope';
import { InventoryService } from './inventory.service';
import type { CorrectReceiptCostResponse } from './inventory.models';
import {
  matchesReceiptCostOutcome,
  RECEIPT_COST_CORRECTION,
  ReceiptCostCorrectionDraftService,
  receiptCostTarget,
  type ReceiptCostCorrectionDraft,
  type ReceiptCostDraftRecord,
} from './receipt-cost-correction-draft.service';

export const RECEIPT_VERSION_CONFLICT = 'PurchaseReceipt.VersionConflict';

export type ReceiptCostOutcome =
  | { kind: 'confirmed'; response: CorrectReceiptCostResponse }
  | { kind: 'pending' | 'rejected'; error?: unknown }
  | { kind: 'skipped' };

@Injectable({ providedIn: 'root' })
export class ReceiptCostCorrectionService {
  private readonly inventory = inject(InventoryService);
  private readonly drafts = inject(ReceiptCostCorrectionDraftService);

  async submit(
    scope: ShopScope, draft: ReceiptCostCorrectionDraft, isCurrent: () => boolean,
  ): Promise<ReceiptCostOutcome> {
    const target = receiptCostTarget(draft.receiptValueId, draft.request.expectedVersion);
    const current = await this.drafts.load(scope, target);
    if (!isCurrent()) return { kind: 'skipped' };
    const record = await this.drafts.prepare(scope, target, draft, current?.revision ?? 0);
    if (record.pending?.status !== 'sending') return { kind: 'pending' };
    return this.send(scope, record, isCurrent, false);
  }

  async recover(
    scope: ShopScope, record: ReceiptCostDraftRecord, isCurrent: () => boolean,
    canResend: () => boolean,
  ): Promise<ReceiptCostOutcome> {
    const pending = record.pending;
    if (!pending || !isCurrent()) return { kind: 'skipped' };
    if (await this.drafts.isConfirmed(scope, record.targetId, pending.operationKey) || !isCurrent())
      return { kind: 'skipped' };

    try {
      const outcome = await firstValueFrom(this.inventory.lookupReceiptCostCorrection(pending.operationKey, scope));
      if (!isCurrent()) return { kind: 'skipped' };
      if (outcome?.operationType !== RECEIPT_COST_CORRECTION || outcome.operationKey !== pending.operationKey ||
          outcome.targetId !== record.draft.receiptValueId)
        return { kind: 'pending' };
      return this.confirm(scope, record, outcome.response);
    } catch (error) {
      if (!(error instanceof HttpErrorResponse) || error.status !== 404 || problemCode(error.error) !== 'Operation.NotFound')
        return { kind: 'pending', error };
    }

    if (!isCurrent() || !canResend()) return { kind: 'pending' };
    const claimed = await this.drafts.claimRecovery(scope, record.targetId, pending.operationKey, pending.payload);
    return this.send(scope, claimed, () => isCurrent() && canResend(), true);
  }

  private async send(
    scope: ShopScope, record: ReceiptCostDraftRecord, isCurrent: () => boolean, recovery: boolean,
  ): Promise<ReceiptCostOutcome> {
    const pending = record.pending!;
    if (await this.drafts.isConfirmed(scope, record.targetId, pending.operationKey))
      return { kind: 'skipped' };
    if (!isCurrent()) {
      await this.drafts.markUnknown(scope, record.targetId, pending.operationKey);
      return { kind: 'skipped' };
    }

    let response: CorrectReceiptCostResponse;
    try {
      response = await firstValueFrom(this.inventory.correctReceiptCost(record.draft.receiptValueId, pending.payload, scope));
    } catch (error) {
      if (recovery && error instanceof HttpErrorResponse && error.status === 409 &&
          problemCode(error.error) === RECEIPT_VERSION_CONFLICT) {
        // A matching committed key would replay before the fresh request checks its version.
        await this.drafts.settle(scope, record.targetId, pending.operationKey, 'rejected');
        return { kind: 'rejected', error };
      }

      const preserve = recovery || !isCurrent() ||
        (error instanceof HttpErrorResponse && problemCode(error.error) === 'Licence.SecuritySuspended');
      const failure = await this.drafts.settleFailure(scope, record.targetId, pending.operationKey, error, preserve);
      if (!failure.matched) return { kind: 'skipped' };
      return { kind: failure.outcome === 'unknown' ? 'pending' : 'rejected', error };
    }

    if (!isCurrent() || !matchesReceiptCostOutcome(record.draft, response)) {
      await this.drafts.markUnknown(scope, record.targetId, pending.operationKey);
      return { kind: 'pending' };
    }
    return this.confirm(scope, record, response);
  }

  private async confirm(
    scope: ShopScope, record: ReceiptCostDraftRecord, response: CorrectReceiptCostResponse,
  ): Promise<ReceiptCostOutcome> {
    if (!matchesReceiptCostOutcome(record.draft, response)) return { kind: 'pending' };
    const key = record.pending!.operationKey;
    const settled = await this.drafts.settle(scope, record.targetId, key, 'confirmed', { response });
    if (settled?.confirmedOperationKey !== key) return { kind: 'skipped' };
    await this.drafts.clearConfirmed(scope, record.targetId, key);
    return { kind: 'confirmed', response };
  }
}
