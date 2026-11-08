import { Observable, Subject } from 'rxjs';
import { Injectable } from '@angular/core';
import type { ScopeOwner } from '../../../core/context/shop-scope';
import { isScopeChangeError } from '../../../core/context/scoped-request';
import { classifyFinancialOperationFailure } from '../../../core/services/financial-operation-intent.service';
import type { TillActionPayload } from './till.models';
import type { TillDraftRecord, TillEditableDraft } from './till.models';

function classifyTillFailure(error: unknown, wasUnknown: boolean): 'unknown' | 'rejected' {
  const code = (error as { error?: { code?: string; title?: string } })?.error;
  return wasUnknown ||
    isScopeChangeError(error) ||
    code?.code === 'Operation.PayloadMismatch' ||
    code?.title === 'Operation.PayloadMismatch' ||
    classifyFinancialOperationFailure(error) !== 'rejected'
    ? 'unknown'
    : 'rejected';
}
export class TillDraftConflictError extends Error {
  constructor() {
    super('Till draft changed in another tab or is frozen.');
  }
}

/** One read-modify-write transaction is the cross-tab claim and durability boundary. */
@Injectable({ providedIn: 'root' })
export class TillDraftService {
  private readonly changed = new Subject<void>();
  watch(): Observable<void> {
    return new Observable((subscriber) => {
      const subscription = this.changed.subscribe(() => subscriber.next());
      const channel =
        typeof BroadcastChannel === 'undefined'
          ? null
          : new BroadcastChannel('intelibill-till-actions');
      if (channel) channel.onmessage = () => subscriber.next();
      return () => {
        subscription.unsubscribe();
        channel?.close();
      };
    });
  }
  private notify() {
    this.changed.next();
    if (typeof BroadcastChannel !== 'undefined') {
      const channel = new BroadcastChannel('intelibill-till-actions');
      channel.postMessage('changed');
      channel.close();
    }
  }
  private readonly store = 'drafts';
  private readonly confirmations = 'confirmations';
  async list(scope: ScopeOwner): Promise<TillDraftRecord[]> {
    const db = await this.open();
    try {
      return await new Promise((resolve, reject) => {
        const tx = db.transaction(this.store, 'readonly');
        const request = tx.objectStore(this.store).getAll();
        request.onsuccess = () =>
          resolve(
            (request.result as TillDraftRecord[]).filter(
              (x) => x.userId === scope.userId && x.shopId === scope.shopId,
            ),
          );
        request.onerror = () => reject(request.error);
      });
    } finally {
      db.close();
    }
  }
  async load(scope: ScopeOwner, targetId: string): Promise<TillDraftRecord | null> {
    return this.transact(scope, targetId, 'readonly', (current) => current);
  }
  save(
    scope: ScopeOwner,
    targetId: string,
    draft: TillEditableDraft,
    expectedRevision: number,
  ): Promise<TillDraftRecord> {
    return this.edit(scope, targetId, draft, expectedRevision, false);
  }
  prepare(
    scope: ScopeOwner,
    targetId: string,
    draft: TillEditableDraft,
    expectedRevision: number,
  ): Promise<TillDraftRecord> {
    return this.edit(scope, targetId, draft, expectedRevision, true);
  }
  private async edit(
    scope: ScopeOwner,
    targetId: string,
    draft: TillEditableDraft,
    revision: number,
    prepare: boolean,
  ): Promise<TillDraftRecord> {
    const snapshot = structuredClone(draft);
    const record = await this.transact(
      scope,
      targetId,
      'readwrite',
      (current) => {
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
          throw new TillDraftConflictError();
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
      },
      true,
      prepare && targetId.split(':').length >= 3
        ? targetId.split(':').slice(0, 2).join(':') + ':'
        : undefined,
    );
    return record!;
  }
  /** A reopened retry must establish its uncertainty durably before it may send. The identity
   * and frozen content are checked inside the same claim transaction, so a disappeared or
   * replaced operation cannot be sent from an earlier in-memory snapshot. */
  async claimRecovery(
    scope: ScopeOwner,
    targetId: string,
    operationKey: string,
    payload: TillActionPayload,
  ): Promise<TillDraftRecord> {
    const expectedPayload = JSON.stringify(payload);
    const claimed = await this.transact(scope, targetId, 'readwrite', (current) => {
      if (
        !current?.pending ||
        current.pending.operationKey !== operationKey ||
        JSON.stringify(current.pending.payload) !== expectedPayload
      )
        throw new TillDraftConflictError();
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
  ): Promise<TillDraftRecord | null> {
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
    record: TillDraftRecord | null;
    outcome: 'unknown' | 'rejected';
    matched: boolean;
  }> {
    let outcome: 'unknown' | 'rejected' = 'unknown';
    let matched = false;
    const record = await this.transact(scope, targetId, 'readwrite', (current) => {
      if (current?.pending?.operationKey !== operationKey) return current;
      matched = true;
      outcome = classifyTillFailure(error, recovery || current.pending.status === 'unknown');
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
        throw new TillDraftConflictError();
      return null;
    });
  }
  private payload(draft: TillEditableDraft, operationKey: string): TillActionPayload {
    return { ...draft.payload, operationKey } as TillActionPayload;
  }

  private async transact(
    scope: ScopeOwner,
    targetId: string,
    mode: IDBTransactionMode,
    update: (current: TillDraftRecord | null, proofs: IDBObjectStore) => TillDraftRecord | null,
    preventCompletedTarget = false,
    pendingPrefix?: string,
  ): Promise<TillDraftRecord | null> {
    if (!scope.userId || !scope.shopId || !targetId) throw new Error('Till owner is incomplete.');
    const db = await this.open();
    try {
      return await new Promise((resolve, reject) => {
        const tx = db.transaction([this.store, this.confirmations], mode);
        const store = tx.objectStore(this.store);
        const key = [scope.userId, scope.shopId, targetId];
        let result: TillDraftRecord | null = null;
        let failure: unknown;
        tx.oncomplete = () => {
          resolve(result);
          if (mode === 'readwrite') this.notify();
        };
        tx.onabort = tx.onerror = () =>
          reject(failure ?? tx.error ?? new Error('Till storage transaction failed.'));
        const request = store.get(key);
        request.onsuccess = () => {
          const applyUpdate = () => {
            try {
              const current = (request.result as TillDraftRecord | undefined) ?? null;
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
          const checkCompletion = () => {
            if (preventCompletedTarget) {
              // Completion is terminal for this action intent. Check durable proofs in the same
              // transaction as the revision CAS, even after the editable row was cleaned.
              const proof = tx.objectStore(this.confirmations).index('target').get(key);
              proof.onsuccess = () => {
                if (proof.result) {
                  failure = new TillDraftConflictError();
                  tx.abort();
                } else applyUpdate();
              };
            } else applyUpdate();
          };
          if (pendingPrefix) {
            // Prefix recovery and new-cycle preparation share this write transaction. Separate
            // list/read calls cannot prevent two tabs from creating different-cycle requests.
            const peers = store.getAll();
            peers.onsuccess = () => {
              const prior = (peers.result as TillDraftRecord[]).find(
                (x) =>
                  x.userId === scope.userId &&
                  x.shopId === scope.shopId &&
                  x.targetId.startsWith(pendingPrefix) &&
                  x.pending,
              );
              if (prior && prior.targetId !== targetId) {
                result = {
                  ...prior,
                  revision: prior.revision + 1,
                  pending: { ...prior.pending!, status: 'unknown' },
                  updatedAt: new Date().toISOString(),
                };
                store.put(result);
              } else checkCompletion();
            };
          } else checkCompletion();
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
      const request = indexedDB.open('intelibill-till-actions', 3);
      let blocked = false;
      request.onupgradeneeded = () => {
        const proofs = request.result.objectStoreNames.contains(this.confirmations)
          ? request.transaction!.objectStore(this.confirmations)
          : request.result.createObjectStore(this.confirmations, {
              keyPath: ['userId', 'shopId', 'targetId', 'operationKey'],
            });
        if (!proofs.indexNames.contains('target'))
          proofs.createIndex('target', ['userId', 'shopId', 'targetId']);
        if (!request.result.objectStoreNames.contains(this.store))
          request.result.createObjectStore(this.store, {
            keyPath: ['userId', 'shopId', 'targetId'],
          });
      };
      request.onblocked = () => {
        blocked = true;
        reject(new Error('Till storage is blocked.'));
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
