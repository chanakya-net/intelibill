import { Injectable } from '@angular/core';
import { ownerKey, type ScopeOwner } from '../context/shop-scope';

export interface InventoryInboundDraftRow {
  readonly clientRowId: string;
  readonly itemName: string;
  readonly barcode: string;
  readonly itemDescription: string | null;
  readonly uom: string;
  readonly batchNumber: string;
  readonly quantity: number;
  readonly totalPurchaseCost: number;
  readonly mrp: number;
  readonly salesPrice: number;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
  readonly purchaseTaxIncluded?: boolean;
  /**
   * Set only on rows built after purchase tax mode became its own choice. Earlier releases stored
   * `purchaseTaxIncluded` silently copied from the sales mode, so a row without this marker has an
   * unconfirmed purchase mode whatever its value. Storage-only: never sent to the server.
   */
  readonly purchaseTaxModeConfirmed?: boolean;
  readonly hsnCode: string | null;
  readonly expiryDate: string | null;
  readonly manufacturingDate: string | null;
  readonly supplierId: string | null;
  readonly referenceNumber: string | null;
  readonly notes: string | null;
  readonly performedAt: string | null;
}

/** True when the user chose this row's purchase tax mode (see `purchaseTaxModeConfirmed`). */
export function isPurchaseTaxModeConfirmed(row: InventoryInboundDraftRow): boolean {
  return row.purchaseTaxModeConfirmed === true && typeof row.purchaseTaxIncluded === 'boolean';
}

/** One server request of a frozen submission: its rows exactly as first sent, and its key. */
export interface InventoryInboundSubmissionChunk {
  readonly operationKey: string;
  readonly rows: readonly InventoryInboundDraftRow[];
}

/**
 * A save frozen before its first request. Until every chunk has a definitive answer it is resent
 * unchanged — same keys, same rows, same `performedAt` — so the server replays rows it already
 * committed instead of receiving them twice.
 */
export interface InventoryInboundSubmission {
  readonly shopId: string;
  readonly ownerUserId?: string;
  readonly chunks: readonly InventoryInboundSubmissionChunk[];
  readonly createdAt: string;
}

interface SubmissionRecord {
  readonly shopId: string;
  readonly submission: InventoryInboundSubmission;
}

interface DraftRecord {
  readonly shopId: string;
  readonly rows: readonly InventoryInboundDraftRow[];
  readonly updatedAt: string;
}

@Injectable({ providedIn: 'root' })
export class InventoryDraftIndexedDbService {
  private readonly databaseName = 'intelibill-offline-drafts';
  private readonly databaseVersion = 2;
  private readonly storeName = 'inventory-inbound-batch';
  private readonly submissionStoreName = 'inventory-inbound-submission';

  async loadRows(shopId: string, owner?: ScopeOwner): Promise<readonly InventoryInboundDraftRow[]> {
    if (!shopId || typeof indexedDB === 'undefined') return [];
    return await this.read<DraftRecord>(this.storeName, owner ? ownerKey(owner) : shopId)
      .then(async (record) => record?.rows ?? (owner ? await this.loadRows(shopId) : []));
  }

  async saveRows(shopId: string, rows: readonly InventoryInboundDraftRow[], owner?: ScopeOwner): Promise<void> {
    if (!shopId || typeof indexedDB === 'undefined') return;
    await this.write([this.storeName], (tx) => {
      const store = tx.objectStore(this.storeName);
      store.put(this.draftRecord(owner ? ownerKey(owner) : shopId, rows));
      if (owner) store.delete(shopId);
    });
  }

  async clearRows(shopId: string, owner?: ScopeOwner): Promise<void> {
    await this.saveRows(shopId, [], owner);
  }

  async loadSubmission(shopId: string, owner?: ScopeOwner): Promise<InventoryInboundSubmission | null> {
    if (!shopId || typeof indexedDB === 'undefined') return null;

    if (owner) {
      const scoped = await this.read<SubmissionRecord>(this.submissionStoreName, ownerKey(owner));
      if (scoped) return scoped.submission.ownerUserId === owner.userId ? scoped.submission : null;
    }

    // Existing v2 submissions are keyed by shop. Their owner and frozen payload are left as stored.
    return await this.read<InventoryInboundSubmission>(this.submissionStoreName, shopId) ?? null;
  }

