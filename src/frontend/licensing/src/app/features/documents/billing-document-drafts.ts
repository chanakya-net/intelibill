import { Injectable } from '@angular/core';
import type { PaymentScope } from '../payments/manual-payment-api.service';
import { PaymentDraftStore } from '../payments/manual-payment-drafts';
import type { InvoiceIntent } from './billing-document-file';

export interface CleanupState {
  readonly operationKey: string;
  settled: boolean;
}

export interface SyncedDraft {
  readonly revision: number;
  readonly pending: InvoiceIntent | null;
}

@Injectable({ providedIn: 'root' })
export class BillingDocumentDrafts extends PaymentDraftStore<InvoiceIntent> {
  constructor() {
    super('intelibill.admin.billing-documents');
  }

  /** Completes local settlement and re-reads revision independently of the server write. */
  async settleAndSync(
    scope: PaymentScope,
    state: CleanupState,
    isCurrent: () => boolean,
  ): Promise<SyncedDraft | null> {
    try {
      if (!state.settled) {
        await this.settle(scope, state.operationKey);
        if (!isCurrent()) return null;
        state.settled = true;
      }
      const draft = await this.read(scope);
      if (!isCurrent()) return null;
      return { revision: draft.revision, pending: draft.pending ?? null };
    } catch {
      return null;
    }
  }
}
