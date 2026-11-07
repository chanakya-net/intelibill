import { Injectable } from '@angular/core';
import type { ScopeOwner } from '../../../core/context/shop-scope';
import { classifyReceivingFailure } from './receiving-errors';
import type { ReceivePurchaseOrderRequest } from './purchase-order.service';
import type { ReceivingDraftRecord, ReceivingEditableDraft } from './receiving.models';

export class ReceivingDraftConflictError extends Error {
  constructor() {
    super('Receiving draft changed in another tab or is frozen.');
  }
}

/** One read-modify-write transaction is the cross-tab claim and durability boundary. */
@Injectable({ providedIn: 'root' })
export class ReceivingDraftService {
  private readonly store = 'drafts';
  private readonly confirmations = 'confirmations';
  async load(scope: ScopeOwner, poId: string): Promise<ReceivingDraftRecord | null> {
    return this.transact(scope, poId, 'readonly', (current) => current);
  }
  save(
    scope: ScopeOwner,
    poId: string,
    draft: ReceivingEditableDraft,
    expectedRevision: number,
  ): Promise<ReceivingDraftRecord> {
    return this.edit(scope, poId, draft, expectedRevision, false);
  }
  prepare(
    scope: ScopeOwner,
    poId: string,
    draft: ReceivingEditableDraft,
    expectedRevision: number,
  ): Promise<ReceivingDraftRecord> {
    return this.edit(scope, poId, draft, expectedRevision, true);
  }
  private async edit(
    scope: ScopeOwner,
    poId: string,
    draft: ReceivingEditableDraft,
    revision: number,
    prepare: boolean,
  ): Promise<ReceivingDraftRecord> {
    const snapshot = structuredClone(draft);
    const record = await this.transact(scope, poId, 'readwrite', (current) => {
      if (prepare && current?.pending)
        return {
          ...current,
          revision: current.revision + 1,
          pending: { ...current.pending, status: 'unknown' },
          updatedAt: new Date().toISOString(),
        };
      if (
        current?.pending ||
        current?.confirmedOperationId ||
        (current?.revision ?? 0) !== revision
      )
        throw new ReceivingDraftConflictError();
      const operationId = prepare ? crypto.randomUUID() : null;
      return {
        schemaVersion: 1,
        userId: scope.userId,
        shopId: scope.shopId,
        purchaseOrderId: poId,
        revision: revision + 1,
        draft: snapshot,
        pending: operationId
          ? {
              operationId,
              status: 'sending',
              payload: this.payload(snapshot, operationId),
            }
          : null,
        confirmedOperationId: null,
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
    poId: string,
    operationId: string,
    payload: ReceivePurchaseOrderRequest,
  ): Promise<ReceivingDraftRecord> {
    const expectedPayload = JSON.stringify(payload);
    const claimed = await this.transact(scope, poId, 'readwrite', (current) => {
      if (
        !current?.pending ||
        current.pending.operationId !== operationId ||
        JSON.stringify(current.pending.payload) !== expectedPayload
      )
        throw new ReceivingDraftConflictError();
      return {
        ...current,
        revision: current.revision + 1,
        pending: { ...current.pending, status: 'unknown' },
        updatedAt: new Date().toISOString(),
      };
    });
    return claimed!;
  }

  async markUnknown(scope: ScopeOwner, poId: string, operationId: string): Promise<void> {
    await this.transact(scope, poId, 'readwrite', (current) =>
      current?.pending?.operationId === operationId
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
    poId: string,
    operationId: string,
    outcome: 'confirmed' | 'rejected',
  ): Promise<ReceivingDraftRecord | null> {
    return this.transact(scope, poId, 'readwrite', (current) =>
      current?.pending?.operationId === operationId
        ? {
            ...current,
            revision: current.revision + 1,
            pending: null,
            confirmedOperationId: outcome === 'confirmed' ? operationId : null,
            updatedAt: new Date().toISOString(),
          }
        : current,
    );
  }
  /** The current durable status, rather than a pre-HTTP snapshot, owns failure settlement. */
  async settleFailure(
    scope: ScopeOwner,
    poId: string,
    operationId: string,
    error: unknown,
    recovery: boolean,
  ): Promise<{
    record: ReceivingDraftRecord | null;
    outcome: 'unknown' | 'rejected';
    matched: boolean;
  }> {
    let outcome: 'unknown' | 'rejected' = 'unknown';
    let matched = false;
    const record = await this.transact(scope, poId, 'readwrite', (current) => {
      if (current?.pending?.operationId !== operationId) return current;
      matched = true;
      outcome = classifyReceivingFailure(error, recovery || current.pending.status === 'unknown');
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
  async isConfirmed(scope: ScopeOwner, poId: string, operationId: string): Promise<boolean> {
    let confirmed = false;
    await this.transact(scope, poId, 'readonly', (current, proofs) => {
      confirmed = current?.confirmedOperationId === operationId;
      const request = proofs.get([scope.userId, scope.shopId, poId, operationId]);
      request.onsuccess = () => {
        confirmed ||= !!request.result;
      };
      return current;
    });
    return confirmed;
  }

  /** Only the exact confirmed operation may clean its draft; replacements are untouched. */
  async clearConfirmed(scope: ScopeOwner, poId: string, operationId: string): Promise<boolean> {
    let cleared = false;
    await this.transact(scope, poId, 'readwrite', (current) => {
      if (current?.confirmedOperationId !== operationId) return current;
      cleared = true;
      return null;
    });
    return cleared;
  }

  /** A stale editor cannot discard another tab's pending work or a newer revision. */
  async discard(scope: ScopeOwner, poId: string, expectedRevision: number): Promise<void> {
    await this.transact(scope, poId, 'readwrite', (current) => {
      if (current && (current.pending || current.revision !== expectedRevision))
        throw new ReceivingDraftConflictError();
      return null;
    });
  }
  private payload(draft: ReceivingEditableDraft, operationId: string): ReceivePurchaseOrderRequest {
    return {
      operationId,
      referenceNumber: draft.referenceNumber.trim() || null,
      notes: draft.notes.trim() || null,
      receivedAt: draft.receivedAt,
      lines: draft.lines.map((line) => ({
        ...line,
        barcode: line.barcode.trim(),
        batchNumber: line.batchNumber.trim(),
      })) as ReceivePurchaseOrderRequest['lines'],
    };
  }
  private async transact(
    scope: ScopeOwner,
    poId: string,
    mode: IDBTransactionMode,
    update: (
      current: ReceivingDraftRecord | null,
      proofs: IDBObjectStore,
    ) => ReceivingDraftRecord | null,
  ): Promise<ReceivingDraftRecord | null> {
    if (!scope.userId || !scope.shopId || !poId) throw new Error('Receiving owner is incomplete.');
    const db = await this.open();
    try {
      return await new Promise((resolve, reject) => {
        const tx = db.transaction([this.store, this.confirmations], mode);
        const store = tx.objectStore(this.store);
        const key = [scope.userId, scope.shopId, poId];
        let result: ReceivingDraftRecord | null = null;
        let failure: unknown;
        tx.oncomplete = () => resolve(result);
        tx.onabort = tx.onerror = () =>
          reject(failure ?? tx.error ?? new Error('Receiving storage transaction failed.'));
        const request = store.get(key);
        request.onsuccess = () => {
          try {
            const current = (request.result as ReceivingDraftRecord | undefined) ?? null;
            result = update(current, tx.objectStore(this.confirmations));
            if (mode === 'readwrite') {
              const operationId =
                result?.confirmedOperationId ??
                (result === null ? current?.confirmedOperationId : null);
              if (operationId)
                tx.objectStore(this.confirmations).put({
                  userId: scope.userId,
                  shopId: scope.shopId,
                  purchaseOrderId: poId,
                  operationId,
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
      const request = indexedDB.open('intelibill-purchase-receiving', 2);
      let blocked = false;
      request.onupgradeneeded = () => {
        if (!request.result.objectStoreNames.contains(this.confirmations))
          request.result.createObjectStore(this.confirmations, {
            keyPath: ['userId', 'shopId', 'purchaseOrderId', 'operationId'],
          });
        if (!request.result.objectStoreNames.contains(this.store))
          request.result.createObjectStore(this.store, {
            keyPath: ['userId', 'shopId', 'purchaseOrderId'],
          });
      };
      request.onblocked = () => {
        blocked = true;
        reject(new Error('Receiving storage is blocked.'));
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
