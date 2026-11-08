import { Observable, Subject } from 'rxjs';
import type { ScopeOwner } from '../context/shop-scope';
import { isScopeChangeError } from '../context/scoped-request';
import { classifyFinancialOperationFailure } from './financial-operation-intent.service';
export interface FinancialDraftRecord<D, P> {
  schemaVersion: 1;
  userId: string;
  shopId: string;
  targetId: string;
  revision: number;
  draft: D;
  pending: { operationKey: string; payload: P; status: 'sending' | 'unknown' } | null;
  confirmedOperationKey: string | null;
  updatedAt: string;
}
interface DraftStoreOptions<D, P> {
  namespace: string;
  diagnosticName: string;
  conflict: () => Error;
  payload: (draft: D, operationKey: string) => P;
  externalKey?: (draft: D) => string | undefined;
  pendingPrefix?: (targetId: string) => string | undefined;
  notify?: boolean;
}

function classifyDraftFailure(error: unknown, wasUnknown: boolean): 'unknown' | 'rejected' {
  const code = (error as { error?: { code?: string; title?: string } })?.error;
  return wasUnknown ||
    isScopeChangeError(error) ||
    code?.code === 'Operation.PayloadMismatch' ||
    code?.title === 'Operation.PayloadMismatch' ||
    classifyFinancialOperationFailure(error) !== 'rejected'
    ? 'unknown'
    : 'rejected';
}
/** Shared version-3 storage engine. Namespaces and wire payload adapters preserve existing records. */
export class FinancialActionDraftStore<D, P extends { operationKey: string }> {
  constructor(private readonly options: DraftStoreOptions<D, P>) {}
  private readonly changed = new Subject<void>();
  watch(): Observable<void> {
    return new Observable((subscriber) => {
      const subscription = this.changed.subscribe(() => subscriber.next());
      const channel =
        typeof BroadcastChannel === 'undefined'
          ? null
          : new BroadcastChannel(this.options.namespace);
      if (channel) channel.onmessage = () => subscriber.next();
      return () => {
        subscription.unsubscribe();
        channel?.close();
      };
    });
  }
  private notify() {
    if (!this.options.notify) return;
    this.changed.next();
    if (typeof BroadcastChannel !== 'undefined') {
      const channel = new BroadcastChannel(this.options.namespace);
      channel.postMessage('changed');
      channel.close();
    }
  }
  private readonly store = 'drafts';
  private readonly confirmations = 'confirmations';
  async list(scope: ScopeOwner): Promise<FinancialDraftRecord<D, P>[]> {
    const db = await this.open();
    try {
      return await new Promise((resolve, reject) => {
        const tx = db.transaction(this.store, 'readonly');
        const request = tx.objectStore(this.store).getAll();
        request.onsuccess = () =>
          resolve(
            (request.result as FinancialDraftRecord<D, P>[]).filter(
              (x) => x.userId === scope.userId && x.shopId === scope.shopId,
            ),
          );
        request.onerror = () => reject(request.error);
      });
    } finally {
      db.close();
    }
  }
  async load(scope: ScopeOwner, targetId: string): Promise<FinancialDraftRecord<D, P> | null> {
    return this.transact(scope, targetId, 'readonly', (current) => current);
  }
  save(
    scope: ScopeOwner,
    targetId: string,
    draft: D,
    expectedRevision: number,
  ): Promise<FinancialDraftRecord<D, P>> {
    return this.edit(scope, targetId, draft, expectedRevision, false);
  }
  prepare(
    scope: ScopeOwner,
    targetId: string,
    draft: D,
    expectedRevision: number,
  ): Promise<FinancialDraftRecord<D, P>> {
    return this.edit(scope, targetId, draft, expectedRevision, true);
  }
  private async edit(
    scope: ScopeOwner,
    targetId: string,
    draft: D,
    revision: number,
    prepare: boolean,
  ): Promise<FinancialDraftRecord<D, P>> {
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
          throw this.options.conflict();
        const operationKey = prepare
          ? (this.options.externalKey?.(snapshot) ?? crypto.randomUUID())
          : null;
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
      prepare ? this.options.pendingPrefix?.(targetId) : undefined,
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
    payload: P,
  ): Promise<FinancialDraftRecord<D, P>> {
    const expectedPayload = JSON.stringify(payload);
    const claimed = await this.transact(scope, targetId, 'readwrite', (current) => {
      if (
        !current?.pending ||
        current.pending.operationKey !== operationKey ||
        JSON.stringify(current.pending.payload) !== expectedPayload
      )
        throw this.options.conflict();
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
    confirmation?: { response: unknown },
  ): Promise<FinancialDraftRecord<D, P> | null> {
    return this.transact(scope, targetId, 'readwrite', (current, proofs) => {
      if (
        current?.pending?.operationKey === operationKey &&
        outcome === 'confirmed' &&
        confirmation
      )
        proofs.put({
          userId: scope.userId,
          shopId: scope.shopId,
          targetId,
          operationKey,
          ...structuredClone(confirmation),
        });
      return current?.pending?.operationKey === operationKey
        ? {
            ...current,
            revision: current.revision + 1,
            pending: null,
            confirmedOperationKey: outcome === 'confirmed' ? operationKey : null,
            updatedAt: new Date().toISOString(),
          }
        : current;
    });
  }
  /** The current durable status, rather than a pre-HTTP snapshot, owns failure settlement. */
  async settleFailure(
    scope: ScopeOwner,
    targetId: string,
    operationKey: string,
    error: unknown,
    recovery: boolean,
  ): Promise<{
    record: FinancialDraftRecord<D, P> | null;
    outcome: 'unknown' | 'rejected';
    matched: boolean;
  }> {
    let outcome: 'unknown' | 'rejected' = 'unknown';
    let matched = false;
    const record = await this.transact(scope, targetId, 'readwrite', (current) => {
      if (current?.pending?.operationKey !== operationKey) return current;
      matched = true;
      outcome = classifyDraftFailure(error, recovery || current.pending.status === 'unknown');
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

  async confirmation(
    scope: ScopeOwner,
    targetId: string,
    operationKey: string,
  ): Promise<{ response?: unknown } | null> {
    let result: { response?: unknown } | null = null;
    await this.transact(scope, targetId, 'readonly', (current, proofs) => {
      const request = proofs.get([scope.userId, scope.shopId, targetId, operationKey]);
      request.onsuccess = () => {
        result = request.result ?? null;
      };
      return current;
    });
    return result;
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
        throw this.options.conflict();
      return null;
    });
  }
  private payload(draft: D, operationKey: string): P {
    return this.options.payload(draft, operationKey);
  }

  private async transact(
    scope: ScopeOwner,
    targetId: string,
    mode: IDBTransactionMode,
    update: (
      current: FinancialDraftRecord<D, P> | null,
      proofs: IDBObjectStore,
    ) => FinancialDraftRecord<D, P> | null,
    preventCompletedTarget = false,
    pendingPrefix?: string,
  ): Promise<FinancialDraftRecord<D, P> | null> {
    if (!scope.userId || !scope.shopId || !targetId)
      throw new Error(`${this.options.diagnosticName} owner is incomplete.`);
    const db = await this.open();
    try {
      return await new Promise((resolve, reject) => {
        const tx = db.transaction([this.store, this.confirmations], mode);
        const store = tx.objectStore(this.store);
        const key = [scope.userId, scope.shopId, targetId];
        let result: FinancialDraftRecord<D, P> | null = null;
        let failure: unknown;
        tx.oncomplete = () => {
          resolve(result);
          if (mode === 'readwrite') this.notify();
        };
        tx.onabort = tx.onerror = () =>
          reject(
            failure ??
              tx.error ??
              new Error(`${this.options.diagnosticName} storage transaction failed.`),
          );
        const request = store.get(key);
        request.onsuccess = () => {
          const applyUpdate = () => {
            try {
              const current = (request.result as FinancialDraftRecord<D, P> | undefined) ?? null;
              result = update(current, tx.objectStore(this.confirmations));
              if (mode === 'readwrite') {
                const operationKey =
                  result?.confirmedOperationKey ??
                  (result === null ? current?.confirmedOperationKey : null);
                if (operationKey) {
                  const proofs = tx.objectStore(this.confirmations);
                  const proof = proofs.get([scope.userId, scope.shopId, targetId, operationKey]);
                  proof.onsuccess = () => {
                    if (!proof.result)
                      proofs.put({
                        userId: scope.userId,
                        shopId: scope.shopId,
                        targetId,
                        operationKey,
                      });
                  };
                }
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
                  failure = this.options.conflict();
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
              const prior = (peers.result as FinancialDraftRecord<D, P>[]).find(
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
      const request = indexedDB.open(this.options.namespace, 3);
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
        reject(new Error(`${this.options.diagnosticName} storage is blocked.`));
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
