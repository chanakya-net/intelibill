import { Injectable } from '@angular/core';
import { PaymentDraftStore } from '../payments/manual-payment-drafts';
import type { ApproveRefundCommand } from './refund-approval-api.service';

/**
 * Durable, owner-bound approval intents: one frozen command per admin + account + refund request, claimed by
 * revision compare-and-set so a second tab or a reload can only see, check or retry the original key.
 */
@Injectable({ providedIn: 'root' })
export class RefundApprovalDrafts extends PaymentDraftStore<ApproveRefundCommand> {
  constructor() {
    super('intelibill.admin.refund-approvals');
  }
}
