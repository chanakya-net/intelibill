import { Injectable } from '@angular/core';
import { isUuid, type CheckoutScope } from '../checkout/checkout.models';
import type { CreateCapacityChangeQuote } from './capacity-change-quote.service';

/**
 * The exact preview request, saved before it is sent and scoped to user, shop, account and term. After a lost answer
 * or a reload the same request is resent with the same key, so licensing returns the one quote it froze.
 */
export interface CapacityChangeDraft {
  readonly schemaVersion: 1;
  readonly command: CreateCapacityChangeQuote;
  readonly purchase?: { readonly quoteId: string; readonly snapshotHash: string; readonly operationKey: string };
}

export class InvalidCapacityChangeDraft extends Error {}

const prefix = 'intelibill.billing.capacity-change.v1:';

@Injectable({ providedIn: 'root' })
export class CapacityChangeQuoteDrafts {
  key(scope: CheckoutScope, term: string): string {
    return prefix + JSON.stringify([scope.userId, scope.shopId, scope.account, term]);
  }

  read(scope: CheckoutScope, term: string): CapacityChangeDraft | null {
    const raw = localStorage.getItem(this.key(scope, term));
    if (raw === null) return null;
    let value: CapacityChangeDraft;
    try {
      value = JSON.parse(raw) as CapacityChangeDraft;
    } catch {
      throw new InvalidCapacityChangeDraft();
    }
    const command = value?.command;
    if (value?.schemaVersion !== 1 || !command || !isUuid(command.operationKey) || !isUuid(command.targetPlanVersionId) ||
      !Number.isSafeInteger(command.targetCapacity) || !Number.isSafeInteger(command.observedCapacity) ||
      command.targetCapacity < command.observedCapacity || typeof command.observedPaidThrough !== 'string' || !Number.isFinite(Date.parse(command.observedPaidThrough)))
      throw new InvalidCapacityChangeDraft();
    if (value.purchase && (!isUuid(value.purchase.quoteId) || !isUuid(value.purchase.operationKey) ||
      !/^[a-f0-9]{64}$/.test(value.purchase.snapshotHash))) throw new InvalidCapacityChangeDraft();
    return value;
  }

  /** Saves the request unless one is already pending for this term; returns whichever is saved. */
  prepare(scope: CheckoutScope, term: string, command: CreateCapacityChangeQuote): Promise<CapacityChangeDraft> {
    const key = this.key(scope, term);
    return navigator.locks.request(key, () => {
      const saved = this.read(scope, term);
      if (saved) return saved;
      const draft: CapacityChangeDraft = { schemaVersion: 1, command };
      localStorage.setItem(key, JSON.stringify(draft));
      return draft;
    });
  }

  purchase(scope: CheckoutScope, term: string, previewKey: string, quoteId: string, snapshotHash: string) {
    const key = this.key(scope, term);
    return navigator.locks.request(key, () => {
      const saved = this.read(scope, term);
      if (!saved || saved.command.operationKey !== previewKey) throw new InvalidCapacityChangeDraft();
      if (saved.purchase) return saved.purchase;
      const purchase = { quoteId, snapshotHash, operationKey: crypto.randomUUID() };
      localStorage.setItem(key, JSON.stringify({ ...saved, purchase }));
      return purchase;
    });
  }

  /** Discard the matching request, or only malformed data when the owner explicitly resets it. */
  discard(scope: CheckoutScope, term: string, operationKey?: string, settledOrder?: string): Promise<void> {
    const key = this.key(scope, term);
    return navigator.locks.request(key, () => {
      try {
        const saved = this.read(scope, term);
        if (saved && saved.command.operationKey === operationKey &&
          (!saved.purchase || saved.purchase.quoteId === settledOrder)) localStorage.removeItem(key);
      } catch (error) {
        if (!(error instanceof InvalidCapacityChangeDraft)) throw error;
        if (operationKey === undefined) localStorage.removeItem(key);
      }
    });
  }
}
