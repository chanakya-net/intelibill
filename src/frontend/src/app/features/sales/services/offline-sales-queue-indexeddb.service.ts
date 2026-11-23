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

/** Last owner-side word on an issued invoice, asked inside the queue transaction just before the row is written. */
export interface PendingSaleWriteGuard {
  /** Synchronous: false refuses the write, and nothing is stored. */
  readonly stillAllowed: () => boolean;
  /** Aborting while the transaction can still abort leaves no row behind. */
  readonly signal?: AbortSignal;
}

export class OfflineQueueWriteRefusedError extends Error {
  constructor() {
    super('Offline queue write was refused before persistence.');
    this.name = 'OfflineQueueWriteRefusedError';
  }
}

const RESOLVED_STATUSES: readonly OfflineSaleQueueStatus[] = ['Synced'];
const EMPTY_STATUS_COUNTS: Record<OfflineSaleQueueStatus, number> = {
  Pending: 0,
  Syncing: 0,
  Synced: 0,
  SyncedWithWarnings: 0,
  NeedsReview: 0,
  Failed: 0,
};

@Injectable({ providedIn: 'root' })
export class OfflineSalesQueueIndexedDbService {
  private readonly databaseName = 'intelibill-offline-sales-queue';
  private readonly databaseVersion = 1;
  private readonly storeName = 'queued-sales';

  async savePendingSale(input: SavePendingInput, guard?: PendingSaleWriteGuard): Promise<OfflineQueuedSaleRecord> {
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

    this.ensureIndexedDbAvailable();

    await this.putRecord(record, guard);
    return record;
  }

  async getPendingSales(shopId: string, deviceId: string): Promise<readonly OfflineQueuedSaleRecord[]> {
    if (!shopId || !deviceId || !this.isIndexedDbAvailable()) {
      return [];
    }

    const records = await this.getAllByShopDevice(shopId, deviceId);
    return records
      .filter((record) => record.status === 'Pending')
      .sort((a, b) => Date.parse(a.soldAt) - Date.parse(b.soldAt));
  }

  async getRetryableSales(shopId: string, deviceId: string): Promise<readonly OfflineQueuedSaleRecord[]> {
    if (!shopId || !deviceId || !this.isIndexedDbAvailable()) {
      return [];
    }

    const records = await this.getAllByShopDevice(shopId, deviceId);
    return records
      .filter((record) => record.status === 'Pending' || record.status === 'Failed')
      .sort((a, b) => Date.parse(a.soldAt) - Date.parse(b.soldAt));
  }

  async getQueuedSale(shopId: string, deviceId: string, clientSaleId: string): Promise<OfflineQueuedSaleRecord | null> {
    if (!shopId || !deviceId || !clientSaleId || !this.isIndexedDbAvailable()) {
      return null;
    }

    return await this.getRecord(shopId, deviceId, clientSaleId);
  }

  async markSyncInProgress(shopId: string, deviceId: string, clientSaleId: string): Promise<OfflineQueuedSaleRecord | null> {
    if (!shopId || !deviceId || !clientSaleId || !this.isIndexedDbAvailable()) {
      return null;
    }

    return await this.updateRecord(shopId, deviceId, clientSaleId, (current) => {
      const now = new Date().toISOString();
      return {
        ...current,
        status: 'Syncing',
        syncAttemptCount: (current.syncAttemptCount ?? 0) + 1,
        lastSyncAttemptAt: now,
        updatedAt: now,
      };
    });
  }

  async applySyncResult(
    shopId: string,
    deviceId: string,
    clientSaleId: string,
    result: OfflineQueueSyncResult
  ): Promise<OfflineQueuedSaleRecord | null> {
    if (!shopId || !deviceId || !clientSaleId || !this.isIndexedDbAvailable()) {
      return null;
    }

    return await this.updateRecord(shopId, deviceId, clientSaleId, (current) => {
      const now = new Date().toISOString();
      return {
        ...current,
        status: result.status,
        warnings: result.warnings ?? current.warnings ?? [],
        errorCode: result.errorCode ?? null,
        errorMessage: result.errorMessage ?? null,
        serverSaleId: result.serverSaleId ?? current.serverSaleId ?? null,
        reviewCaseId: result.reviewCaseId ?? null,
        mismatches: result.mismatches ?? [],
        updatedAt: now,
        syncAttempts: [
          ...(current.syncAttempts ?? []),
          {
            attemptedAt: now,
            ok: result.status === 'Synced' || result.status === 'SyncedWithWarnings',
            errorCode: result.errorCode ?? undefined,
            errorMessage: result.errorMessage ?? undefined,
          },
        ],
      };
    });
  }

  async getStatusCounts(shopId: string, deviceId: string): Promise<Record<OfflineSaleQueueStatus, number>> {
    const base = { ...EMPTY_STATUS_COUNTS };
    if (!shopId || !deviceId || !this.isIndexedDbAvailable()) {
      return base;
    }

    const records = await this.getAllByShopDevice(shopId, deviceId);
    for (const record of records) {
      base[record.status] += 1;
    }
    return base;
  }

