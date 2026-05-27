import { Injectable } from '@angular/core';

export interface OfflineShadowStockRecord {
  readonly key: string;
  readonly shopId: string;
  readonly deviceId: string;
  readonly inventoryBatchId: string;
  readonly quantity: number;
  readonly updatedAt: string;
}

@Injectable({ providedIn: 'root' })
export class OfflineSalesShadowStockIndexedDbService {
  private readonly databaseName = 'intelibill-offline-shadow-stock';
  private readonly databaseVersion = 1;
  private readonly storeName = 'shadow-stock';

  async getQuantity(shopId: string, deviceId: string, inventoryBatchId: string): Promise<number | null> {
    if (!shopId || !deviceId || !inventoryBatchId || !this.isIndexedDbAvailable()) {
      return null;
    }

    const record = await this.getRecord(shopId, deviceId, inventoryBatchId);
    return record?.quantity ?? null;
  }

  async ensureQuantity(shopId: string, deviceId: string, inventoryBatchId: string, initialQuantity: number): Promise<number> {
    if (!shopId || !deviceId || !inventoryBatchId || !this.isIndexedDbAvailable()) {
      return Math.max(0, initialQuantity);
    }

    const existing = await this.getRecord(shopId, deviceId, inventoryBatchId);
    if (existing) return existing.quantity;

    const created: OfflineShadowStockRecord = {
      key: this.buildKey(shopId, deviceId, inventoryBatchId),
      shopId,
      deviceId,
      inventoryBatchId,
      quantity: Math.max(0, initialQuantity),
      updatedAt: new Date().toISOString(),
    };
    await this.putRecord(created);
    return created.quantity;
  }

  async reduceQuantity(shopId: string, deviceId: string, inventoryBatchId: string, quantity: number): Promise<number> {
    if (!shopId || !deviceId || !inventoryBatchId || !this.isIndexedDbAvailable()) {
      return 0;
    }

    const current = await this.ensureQuantity(shopId, deviceId, inventoryBatchId, 0);
    const next = Math.max(0, current - Math.max(0, quantity));

    await this.putRecord({
      key: this.buildKey(shopId, deviceId, inventoryBatchId),
      shopId,
      deviceId,
      inventoryBatchId,
      quantity: next,
      updatedAt: new Date().toISOString(),
    });

    return next;
  }

  private async getRecord(shopId: string, deviceId: string, inventoryBatchId: string): Promise<OfflineShadowStockRecord | null> {
    const database = await this.openDatabase();
    return await new Promise((resolve, reject) => {
      const tx = database.transaction(this.storeName, 'readonly');
      const req = tx.objectStore(this.storeName).get(this.buildKey(shopId, deviceId, inventoryBatchId));
      req.onsuccess = () => resolve((req.result as OfflineShadowStockRecord | undefined) ?? null);
      req.onerror = () => reject(req.error);
    });
  }

  private async putRecord(record: OfflineShadowStockRecord): Promise<void> {
    const database = await this.openDatabase();
    await new Promise<void>((resolve, reject) => {
      const tx = database.transaction(this.storeName, 'readwrite');
      const req = tx.objectStore(this.storeName).put(record);
      req.onsuccess = () => resolve();
      req.onerror = () => reject(req.error);
    });
  }

  private buildKey(shopId: string, deviceId: string, inventoryBatchId: string): string {
    return `${shopId}::${deviceId}::${inventoryBatchId}`;
  }

  private isIndexedDbAvailable(): boolean {
    return typeof indexedDB !== 'undefined';
  }

  private ensureIndexedDbAvailable(): void {
    if (!this.isIndexedDbAvailable()) {
      throw new Error('IndexedDB is not available.');
    }
  }

  private async openDatabase(): Promise<IDBDatabase> {
    this.ensureIndexedDbAvailable();

    return await new Promise((resolve, reject) => {
      const request = indexedDB.open(this.databaseName, this.databaseVersion);

      request.onupgradeneeded = () => {
        const database = request.result;
        if (!database.objectStoreNames.contains(this.storeName)) {
          database.createObjectStore(this.storeName, { keyPath: 'key' });
        }
      };

      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }
}
