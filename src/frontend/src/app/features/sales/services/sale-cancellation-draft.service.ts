import { Injectable } from '@angular/core';
import { FinancialActionDraftStore } from '../../../core/services/financial-action-draft-store';
import type { CancelSaleRequest, SaleCancellationEditableDraft } from './sale-cancellation.models';
export class SaleCancellationDraftConflictError extends Error {
  constructor() {
    super('SaleCancellation draft changed in another tab or is frozen.');
  }
}
/** Typed adapter over the shared durable engine; existing namespace and record schema are retained. */
@Injectable({ providedIn: 'root' })
export class SaleCancellationDraftService extends FinancialActionDraftStore<
  SaleCancellationEditableDraft,
  CancelSaleRequest
> {
  constructor() {
    super({
      diagnosticName: 'SaleCancellation',
      namespace: 'intelibill-sale-cancellations',
      conflict: () => new SaleCancellationDraftConflictError(),
      payload: (draft, operationKey) => ({ ...draft.payload, operationKey }),
    });
  }
}
