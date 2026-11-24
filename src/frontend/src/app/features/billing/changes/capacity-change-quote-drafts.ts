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
}

const prefix = 'intelibill.billing.capacity-change.v1:';

@Injectable({ providedIn: 'root' })
export class CapacityChangeQuoteDrafts {
  key(scope: CheckoutScope, term: string): string {
    return prefix + JSON.stringify([scope.userId, scope.shopId, scope.account, term]);
  }

  read(scope: CheckoutScope, term: string): CapacityChangeDraft | null {
    const raw = localStorage.getItem(this.key(scope, term));
    if (raw === null) return null;
    const value = JSON.parse(raw) as CapacityChangeDraft;
    const command = value?.command;
    if (value?.schemaVersion !== 1 || !command || !isUuid(command.operationKey) || !isUuid(command.targetPlanVersionId) ||
      !Number.isSafeInteger(command.targetCapacity) || !Number.isSafeInteger(command.observedCapacity) ||
      command.targetCapacity < command.observedCapacity || typeof command.observedPaidThrough !== 'string')
      throw new Error('Invalid capacity change draft');
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

  /** An explicit new preview: the frozen request is discarded only when it is still the one shown. */
  discard(scope: CheckoutScope, term: string, operationKey: string): Promise<void> {
    const key = this.key(scope, term);
    return navigator.locks.request(key, () => {
      if (this.read(scope, term)?.command.operationKey === operationKey) localStorage.removeItem(key);
    });
  }
}
