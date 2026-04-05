import { Injectable } from '@angular/core';

export interface InventoryInboundDraftRow {
  readonly clientRowId: string;
  readonly itemName: string;
  readonly barcode: string;
  readonly itemDescription: string | null;
  readonly uom: string;
  readonly batchNumber: string;
  readonly quantity: number;
  readonly costPrice: number;
  readonly mrp: number;
  readonly salesPrice: number;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
  readonly expiryDate: string | null;
  readonly manufacturingDate: string | null;
  readonly supplierId: string | null;
  readonly referenceNumber: string | null;
  readonly notes: string | null;
  readonly performedAt: string | null;
}

interface DraftRecord {
  readonly shopId: string;
  readonly rows: readonly InventoryInboundDraftRow[];
  readonly updatedAt: string;
}

@Injectable({ providedIn: 'root' })
export class InventoryDraftIndexedDbService {
  private readonly databaseName = 'intelibill-offline-drafts';
  private readonly databaseVersion = 1;
  private readonly storeName = 'inventory-inbound-batch';

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

  private async openDatabase(): Promise<IDBDatabase> {
    return await new Promise((resolve, reject) => {
      const request = indexedDB.open(this.databaseName, this.databaseVersion);

      request.onupgradeneeded = () => {
        const database = request.result;
        if (!database.objectStoreNames.contains(this.storeName)) {
          database.createObjectStore(this.storeName, { keyPath: 'shopId' });
        }
      };

      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }
}
