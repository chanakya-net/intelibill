import { Injectable } from '@angular/core';

export interface SalesCartDraftItem {
  readonly barcode: string;
  readonly itemName: string;
  readonly batchNumber: string;
  readonly inventoryBatchId: string;
  readonly quantity: number;
  readonly availableQuantity: number;
  readonly salesPrice: number;
  readonly mrp: number;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
  readonly costPrice: number;
}

interface SalesCartRecord {
  readonly shopId: string;
  readonly items: readonly SalesCartDraftItem[];
  readonly updatedAt: string;
}

@Injectable({ providedIn: 'root' })
export class SalesCartIndexedDbService {
  private readonly databaseName = 'intelibill-sales-cart';
  private readonly databaseVersion = 1;
  private readonly storeName = 'sales-cart';

  async loadCart(shopId: string, maxAgeMs: number): Promise<readonly SalesCartDraftItem[]> {
    if (!shopId || typeof indexedDB === 'undefined') {
      return [];
    }

    const database = await this.openDatabase();
    const record = await new Promise<SalesCartRecord | undefined>((resolve, reject) => {
      const transaction = database.transaction(this.storeName, 'readonly');
      const request = transaction.objectStore(this.storeName).get(shopId);

      request.onsuccess = () => resolve(request.result as SalesCartRecord | undefined);
      request.onerror = () => reject(request.error);
    });

    if (!record) {
      return [];
    }

    const updatedAtMs = Date.parse(record.updatedAt);
    const isExpired =
      Number.isNaN(updatedAtMs) ||
      maxAgeMs <= 0 ||
      Date.now() - updatedAtMs > maxAgeMs;

    if (isExpired) {
      await this.clearCart(shopId);
      return [];
    }

    const items = record.items ?? [];
    if (!Array.isArray(items) || items.length === 0) {
      return [];
    }

    if (items.some((item) => !this.isPersistedCartItem(item))) {
      await this.clearCart(shopId);
      return [];
    }

    return items;
  }

  async saveCart(shopId: string, items: readonly SalesCartDraftItem[]): Promise<void> {
    if (!shopId || typeof indexedDB === 'undefined') {
      return;
    }

    const database = await this.openDatabase();
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction(this.storeName, 'readwrite');
      const request = transaction.objectStore(this.storeName).put({
        shopId,
        items,
        updatedAt: new Date().toISOString(),
      } as SalesCartRecord);

      request.onsuccess = () => resolve();
      request.onerror = () => reject(request.error);
    });
  }

  async clearCart(shopId: string): Promise<void> {
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

  private isPersistedCartItem(item: unknown): item is SalesCartDraftItem {
    if (!item || typeof item !== 'object') {
      return false;
    }

    return typeof (item as { inventoryBatchId?: unknown }).inventoryBatchId === 'string';
  }
}
