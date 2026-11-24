import { Injectable } from '@angular/core';
import { isUuid, type CheckoutScope } from '../checkout/checkout.models';
import {
  normalizeRefundReason, REFUND_MAX_PAISE, refundCommandsMatch, type CreateRefundRequest,
} from './refund-request.models';

/**
 * Exact refund-request command saved before send and scoped to user, shop, billing account and confirmation.
 * After a lost answer or reload the same operationKey and payload are resent so licensing preserves one request.
 */
export interface RefundRequestDraft {
  readonly schemaVersion: 1;
  readonly command: CreateRefundRequest;
}

export interface PrepareRefundDraftResult {
  readonly draft: RefundRequestDraft;
  readonly existing: boolean;
  readonly matchesRequest: boolean;
}

export class InvalidRefundRequestDraft extends Error {}

const prefix = 'intelibill.billing.refund-request.v1:';

@Injectable({ providedIn: 'root' })
export class RefundRequestDrafts {
  key(scope: CheckoutScope, confirmation: string): string {
    return prefix + JSON.stringify([scope.userId, scope.shopId, scope.account, confirmation]);
  }

  read(scope: CheckoutScope, confirmation: string): RefundRequestDraft | null {
    const raw = localStorage.getItem(this.key(scope, confirmation));
    if (raw === null) return null;
    let value: RefundRequestDraft;
    try {
      value = JSON.parse(raw) as RefundRequestDraft;
    } catch {
      throw new InvalidRefundRequestDraft();
    }
    const c = value?.command;
    if (value?.schemaVersion !== 1 || !c || !isUuid(c.operationKey) || !isUuid(c.orderId) ||
      !isUuid(c.confirmationId) || c.confirmationId !== confirmation ||
      !Number.isSafeInteger(c.amountPaise) || c.amountPaise <= 0 || c.amountPaise > REFUND_MAX_PAISE ||
      typeof c.reason !== 'string' || normalizeRefundReason(c.reason) !== c.reason)
      throw new InvalidRefundRequestDraft();
    return value;
  }

  /** Saves the command unless one is already pending for this confirmation; flags whether an existing draft matches. */
  prepare(scope: CheckoutScope, confirmation: string, command: CreateRefundRequest): Promise<PrepareRefundDraftResult> {
    const key = this.key(scope, confirmation);
    return navigator.locks.request(key, () => {
      const saved = this.read(scope, confirmation);
      if (saved) {
        return { draft: saved, existing: true, matchesRequest: refundCommandsMatch(saved.command, command) };
      }
      const draft: RefundRequestDraft = { schemaVersion: 1, command };
      localStorage.setItem(key, JSON.stringify(draft));
      return { draft, existing: false, matchesRequest: true };
    });
  }

  /** Discards a matching saved command, or malformed data when resetting explicitly. */
  discard(scope: CheckoutScope, confirmation: string, operationKey?: string): Promise<void> {
    const key = this.key(scope, confirmation);
    return navigator.locks.request(key, () => {
      try {
        const saved = this.read(scope, confirmation);
        if (saved && saved.command.operationKey === operationKey) localStorage.removeItem(key);
      } catch (error) {
        if (!(error instanceof InvalidRefundRequestDraft)) throw error;
        if (operationKey === undefined) localStorage.removeItem(key);
      }
    });
  }
}
