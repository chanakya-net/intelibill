import { Injectable } from '@angular/core';

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
  readonly hsnCode: string | null;
  readonly expiryDate: string | null;
  readonly manufacturingDate: string | null;
  readonly supplierId: string | null;
  readonly referenceNumber: string | null;
  readonly notes: string | null;
  readonly performedAt: string | null;
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
  readonly chunks: readonly InventoryInboundSubmissionChunk[];
  readonly createdAt: string;
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

  async loadRows(shopId: string): Promise<readonly InventoryInboundDraftRow[]> {
    if (!shopId || typeof indexedDB === 'undefined') {
      return [];
    }

    const database = await this.openDatabase();
    return await new Promise((resolve, reject) => {
      const transaction = database.transaction(this.storeName, 'readonly');
      const request = transaction.objectStore(this.storeName).get(shopId);

      request.onsuccess = () => {
        const record = request.result as DraftRecord | undefined;
        resolve(record?.rows ?? []);
      };

      request.onerror = () => reject(request.error);
    });
  }

  async saveRows(shopId: string, rows: readonly InventoryInboundDraftRow[]): Promise<void> {
    if (!shopId || typeof indexedDB === 'undefined') {
      return;
    }

    const database = await this.openDatabase();
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction(this.storeName, 'readwrite');
      const request = transaction.objectStore(this.storeName).put({
        shopId,
        rows,
        updatedAt: new Date().toISOString(),
      } as DraftRecord);

      request.onsuccess = () => resolve();
      request.onerror = () => reject(request.error);
    });
  }

  async clearRows(shopId: string): Promise<void> {
    if (!shopId || typeof indexedDB === 'undefined') {
      return;
    }

    const database = await this.openDatabase();
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction(this.storeName, 'readwrite');
      const request = transaction.objectStore(this.storeName).delete(shopId);

      request.onsuccess = () => resolve();
      request.onerror = () => reject(request.error);
    });
  }

  async loadSubmission(shopId: string): Promise<InventoryInboundSubmission | null> {
    if (!shopId || typeof indexedDB === 'undefined') {
      return null;
    }

    const database = await this.openDatabase();
    return await new Promise((resolve, reject) => {
      const transaction = database.transaction(this.submissionStoreName, 'readonly');
      const request = transaction.objectStore(this.submissionStoreName).get(shopId);

      request.onsuccess = () => resolve((request.result as InventoryInboundSubmission | undefined) ?? null);
      request.onerror = () => reject(request.error);
    });
  }

  /** Rejects when the submission cannot be stored; the caller must then send nothing. */
  async saveSubmission(submission: InventoryInboundSubmission): Promise<void> {
    if (typeof indexedDB === 'undefined') {
      throw new Error('IndexedDB is unavailable; a save cannot be made resumable.');
    }

    const database = await this.openDatabase();
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction(this.submissionStoreName, 'readwrite');
      transaction.objectStore(this.submissionStoreName).put(submission);

      // Resolve on commit, not on the request: only a committed write survives a reload.
      transaction.oncomplete = () => resolve();
      transaction.onerror = () => reject(transaction.error);
      transaction.onabort = () => reject(transaction.error);
    });
  }

  async clearSubmission(shopId: string): Promise<void> {
    if (!shopId || typeof indexedDB === 'undefined') {
      return;
    }

    const database = await this.openDatabase();
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction(this.submissionStoreName, 'readwrite');
      const request = transaction.objectStore(this.submissionStoreName).delete(shopId);

      request.onsuccess = () => resolve();
      request.onerror = () => reject(request.error);
    });
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
