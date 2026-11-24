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
  /**
   * Deliveries of this key started from any tab, counted under the draft lock before each send.
   * Absent on a draft saved before the counter existed, which is treated as already delivered.
   */
  readonly deliveries?: number;
}

export interface PrepareRefundDraftResult {
  readonly draft: RefundRequestDraft;
  readonly existing: boolean;
  readonly matchesRequest: boolean;
}

export class InvalidRefundRequestDraft extends Error {}

const prefix = 'intelibill.billing.refund-request.v1:';

function isCount(value: unknown): boolean {
  return value === undefined || (Number.isSafeInteger(value) && (value as number) >= 0);
}

@Injectable({ providedIn: 'root' })
export class RefundRequestDrafts {
  key(scope: CheckoutScope, confirmation: string): string {
    return prefix + JSON.stringify([scope.userId, scope.shopId, scope.account, confirmation]);
  }

  /** Reads the command saved for this receipt; data that is malformed or names another order is unreadable. */
  read(scope: CheckoutScope, confirmation: string, order: string): RefundRequestDraft | null {
    const raw = localStorage.getItem(this.key(scope, confirmation));
    if (raw === null) return null;
    let value: RefundRequestDraft;
    try {
      value = JSON.parse(raw) as RefundRequestDraft;
    } catch {
      throw new InvalidRefundRequestDraft();
    }
    const c = value?.command;
    if (value?.schemaVersion !== 1 || !c || !isUuid(c.operationKey) || c.orderId !== order ||
      c.confirmationId !== confirmation || !isCount(value.deliveries) ||
      !Number.isSafeInteger(c.amountPaise) || c.amountPaise <= 0 || c.amountPaise > REFUND_MAX_PAISE ||
      typeof c.reason !== 'string' || normalizeRefundReason(c.reason) !== c.reason)
      throw new InvalidRefundRequestDraft();
    return value;
  }

  /** Saves the command unless one is already pending for this confirmation; flags whether an existing draft matches. */
  prepare(scope: CheckoutScope, command: CreateRefundRequest): Promise<PrepareRefundDraftResult> {
    const key = this.key(scope, command.confirmationId);
    return navigator.locks.request(key, () => {
      const saved = this.read(scope, command.confirmationId, command.orderId);
      if (saved) {
        return { draft: saved, existing: true, matchesRequest: refundCommandsMatch(saved.command, command) };
      }
      const draft: RefundRequestDraft = { schemaVersion: 1, command, deliveries: 0 };
      localStorage.setItem(key, JSON.stringify(draft));
      return { draft, existing: false, matchesRequest: true };
    });
  }

  /**
   * Durably counts a delivery before it is sent. False when the saved command is gone or carries another key,
   * so nothing may be sent: another tab already settled it.
   */
  beginDelivery(scope: CheckoutScope, command: CreateRefundRequest): Promise<boolean> {
    const key = this.key(scope, command.confirmationId);
    return navigator.locks.request(key, () => {
      const saved = this.read(scope, command.confirmationId, command.orderId);
      if (saved?.command.operationKey !== command.operationKey) return false;
      const counted: RefundRequestDraft = { ...saved, deliveries: (saved.deliveries ?? 1) + 1 };
      localStorage.setItem(key, JSON.stringify(counted));
      return true;
    });
  }

  /**
   * Removes the saved command for this key and reports whether it is now gone. With onlyIfSoleDelivery the
   * command is kept whenever a second delivery was ever started, since that one may have committed.
   */
  release(scope: CheckoutScope, command: CreateRefundRequest, onlyIfSoleDelivery = false): Promise<boolean> {
    const key = this.key(scope, command.confirmationId);
    return navigator.locks.request(key, () => {
      const saved = this.read(scope, command.confirmationId, command.orderId);
      if (saved?.command.operationKey !== command.operationKey) return true;
      if (onlyIfSoleDelivery && saved.deliveries !== 1) return false;
      localStorage.removeItem(key);
      return true;
    });
  }

  /** Explicit reset: removes saved data only when it cannot be read as a command for this receipt. */
  discardUnreadable(scope: CheckoutScope, confirmation: string, order: string): Promise<void> {
    const key = this.key(scope, confirmation);
    return navigator.locks.request(key, () => {
      try {
        this.read(scope, confirmation, order);
      } catch (error) {
        if (!(error instanceof InvalidRefundRequestDraft)) throw error;
        localStorage.removeItem(key);
      }
    });
  }
}
