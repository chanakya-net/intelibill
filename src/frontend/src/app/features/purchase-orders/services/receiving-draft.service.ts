import { Injectable } from '@angular/core';
import type { ScopeOwner } from '../../../core/context/shop-scope';
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
  ): Promise<void> {
    await this.transact(scope, poId, 'readwrite', (current) =>
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
    update: (current: ReceivingDraftRecord | null) => ReceivingDraftRecord | null,
  ): Promise<ReceivingDraftRecord | null> {
    if (!scope.userId || !scope.shopId || !poId) throw new Error('Receiving owner is incomplete.');
    const db = await this.open();
    try {
      return await new Promise((resolve, reject) => {
        const tx = db.transaction(this.store, mode);
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
            result = update(current);
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
      const request = indexedDB.open('intelibill-purchase-receiving', 1);
      let blocked = false;
      request.onupgradeneeded = () => {
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
