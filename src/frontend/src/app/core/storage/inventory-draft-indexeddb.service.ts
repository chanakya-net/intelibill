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

export function sameIntakeSubmission(
  left: InventoryInboundSubmission,
  right: InventoryInboundSubmission,
): boolean {
  return left.shopId === right.shopId && left.ownerUserId === right.ownerUserId &&
    left.createdAt === right.createdAt && left.chunks[0]?.operationKey === right.chunks[0]?.operationKey;
}

export interface InventoryInboundDraft {
  readonly rows: readonly InventoryInboundDraftRow[];
  readonly submission: InventoryInboundSubmission | null;
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

  async loadDraft(shopId: string, owner?: ScopeOwner): Promise<InventoryInboundDraft> {
    if (!shopId || typeof indexedDB === 'undefined') return { rows: [], submission: null };

    const database = await this.openDatabase();
    try {
      const tx = database.transaction([this.storeName, this.submissionStoreName]);
      const drafts = tx.objectStore(this.storeName);
      const submissions = tx.objectStore(this.submissionStoreName);
      const key = owner ? ownerKey(owner) : null;
      const [draft, legacyDraft, record, legacy] = await Promise.all([
        key ? this.read<DraftRecord>(drafts, key) : undefined,
        this.read<DraftRecord>(drafts, shopId),
        key ? this.read<SubmissionRecord>(submissions, key) : undefined,
        this.read<InventoryInboundSubmission>(submissions, shopId),
      ]);
      if (record && record.submission.ownerUserId === owner?.userId) {
        return { rows: draft?.rows ?? [], submission: record.submission };
      }
      // Existing v2 submissions belong to the shop slot and retain their original payload.
      if (!record && legacy) return { rows: legacyDraft?.rows ?? [], submission: legacy };
      return { rows: (draft ?? legacyDraft)?.rows ?? [], submission: null };
    } finally {
      database.close();
    }
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

  /** Freezes exactly the draft the operator saw, provided no tab has already changed it. */
  async saveSubmission(
    owner: ScopeOwner,
    expectedRows: readonly InventoryInboundDraftRow[],
    freeze: () => InventoryInboundSubmission,
    assertScope: () => void,
  ): Promise<InventoryInboundSubmission | null> {
    const key = ownerKey(owner);
    let saved: InventoryInboundSubmission | null = null;
    await this.write([this.submissionStoreName, this.storeName], (tx) => {
      const store = tx.objectStore(this.submissionStoreName);
      this.readWithin(tx, store, key, current => {
        if (current) return;
        this.readWithin(tx, store, owner.shopId, legacy => {
          if (legacy) return;
          this.readDraftForFreeze(tx, key, owner.shopId, rows => {
            assertScope();
            if (JSON.stringify(rows) !== JSON.stringify(expectedRows)) return;

            const submission = freeze();
            store.add({ shopId: key, submission } satisfies SubmissionRecord);
            const drafts = tx.objectStore(this.storeName);
            drafts.put(this.draftRecord(key, submission.chunks.flatMap(chunk => chunk.rows)));
            drafts.delete(owner.shopId);
            saved = submission;
          });
        });
      });
    });
    return saved;
  }

  private readDraftForFreeze(
    tx: IDBTransaction,
    key: string,
    shopId: string,
    useRows: (rows: readonly InventoryInboundDraftRow[]) => void,
  ): void {
    const drafts = tx.objectStore(this.storeName);
    this.readWithin<DraftRecord>(tx, drafts, key, draft => {
      if (draft) {
        useRows(draft.rows);
        return;
      }
      this.readWithin<DraftRecord>(tx, drafts, shopId, legacy => useRows(legacy?.rows ?? []));
    });
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
        if (!current || !sameIntakeSubmission(current, expected)) return;
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

  private read<T>(store: IDBObjectStore, key: string): Promise<T | undefined> {
    return new Promise((resolve, reject) => {
      const request = store.get(key);
      request.onsuccess = () => resolve(request.result as T | undefined);
      request.onerror = () => reject(request.error);
    });
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
