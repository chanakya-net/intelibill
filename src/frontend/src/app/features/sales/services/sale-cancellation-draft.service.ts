import { Injectable } from '@angular/core';
import type { ScopeOwner } from '../../../core/context/shop-scope';
import { isScopeChangeError } from '../../../core/context/scoped-request';
import { classifyFinancialOperationFailure } from '../../../core/services/financial-operation-intent.service';
import type { CancelSaleRequest } from './sale-cancellation.models';
import type {
  SaleCancellationDraftRecord,
  SaleCancellationEditableDraft,
} from './sale-cancellation.models';

function classifySaleCancellationFailure(
  error: unknown,
  wasUnknown: boolean,
): 'unknown' | 'rejected' {
  const code = (error as { error?: { code?: string; title?: string } })?.error;
  return wasUnknown ||
    isScopeChangeError(error) ||
    code?.code === 'Operation.PayloadMismatch' ||
    code?.title === 'Operation.PayloadMismatch' ||
    classifyFinancialOperationFailure(error) !== 'rejected'
    ? 'unknown'
    : 'rejected';
}
export class SaleCancellationDraftConflictError extends Error {
  constructor() {
    super('SaleCancellation draft changed in another tab or is frozen.');
  }
}

/** One read-modify-write transaction is the cross-tab claim and durability boundary. */
@Injectable({ providedIn: 'root' })
export class SaleCancellationDraftService {
  private readonly store = 'drafts';
  private readonly confirmations = 'confirmations';
  async load(scope: ScopeOwner, targetId: string): Promise<SaleCancellationDraftRecord | null> {
    return this.transact(scope, targetId, 'readonly', (current) => current);
  }
  save(
    scope: ScopeOwner,
    targetId: string,
    draft: SaleCancellationEditableDraft,
    expectedRevision: number,
  ): Promise<SaleCancellationDraftRecord> {
    return this.edit(scope, targetId, draft, expectedRevision, false);
  }
  prepare(
    scope: ScopeOwner,
    targetId: string,
    draft: SaleCancellationEditableDraft,
    expectedRevision: number,
  ): Promise<SaleCancellationDraftRecord> {
    return this.edit(scope, targetId, draft, expectedRevision, true);
  }
  private async edit(
    scope: ScopeOwner,
    targetId: string,
    draft: SaleCancellationEditableDraft,
    revision: number,
    prepare: boolean,
  ): Promise<SaleCancellationDraftRecord> {
    const snapshot = structuredClone(draft);
    const record = await this.transact(scope, targetId, 'readwrite', (current) => {
      if (prepare && current?.pending)
        return {
          ...current,
          revision: current.revision + 1,
          pending: { ...current.pending, status: 'unknown' },
          updatedAt: new Date().toISOString(),
        };
      if (
        current?.pending ||
        current?.confirmedOperationKey ||
        (current?.revision ?? 0) !== revision
      )
        throw new SaleCancellationDraftConflictError();
      const operationKey = prepare ? crypto.randomUUID() : null;
      return {
        schemaVersion: 1,
        userId: scope.userId,
        shopId: scope.shopId,
        targetId: targetId,
        revision: revision + 1,
        draft: snapshot,
        pending: operationKey
          ? {
              operationKey,
              status: 'sending',
              payload: this.payload(snapshot, operationKey),
            }
          : null,
        confirmedOperationKey: null,
        updatedAt: new Date().toISOString(),
      };
    });
    return record!;
  }
  /** A reopened retry must establish its uncertainty durably before it may send. The identity
   * and frozen content are checked inside the same claim transaction, so a disappeared or
   * replaced operation cannot be sent from an earlier in-memory snapshot. */
  async claimRecovery(
    scope: ScopeOwner,
    targetId: string,
    operationKey: string,
    payload: CancelSaleRequest,
  ): Promise<SaleCancellationDraftRecord> {
    const expectedPayload = JSON.stringify(payload);
    const claimed = await this.transact(scope, targetId, 'readwrite', (current) => {
      if (
        !current?.pending ||
        current.pending.operationKey !== operationKey ||
        JSON.stringify(current.pending.payload) !== expectedPayload
      )
        throw new SaleCancellationDraftConflictError();
      return {
        ...current,
        revision: current.revision + 1,
        pending: { ...current.pending, status: 'unknown' },
        updatedAt: new Date().toISOString(),
      };
    });
    return claimed!;
  }

  async markUnknown(scope: ScopeOwner, targetId: string, operationKey: string): Promise<void> {
    await this.transact(scope, targetId, 'readwrite', (current) =>
      current?.pending?.operationKey === operationKey
        ? {
            ...current,
            revision: current.revision + 1,
            pending: { ...current.pending, status: 'unknown' },
            updatedAt: new Date().toISOString(),
          }
        : current,
    );
  }
  async settle(
    scope: ScopeOwner,
    targetId: string,
    operationKey: string,
    outcome: 'confirmed' | 'rejected',
  ): Promise<SaleCancellationDraftRecord | null> {
    return this.transact(scope, targetId, 'readwrite', (current) =>
      current?.pending?.operationKey === operationKey
        ? {
            ...current,
            revision: current.revision + 1,
            pending: null,
            confirmedOperationKey: outcome === 'confirmed' ? operationKey : null,
            updatedAt: new Date().toISOString(),
          }
        : current,
    );
  }
  /** The current durable status, rather than a pre-HTTP snapshot, owns failure settlement. */
  async settleFailure(
    scope: ScopeOwner,
    targetId: string,
    operationKey: string,
    error: unknown,
    recovery: boolean,
  ): Promise<{
    record: SaleCancellationDraftRecord | null;
    outcome: 'unknown' | 'rejected';
    matched: boolean;
  }> {
    let outcome: 'unknown' | 'rejected' = 'unknown';
    let matched = false;
    const record = await this.transact(scope, targetId, 'readwrite', (current) => {
      if (current?.pending?.operationKey !== operationKey) return current;
      matched = true;
      outcome = classifySaleCancellationFailure(
        error,
        recovery || current.pending.status === 'unknown',
      );
      return {
        ...current,
        revision: current.revision + 1,
        pending: outcome === 'unknown' ? { ...current.pending, status: 'unknown' } : null,
        updatedAt: new Date().toISOString(),
      };
    });
    return { record, outcome, matched };
  }

