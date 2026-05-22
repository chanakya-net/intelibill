import { Injectable } from '@angular/core';

import { OfflineQueueSyncResult, type OfflineQueuedSalePayload, type OfflineQueuedSaleRecord, type OfflineSaleQueueStatus } from './offline-sale-core.types';

interface SavePendingInput {
  readonly shopId: string;
  readonly deviceId: string;
  readonly clientSaleId: string;
  readonly idempotencyKey: string;
  readonly invoiceNumber: string;
  readonly soldAt: string;
  readonly payload: OfflineQueuedSalePayload;
  readonly warnings?: readonly string[];
}

const RESOLVED_STATUSES: readonly OfflineSaleQueueStatus[] = ['Synced'];

@Injectable({ providedIn: 'root' })
export class OfflineSalesQueueIndexedDbService {
  private readonly databaseName = 'intelibill-offline-sales-queue';
  private readonly databaseVersion = 1;
  private readonly storeName = 'queued-sales';

  async savePendingSale(input: SavePendingInput): Promise<OfflineQueuedSaleRecord> {
    const now = new Date().toISOString();
    const record: OfflineQueuedSaleRecord = {
      key: this.buildKey(input.shopId, input.deviceId, input.clientSaleId),
      shopId: input.shopId,
      deviceId: input.deviceId,
      clientSaleId: input.clientSaleId,
      idempotencyKey: input.idempotencyKey,
      invoiceNumber: input.invoiceNumber,
      soldAt: input.soldAt,
      payload: input.payload,
      status: 'Pending',
      warnings: input.warnings ?? [],
      errorCode: null,
      errorMessage: null,
      serverSaleId: null,
      createdAt: now,
      updatedAt: now,
      syncAttemptCount: 0,
      lastSyncAttemptAt: null,
      syncAttempts: [],
    };

    await this.putRecord(record);
    return record;
  }

  async getPendingSales(shopId: string, deviceId: string): Promise<readonly OfflineQueuedSaleRecord[]> {
    const records = await this.getAllByShopDevice(shopId, deviceId);
    return records
      .filter((record) => record.status === 'Pending')
      .sort((a, b) => Date.parse(a.soldAt) - Date.parse(b.soldAt));
  }

  async getRetryableSales(shopId: string, deviceId: string): Promise<readonly OfflineQueuedSaleRecord[]> {
    const records = await this.getAllByShopDevice(shopId, deviceId);
    return records
      .filter((record) => record.status === 'Pending' || record.status === 'Failed')
      .sort((a, b) => Date.parse(a.soldAt) - Date.parse(b.soldAt));
  }

  async getQueuedSale(shopId: string, deviceId: string, clientSaleId: string): Promise<OfflineQueuedSaleRecord | null> {
    if (!shopId || !deviceId || !clientSaleId) {
      return null;
    }

    return await this.getRecord(shopId, deviceId, clientSaleId);
  }

  async markSyncInProgress(shopId: string, deviceId: string, clientSaleId: string): Promise<OfflineQueuedSaleRecord | null> {
    return await this.updateRecord(shopId, deviceId, clientSaleId, (current) => ({
      ...current,
      status: 'Syncing',
      syncAttemptCount: current.syncAttemptCount + 1,
      lastSyncAttemptAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    }));
  }

  async applySyncResult(
    shopId: string,
    deviceId: string,
    clientSaleId: string,
    result: OfflineQueueSyncResult
  ): Promise<OfflineQueuedSaleRecord | null> {
    return await this.updateRecord(shopId, deviceId, clientSaleId, (current) => ({
      ...current,
      status: result.status,
      warnings: result.warnings ?? current.warnings,
      errorCode: result.errorCode ?? null,
      errorMessage: result.errorMessage ?? null,
      serverSaleId: result.serverSaleId ?? current.serverSaleId,
      updatedAt: new Date().toISOString(),
      syncAttempts: [
        ...current.syncAttempts,
        {
          attemptedAt: new Date().toISOString(),
          ok: result.status === 'Synced' || result.status === 'SyncedWithWarnings',
          errorCode: result.errorCode ?? undefined,
          errorMessage: result.errorMessage ?? undefined,
        },
      ],
    }));
  }

