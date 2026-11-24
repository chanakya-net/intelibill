import { Injectable } from '@angular/core';

import {
  evaluateOfflineClock,
  rebaseOfflineClock,
  type OfflineClockCheckpoint,
  type OfflineClockObservation,
  type OfflineClockOwner,
  type OfflineClockSample,
  type OfflineClockVerdict,
} from '../../features/sales/utils/offline-permit-clock';

interface ClockRecord {
  readonly key: string;
  readonly checkpoint: OfflineClockCheckpoint;
  readonly updatedAt: string;
}

/** Identifies this document: a monotonic reading only means something against a baseline of the same document. */
const DOCUMENT_ID =
  typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : `doc-${Math.random().toString(36).slice(2)}`;

/**
 * Keeps the clock checkpoint beside the lease it belongs to (same database and store, its own key). Every read and
 * write of it happens in one readwrite transaction with the sample taken inside it, so two tabs cannot interleave.
 */
@Injectable({ providedIn: 'root' })
export class OfflinePermitClockIndexedDbService {
  private readonly databaseName = 'intelibill-invoice-leases';
  private readonly databaseVersion = 1;
  private readonly storeName = 'invoice-leases';
  private readonly baselines = new Map<string, OfflineClockSample>();

  /** A missing checkpoint after an interrupted write needs a fresh bound stream on reconnect. */
  async hasCheckpoint(owner: OfflineClockOwner, permitId: string): Promise<boolean> {
    return this.readModifyWrite(this.buildKey(owner), stored => ({
      result: stored?.sessionId === owner.sessionId && stored?.permitId === permitId, checkpoint: null,
    }));
  }

  /** Judges the clocks now. A detected discrepancy is stored before this answers, whatever the caller does next. */
  async evaluate(owner: OfflineClockOwner, permitId: string): Promise<OfflineClockVerdict> {
    if (typeof indexedDB === 'undefined') return 'unverified';
    const key = this.buildKey(owner);
    return await this.readModifyWrite<OfflineClockVerdict>(key, (stored) => {
      const sample = this.sample();
      const baselineKey = `${key}::${permitId}`;
      const evaluation = evaluateOfflineClock(stored, owner, permitId, sample, this.baselines.get(baselineKey) ?? null);
      if (evaluation.verdict === 'consistent' && !this.baselines.has(baselineKey)) this.baselines.set(baselineKey, sample);
      return { result: evaluation.verdict, checkpoint: evaluation.checkpoint };
    });
  }

  /** Adopts a freshly verified signed server time; true when the checkpoint was established or rebased. */
  async rebase(observation: OfflineClockObservation): Promise<boolean> {
    if (typeof indexedDB === 'undefined') return false;
    const key = this.buildKey(observation);
    return await this.readModifyWrite<boolean>(key, (stored) => {
      const sample = this.sample();
      const checkpoint = rebaseOfflineClock(stored, observation, sample);
      if (checkpoint) this.baselines.set(`${key}::${observation.permitId}`, sample);
      return { result: checkpoint !== null, checkpoint };
    });
  }

  private sample(): OfflineClockSample {
    return {
      documentId: DOCUMENT_ID,
      wallMs: Date.now(),
      monotonicMs: typeof performance !== 'undefined' ? performance.now() : Number.NaN,
    };
  }

  private buildKey(owner: OfflineClockOwner): string {
    return `clock::${owner.shopId}::${owner.deviceId}::${owner.userId}`;
  }

  private async readModifyWrite<T>(
    key: string,
    decide: (stored: OfflineClockCheckpoint | null) => { result: T; checkpoint: OfflineClockCheckpoint | null },
  ): Promise<T> {
    const database = await this.openDatabase();
    return await new Promise<T>((resolve, reject) => {
      const transaction = database.transaction(this.storeName, 'readwrite');
      const store = transaction.objectStore(this.storeName);
      const request = store.get(key);
      let result: T;
      request.onerror = () => reject(request.error);
      request.onsuccess = () => {
        const decided = decide((request.result as ClockRecord | undefined)?.checkpoint ?? null);
        result = decided.result;
        if (decided.checkpoint) {
          store.put({ key, checkpoint: decided.checkpoint, updatedAt: new Date().toISOString() } satisfies ClockRecord);
        }
      };
      transaction.oncomplete = () => resolve(result);
      transaction.onabort = () => reject(transaction.error);
    });
  }

  private async openDatabase(): Promise<IDBDatabase> {
    return await new Promise((resolve, reject) => {
      const request = indexedDB.open(this.databaseName, this.databaseVersion);
      request.onupgradeneeded = () => {
        if (!request.result.objectStoreNames.contains(this.storeName)) {
          request.result.createObjectStore(this.storeName, { keyPath: 'key' });
        }
      };
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }
}