  /** Immutable, operation-specific evidence survives another tab cleaning the draft tombstone. */
  async isConfirmed(scope: ScopeOwner, targetId: string, operationKey: string): Promise<boolean> {
    let confirmed = false;
    await this.transact(scope, targetId, 'readonly', (current, proofs) => {
      confirmed = current?.confirmedOperationKey === operationKey;
      const request = proofs.get([scope.userId, scope.shopId, targetId, operationKey]);
      request.onsuccess = () => {
        confirmed ||= !!request.result;
      };
      return current;
    });
    return confirmed;
  }

  /** Only the exact confirmed operation may clean its draft; replacements are untouched. */
  async clearConfirmed(
    scope: ScopeOwner,
    targetId: string,
    operationKey: string,
  ): Promise<boolean> {
    let cleared = false;
    await this.transact(scope, targetId, 'readwrite', (current) => {
      if (current?.confirmedOperationKey !== operationKey) return current;
      cleared = true;
      return null;
    });
    return cleared;
  }

  /** A stale editor cannot discard another tab's pending work or a newer revision. */
  async discard(scope: ScopeOwner, targetId: string, expectedRevision: number): Promise<void> {
    await this.transact(scope, targetId, 'readwrite', (current) => {
      if (current && (current.pending || current.revision !== expectedRevision))
        throw new SaleCancellationDraftConflictError();
      return null;
    });
  }
  private payload(draft: SaleCancellationEditableDraft, operationKey: string): CancelSaleRequest {
    return { ...draft.payload, operationKey } as CancelSaleRequest;
  }

  private async transact(
    scope: ScopeOwner,
    targetId: string,
    mode: IDBTransactionMode,
    update: (
      current: SaleCancellationDraftRecord | null,
      proofs: IDBObjectStore,
    ) => SaleCancellationDraftRecord | null,
  ): Promise<SaleCancellationDraftRecord | null> {
    if (!scope.userId || !scope.shopId || !targetId)
      throw new Error('SaleCancellation owner is incomplete.');
    const db = await this.open();
    try {
      return await new Promise((resolve, reject) => {
        const tx = db.transaction([this.store, this.confirmations], mode);
        const store = tx.objectStore(this.store);
        const key = [scope.userId, scope.shopId, targetId];
        let result: SaleCancellationDraftRecord | null = null;
        let failure: unknown;
        tx.oncomplete = () => resolve(result);
        tx.onabort = tx.onerror = () =>
          reject(failure ?? tx.error ?? new Error('SaleCancellation storage transaction failed.'));
        const request = store.get(key);
        request.onsuccess = () => {
          try {
            const current = (request.result as SaleCancellationDraftRecord | undefined) ?? null;
            result = update(current, tx.objectStore(this.confirmations));
            if (mode === 'readwrite') {
              const operationKey =
                result?.confirmedOperationKey ??
                (result === null ? current?.confirmedOperationKey : null);
              if (operationKey)
                tx.objectStore(this.confirmations).put({
                  userId: scope.userId,
                  shopId: scope.shopId,
                  targetId: targetId,
                  operationKey,
                });
            }
            if (mode === 'readwrite' && result !== current) {
              if (result) store.put(result);
              else store.delete(key);
            }
          } catch (error) {
            failure = error;
            tx.abort();
          }
        };
      });
    } finally {
      db.close();
    }
  }
  private open(): Promise<IDBDatabase> {
    if (typeof indexedDB === 'undefined')
      return Promise.reject(new Error('IndexedDB is unavailable.'));
    return new Promise((resolve, reject) => {
      const request = indexedDB.open('intelibill-sale-cancellations', 2);
      let blocked = false;
      request.onupgradeneeded = () => {
        if (!request.result.objectStoreNames.contains(this.confirmations))
          request.result.createObjectStore(this.confirmations, {
            keyPath: ['userId', 'shopId', 'targetId', 'operationKey'],
          });
        if (!request.result.objectStoreNames.contains(this.store))
          request.result.createObjectStore(this.store, {
            keyPath: ['userId', 'shopId', 'targetId'],
          });
      };
      request.onblocked = () => {
        blocked = true;
        reject(new Error('SaleCancellation storage is blocked.'));
      };
      request.onerror = () => reject(request.error);
      request.onsuccess = () => {
        if (blocked) {
          request.result.close();
          return;
        }
        request.result.onversionchange = () => request.result.close();
        resolve(request.result);
      };
    });
  }
}
