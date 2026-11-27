import { Injectable } from '@angular/core';

export interface ProductCatalogEntry {
  readonly itemId: string;
  readonly name: string;
  readonly barcode: string;
}

interface CatalogRecord {
  readonly shopId: string;
  readonly items: readonly ProductCatalogEntry[];
  readonly syncedAt: string;
}

@Injectable({ providedIn: 'root' })
export class ProductCatalogIndexedDbService {
  private readonly databaseName = 'intelibill-product-catalog';
  private readonly databaseVersion = 1;
  private readonly storeName = 'product-catalog';

  async getCatalog(shopId: string): Promise<readonly ProductCatalogEntry[]> {
    if (!shopId || typeof indexedDB === 'undefined') {
      return [];
    }

    const database = await this.openDatabase();
    return await new Promise((resolve, reject) => {
      const transaction = database.transaction(this.storeName, 'readonly');
      const request = transaction.objectStore(this.storeName).get(shopId);

      request.onsuccess = () => {
        const record = request.result as CatalogRecord | undefined;
        resolve(record?.items ?? []);
      };

      request.onerror = () => reject(request.error);
    });
  }

  async saveCatalog(shopId: string, items: readonly ProductCatalogEntry[]): Promise<void> {
    if (!shopId || typeof indexedDB === 'undefined') {
      return;
    }

    const database = await this.openDatabase();
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction(this.storeName, 'readwrite');
      const request = transaction.objectStore(this.storeName).put({
        shopId,
        items,
        syncedAt: new Date().toISOString(),
      } as CatalogRecord);

      request.onsuccess = () => resolve();
      request.onerror = () => reject(request.error);
    });
  }

  async clearCatalog(shopId: string): Promise<void> {
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
