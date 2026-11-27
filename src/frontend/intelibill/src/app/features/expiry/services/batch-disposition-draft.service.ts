import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { TillService } from '../../tills/services/till.service';
import { TillDraftService } from '../../tills/services/till-draft.service';
import { ExpiryPolicyService } from './expiry-policy.service';
export interface BatchDispositionChange {
  readonly id: string;
  readonly inventoryBatchId: string;
  readonly rootChangeId: string | null;
  readonly action: 'Quarantine' | 'Release' | 'Dispose' | 'Reverse';
  readonly quantity: number;
  readonly sourceKind: string;
  readonly sourceId: string;
  readonly sourceLineId: string;
  readonly expiryDate: string | null;
  readonly manufacturingDate: string | null;
  readonly reasonCode: string;
  readonly reason: string;
  readonly businessDate: string;
  readonly recordedAt: string;
  readonly actorUserId: string;
  readonly policyJson: string;
}
export interface BatchDispositionPayload {
  readonly expectedVersion: number;
  readonly quarantineChangeId: string;
  readonly action: 'Release' | 'Dispose';
  readonly quantity: number;
  readonly reason: string;
}
export interface BatchDispositionResponse {
  readonly changeId: string;
  readonly quarantineChangeId: string;
  readonly batchId: string;
  readonly action: string;
  readonly quantity: number;
  readonly physicalQuantity: number;
  readonly quarantinedQuantity: number;
  readonly rootRemainingQuantity: number;
  readonly version: number;
  readonly adjustmentId: string | null;
}
/** Adapter to the existing durable financial-action engine, including cross-tab claims and tombstones. */
@Injectable({ providedIn: 'root' })
export class BatchDispositionDraftService {
  private readonly http = inject(HttpClient);
  private readonly context = inject(ShopContextService);
  private readonly actions = inject(TillService);
  private readonly drafts = inject(TillDraftService);
  private readonly policies = inject(ExpiryPolicyService);
  readonly changes$ = this.drafts.watch();
  history(batchId: string) {
    return this.http.get<readonly BatchDispositionChange[]>(
      `${API_BASE_URL}/inventory/batches/${encodeURIComponent(batchId)}/dispositions`,
    );
  }
  async pending(batchId: string) {
    const scope = this.context.scope();
    if (!scope) return [];
    return (await this.drafts.list(scope)).filter(
      (x) => x.pending && x.targetId.startsWith(`batch-disposition:${batchId}/`),
    );
  }
  execute(batchId: string, payload: BatchDispositionPayload) {
    const scope = this.context.scope();
    if (!scope || this.policies.role() !== 'owner') throw new Error('expiry.ownerOnly');
    return this.actions.execute<BatchDispositionResponse>(
      scope,
      'batch-disposition',
      `${batchId}/${payload.quarantineChangeId}`,
      String(payload.expectedVersion),
      { ...payload },
      `/inventory/batches/${encodeURIComponent(batchId)}/dispositions`,
      true,
      undefined,
      true,
    );
  }
}
export function remainingQuarantine(
  root: BatchDispositionChange,
  history: readonly BatchDispositionChange[],
): number {
  return (
    Math.round(
      (root.quantity -
        history.filter((x) => x.rootChangeId === root.id).reduce((n, x) => n + x.quantity, 0)) *
        1000,
    ) / 1000
  );
}