  /** Atomically freezes the submission, or returns the exact operation that won in another tab. */
  async saveSubmission(
    submission: InventoryInboundSubmission,
    assertScope: () => void = () => undefined,
  ): Promise<InventoryInboundSubmission> {
    const key = this.submissionKey(submission);
    let selected = submission;
    await this.write([this.submissionStoreName, this.storeName], (tx) => {
      const store = tx.objectStore(this.submissionStoreName);
      if (!submission.ownerUserId) {
        this.readWithin(tx, store, key, (legacy?: InventoryInboundSubmission) => {
          assertScope();
          selected = legacy ?? submission;
          if (!legacy) store.add(submission);
        });
        return;
      }

      this.readWithin(tx, store, key, (current?: SubmissionRecord) => {
        this.readWithin(tx, store, submission.shopId, (legacy?: InventoryInboundSubmission) => {
          assertScope();
          selected = current?.submission ?? legacy ?? submission;
          if (current || legacy) return;
          store.add({ shopId: key, submission } satisfies SubmissionRecord);
          const drafts = tx.objectStore(this.storeName);
          drafts.put(this.draftRecord(key, submission.chunks.flatMap(chunk => chunk.rows)));
          drafts.delete(submission.shopId);
        });
      });
    });
    return selected;
  }

  async settleSubmission(
    expected: InventoryInboundSubmission,
    savedIds: ReadonlySet<string>,
    complete: boolean,
    assertScope: () => void,
  ): Promise<readonly InventoryInboundDraftRow[] | null> {
    const key = this.submissionKey(expected);
    let remaining: readonly InventoryInboundDraftRow[] | null = null;
    await this.write([this.submissionStoreName, this.storeName], (tx) => {
      const submissions = tx.objectStore(this.submissionStoreName);
      this.readWithin(tx, submissions, key, (record: SubmissionRecord | InventoryInboundSubmission | undefined) => {
        assertScope();
        const current = record && ('submission' in record ? record.submission : record);
        if (!current || current.ownerUserId !== expected.ownerUserId || current.shopId !== expected.shopId ||
            current.createdAt !== expected.createdAt || current.chunks[0]?.operationKey !== expected.chunks[0]?.operationKey) return;
        const drafts = tx.objectStore(this.storeName);
        this.readWithin(tx, drafts, key, (draft?: DraftRecord) => {
          assertScope();
          remaining = (draft?.rows ?? []).filter(row => !savedIds.has(row.clientRowId));
          drafts.put(this.draftRecord(key, remaining));
          if (complete) submissions.delete(key);
        });
      });
    });
    return remaining;
  }

  private draftRecord(key: string, rows: readonly InventoryInboundDraftRow[]): DraftRecord {
    return { shopId: key, rows, updatedAt: new Date().toISOString() };
  }

  private submissionKey({ shopId, ownerUserId }: InventoryInboundSubmission): string {
    return ownerUserId ? ownerKey({ shopId, userId: ownerUserId }) : shopId;
  }

  private async read<T>(name: string, key: string): Promise<T | undefined> {
    const database = await this.openDatabase();
    try {
      return await new Promise((resolve, reject) => {
        const request = database.transaction(name).objectStore(name).get(key);
        request.onsuccess = () => resolve(request.result as T | undefined);
        request.onerror = () => reject(request.error);
      });
    } finally {
      database.close();
    }
  }

  private readWithin<T>(tx: IDBTransaction, store: IDBObjectStore, key: string, apply: (record: T | undefined) => void): void {
    const request = store.get(key);
    request.onsuccess = () => {
      try {
        apply(request.result as T | undefined);
      } catch {
        tx.abort();
      }
    };
  }

  private async write(names: string[], mutate: (transaction: IDBTransaction) => void): Promise<void> {
    const database = await this.openDatabase();
    try {
      await new Promise<void>((resolve, reject) => {
        const transaction = database.transaction(names, 'readwrite');
        transaction.oncomplete = () => resolve();
        transaction.onabort = () => reject(transaction.error ?? new Error('Inventory draft update was aborted.'));
        mutate(transaction);
      });
    } finally {
      database.close();
    }
  }

  private async openDatabase(): Promise<IDBDatabase> {
    return await new Promise((resolve, reject) => {
      const request = indexedDB.open(this.databaseName, this.databaseVersion);

      request.onupgradeneeded = () => {
        const database = request.result;
        if (!database.objectStoreNames.contains(this.storeName)) {
          database.createObjectStore(this.storeName, { keyPath: 'shopId' });
        }
        if (!database.objectStoreNames.contains(this.submissionStoreName)) {
          database.createObjectStore(this.submissionStoreName, { keyPath: 'shopId' });
        }
      };

      request.onsuccess = () => {
        const database = request.result;
        // Let a newer tab upgrade the schema instead of waiting on this connection forever.
        database.onversionchange = () => database.close();
        resolve(database);
      };
      request.onerror = () => reject(request.error);
      request.onblocked = () => reject(new Error('Inventory drafts are open in an older tab; reload it to continue.'));
    });
  }
}