  async deleteOldSyncedRecords(retainForMs: number = 3 * 24 * 60 * 60 * 1000): Promise<number> {
    if (!this.isIndexedDbAvailable()) {
      return 0;
    }

    const database = await this.openDatabase();
    // Stored rows, not the read projection: a row whose last status update was never recorded is kept.
    const all = await this.getAllStored(database);
    const now = Date.now();

    const stale = all.filter((record) => {
      if (!RESOLVED_STATUSES.includes(record.status)) return false;
      if (typeof record.updatedAt !== 'string') return false;
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
    const database = await this.openDatabase();
    const key = this.buildKey(shopId, deviceId, clientSaleId);

    return await new Promise((resolve, reject) => {
      const tx = database.transaction(this.storeName, 'readwrite');
      const store = tx.objectStore(this.storeName);
      const getReq = store.get(key);
      let updated: OfflineQueuedSaleRecord | null = null;
      let failed = false;

      getReq.onsuccess = () => {
        const raw = getReq.result as OfflineQueuedSaleRecord | undefined;
        if (!raw) return;
        updated = updater(raw);
        const putReq = store.put(updated);
        putReq.onerror = () => {
          failed = true;
          reject(putReq.error);
        };
      };
      getReq.onerror = () => {
        failed = true;
        reject(getReq.error);
      };
      tx.oncomplete = () => {
        if (!failed) resolve(updated ? this.toCompatibleReadRecord(updated) : null);
      };
      tx.onerror = () => {
        if (!failed) reject(tx.error ?? getReq.error);
      };
      tx.onabort = () => {
        if (!failed) reject(tx.error ?? getReq.error);
      };
    });
  }

  private async getRecord(shopId: string, deviceId: string, clientSaleId: string): Promise<OfflineQueuedSaleRecord | null> {
    const database = await this.openDatabase();
    const key = this.buildKey(shopId, deviceId, clientSaleId);

    return await new Promise((resolve, reject) => {
      const tx = database.transaction(this.storeName, 'readonly');
      const req = tx.objectStore(this.storeName).get(key);
      req.onsuccess = () => {
        const raw = req.result as OfflineQueuedSaleRecord | undefined;
        resolve(raw ? this.toCompatibleReadRecord(raw) : null);
      };
      req.onerror = () => reject(req.error);
    });
  }

  async getAllByShopDevice(shopId: string, deviceId: string): Promise<readonly OfflineQueuedSaleRecord[]> {
    if (!shopId || !deviceId || !this.isIndexedDbAvailable()) {
      return [];
    }

    const database = await this.openDatabase();
    const all = await this.getAll(database);
    return all.filter((record) => record.shopId === shopId && record.deviceId === deviceId);
  }

  private async getAll(database: IDBDatabase): Promise<readonly OfflineQueuedSaleRecord[]> {
    return (await this.getAllStored(database)).map((row) => this.toCompatibleReadRecord(row));
  }

  /** Rows exactly as stored, so durable decisions never rest on values the read projection fills in. */
  private async getAllStored(database: IDBDatabase): Promise<readonly OfflineQueuedSaleRecord[]> {
    return await new Promise((resolve, reject) => {
      const tx = database.transaction(this.storeName, 'readonly');
      const req = tx.objectStore(this.storeName).getAll();
      req.onsuccess = () => resolve((req.result as OfflineQueuedSaleRecord[] | undefined) ?? []);
      req.onerror = () => reject(req.error);
    });
  }

  /**
   * Projects a stored v1 row into the current read model without cloning or mutating its frozen `payload`
   * and without writing anything back to IndexedDB.
   */
  private toCompatibleReadRecord(raw: OfflineQueuedSaleRecord): OfflineQueuedSaleRecord {
    const hasAllMetadata =
      Array.isArray(raw.warnings)
      && raw.errorCode !== undefined
      && raw.errorMessage !== undefined
      && raw.serverSaleId !== undefined
      && typeof raw.syncAttemptCount === 'number'
      && raw.lastSyncAttemptAt !== undefined
      && Array.isArray(raw.syncAttempts)
      && typeof raw.createdAt === 'string'
      && typeof raw.updatedAt === 'string';
    if (hasAllMetadata) {
      return raw;
    }

    return {
      ...raw,
      warnings: Array.isArray(raw.warnings) ? raw.warnings : [],
      errorCode: raw.errorCode ?? null,
      errorMessage: raw.errorMessage ?? null,
      serverSaleId: raw.serverSaleId ?? null,
      createdAt: raw.createdAt ?? raw.soldAt,
      updatedAt: raw.updatedAt ?? raw.soldAt,
      syncAttemptCount: typeof raw.syncAttemptCount === 'number' ? raw.syncAttemptCount : 0,
      lastSyncAttemptAt: raw.lastSyncAttemptAt ?? null,
      syncAttempts: Array.isArray(raw.syncAttempts) ? raw.syncAttempts : [],
    };
  }

  private async putRecord(record: OfflineQueuedSaleRecord, guard?: PendingSaleWriteGuard): Promise<void> {
    const database = await this.openDatabase();
    await new Promise<void>((resolve, reject) => {
      // Asked after the database opened and in the same task as the write, so no await separates check from insert.
      if (guard && (guard.signal?.aborted || !guard.stillAllowed())) {
        reject(new OfflineQueueWriteRefusedError());
        return;
      }
      const tx = database.transaction(this.storeName, 'readwrite');
      const req = tx.objectStore(this.storeName).put(record);
      let refused = false;
      const abort = () => {
        refused = true;
        try {
          tx.abort();
        } catch {
          // Already committing: the row is durable and the caller settles it as issued.
          refused = false;
        }
      };
      guard?.signal?.addEventListener('abort', abort, { once: true });
      const done = () => guard?.signal?.removeEventListener('abort', abort);
      req.onerror = () => reject(refused ? new OfflineQueueWriteRefusedError() : req.error);
      // Resolves only once the row is durable; confirmation and stock reduction wait for this.
      tx.oncomplete = () => {
        done();
        resolve();
      };
      tx.onerror = () => {
        done();
        reject(tx.error ?? req.error);
      };
      tx.onabort = () => {
        done();
        reject(refused ? new OfflineQueueWriteRefusedError() : (tx.error ?? req.error));
      };
    });
  }

  private buildKey(shopId: string, deviceId: string, clientSaleId: string): string {
    return `${shopId}::${deviceId}::${clientSaleId}`;
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
