import { Injectable } from '@angular/core';
import { FinancialActionDraftStore } from '../../../core/services/financial-action-draft-store';
import type { TillActionPayload, TillEditableDraft } from './till.models';
export class TillDraftConflictError extends Error {
  constructor() {
    super('Till draft changed in another tab or is frozen.');
  }
}
/** Typed adapter over the shared durable engine; existing namespace and record schema are retained. */
@Injectable({ providedIn: 'root' })
export class TillDraftService extends FinancialActionDraftStore<
  TillEditableDraft,
  TillActionPayload
> {
  constructor() {
    super({
      diagnosticName: 'Till',
      namespace: 'intelibill-till-actions',
      conflict: () => new TillDraftConflictError(),
      payload: (draft, operationKey) => ({ ...draft.payload, operationKey }),
      externalKey: (draft) =>
        typeof draft.payload['operationKey'] === 'string' && draft.payload['operationKey']
          ? draft.payload['operationKey']
          : undefined,
      pendingPrefix: (target) =>
        target.split(':').length >= 3 ? target.split(':').slice(0, 2).join(':') + ':' : undefined,
      notify: true,
    });
  }
}