  async getStatusCounts(shopId: string, deviceId: string): Promise<Record<OfflineSaleQueueStatus, number>> {
    const base: Record<OfflineSaleQueueStatus, number> = {
      Pending: 0,
      Syncing: 0,
      Synced: 0,
      SyncedWithWarnings: 0,
      NeedsReview: 0,
      Failed: 0,
    };
    const records = await this.getAllByShopDevice(shopId, deviceId);
    for (const record of records) {
      base[record.status] += 1;
    }
    return base;
  }

  async deleteOldSyncedRecords(retainForMs: number = 3 * 24 * 60 * 60 * 1000): Promise<number> {
    const database = await this.openDatabase();
    const all = await this.getAll(database);
    const now = Date.now();

    const stale = all.filter((record) => {
      if (!RESOLVED_STATUSES.includes(record.status)) return false;
      const updatedAtMs = Date.parse(record.updatedAt);
      return !Number.isNaN(updatedAtMs) && now - updatedAtMs > retainForMs;
    });

    await new Promise<void>((resolve, reject) => {
      const tx = database.transaction(this.storeName, 'readwrite');
      const store = tx.objectStore(this.storeName);
      stale.forEach((record) => store.delete(record.key));
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error);
      tx.onabort = () => reject(tx.error);
    });

    return stale.length;
  }

  private async updateRecord(
    shopId: string,
    deviceId: string,
    clientSaleId: string,
    updater: (record: OfflineQueuedSaleRecord) => OfflineQueuedSaleRecord
  ): Promise<OfflineQueuedSaleRecord | null> {
    const current = await this.getRecord(shopId, deviceId, clientSaleId);
    if (!current) return null;

    const updated = updater(current);
    await this.putRecord(updated);
    return updated;
  }

  private async getRecord(shopId: string, deviceId: string, clientSaleId: string): Promise<OfflineQueuedSaleRecord | null> {
    const database = await this.openDatabase();
    const key = this.buildKey(shopId, deviceId, clientSaleId);

    return await new Promise((resolve, reject) => {
      const tx = database.transaction(this.storeName, 'readonly');
      const req = tx.objectStore(this.storeName).get(key);
      req.onsuccess = () => resolve((req.result as OfflineQueuedSaleRecord | undefined) ?? null);
      req.onerror = () => reject(req.error);
    });
  }

  async getAllByShopDevice(shopId: string, deviceId: string): Promise<readonly OfflineQueuedSaleRecord[]> {
    const database = await this.openDatabase();
    const all = await this.getAll(database);
    return all.filter((record) => record.shopId === shopId && record.deviceId === deviceId);
  }

  private async getAll(database: IDBDatabase): Promise<readonly OfflineQueuedSaleRecord[]> {
    return await new Promise((resolve, reject) => {
      const tx = database.transaction(this.storeName, 'readonly');
      const req = tx.objectStore(this.storeName).getAll();
      req.onsuccess = () => resolve((req.result as OfflineQueuedSaleRecord[]) ?? []);
      req.onerror = () => reject(req.error);
    });
  }

  private async putRecord(record: OfflineQueuedSaleRecord): Promise<void> {
    const database = await this.openDatabase();
    await new Promise<void>((resolve, reject) => {
      const tx = database.transaction(this.storeName, 'readwrite');
      const req = tx.objectStore(this.storeName).put(record);
      req.onerror = () => reject(req.error);
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error ?? req.error);
      tx.onabort = () => reject(tx.error ?? req.error);
    });
  }

  private buildKey(shopId: string, deviceId: string, clientSaleId: string): string {
    return `${shopId}::${deviceId}::${clientSaleId}`;
  }

  private async openDatabase(): Promise<IDBDatabase> {
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
