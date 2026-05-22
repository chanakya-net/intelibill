import { Injectable } from '@angular/core';

export interface OfflineSalesSnapshotMetadata {
  readonly snapshotId: string;
  readonly shopId: string;
  readonly schemaVersion: number;
  readonly startedAt: string;
}

export type OfflineSalesSnapshotAttemptStatus = 'incomplete' | 'complete' | 'failed';

export interface OfflineSalesSnapshotAttemptRecord {
  readonly snapshotId: string;
  readonly shopId: string;
  readonly schemaVersion: number;
  readonly startedAt: string;
  readonly completedAt?: string;
  readonly status: OfflineSalesSnapshotAttemptStatus;
  readonly errorCode?: string;
  readonly errorMessage?: string;
}

export interface OfflineSellableBatchSnapshot {
  readonly batchId: string;
  readonly itemId: string;
  readonly itemName: string;
  readonly barcode: string;
  readonly uom: string;
  readonly hsnCode?: string | null;
  readonly batchNumber: string;
  readonly quantity: number;
  readonly costPrice: number;
  readonly mrp: number;
  readonly salesPrice: number;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
  readonly purchaseTaxIncluded: boolean;
  readonly expiryDate?: string | null;
}

export interface OfflineCustomerLiteSnapshot {
  readonly customerId: string;
  readonly name: string;
  readonly phoneNumber: string;
}

export interface OfflineDiscountRuleSnapshot {
  readonly ruleId: string;
  readonly ruleType: string;
  readonly name: string;
  readonly description?: string | null;
  readonly inventoryBatchId?: string | null;
  readonly percentage: number;
  readonly thresholdAmount?: number | null;
  readonly startsAt?: string | null;
  readonly endsAt?: string | null;
  readonly belowCostConfirmed: boolean;
}

export interface OfflineActiveLeaseSnapshot {
  readonly leaseId: string;
  readonly invoiceSequenceId: string;
  readonly deviceId: string;
  readonly fiscalYearStart: number;
  readonly prefix: string;
  readonly rangeStart: number;
  readonly rangeEnd: number;
  readonly nextNumber: number;
  readonly numberPadding: number;
  readonly reservedAt: string;
  readonly expiresAt: string;
}

interface ShopPointerRecord {
  readonly shopId: string;
  readonly usableSnapshotId?: string;
  readonly usableCompletedAt?: string;
  readonly lastAttemptSnapshotId?: string;
  readonly lastAttemptStartedAt?: string;
  readonly lastAttemptStatus?: OfflineSalesSnapshotAttemptStatus;
}

export interface OfflineUsableSnapshotInfo {
  readonly snapshotId: string;
  readonly completedAt: string;
}

interface SnapshotScopedRecord<T> {
  readonly key: string;
  readonly snapshotId: string;
  readonly shopId: string;
  readonly entityId: string;
  readonly entity: T;
  readonly writtenAt: string;
}

@Injectable({ providedIn: 'root' })
export class OfflineSalesSnapshotIndexedDbService {
  private readonly databaseName = 'intelibill-offline-sales-snapshot';
  private readonly databaseVersion = 1;

  private readonly attemptsStore = 'snapshot-attempts';
  private readonly shopPointersStore = 'shop-pointers';
  private readonly batchesStore = 'batches';
  private readonly customersStore = 'customers';
  private readonly discountRulesStore = 'discount-rules';
  private readonly activeLeasesStore = 'active-leases';

  async beginAttempt(metadata: OfflineSalesSnapshotMetadata): Promise<void> {
    if (!metadata?.snapshotId || !metadata?.shopId || typeof indexedDB === 'undefined') {
      return;
    }

    const database = await this.openDatabase();
    const attempt: OfflineSalesSnapshotAttemptRecord = {
      snapshotId: metadata.snapshotId,
      shopId: metadata.shopId,
      schemaVersion: metadata.schemaVersion,
      startedAt: metadata.startedAt,
      status: 'incomplete',
    };

    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction([this.attemptsStore, this.shopPointersStore], 'readwrite');

      const attempts = transaction.objectStore(this.attemptsStore);
      const pointers = transaction.objectStore(this.shopPointersStore);

      const putAttempt = attempts.put(attempt);
      putAttempt.onerror = () => reject(putAttempt.error);

      const getPointer = pointers.get(metadata.shopId);
      getPointer.onerror = () => reject(getPointer.error);
      getPointer.onsuccess = () => {
        const pointer = (getPointer.result as ShopPointerRecord | undefined) ?? ({ shopId: metadata.shopId } as ShopPointerRecord);
        const updatePointer = pointers.put({
          ...pointer,
          shopId: metadata.shopId,
          lastAttemptSnapshotId: metadata.snapshotId,
          lastAttemptStartedAt: metadata.startedAt,
          lastAttemptStatus: 'incomplete',
        } as ShopPointerRecord);
        updatePointer.onerror = () => reject(updatePointer.error);
      };

      transaction.oncomplete = () => resolve();
      transaction.onerror = () => reject(transaction.error);
    });
  }

  async writeBatch(snapshotId: string, shopId: string, batch: OfflineSellableBatchSnapshot): Promise<void> {
    await this.writeEntity(this.batchesStore, snapshotId, shopId, batch.batchId, batch);
  }

  async writeCustomer(snapshotId: string, shopId: string, customer: OfflineCustomerLiteSnapshot): Promise<void> {
    await this.writeEntity(this.customersStore, snapshotId, shopId, customer.customerId, customer);
  }

  async writeDiscountRule(snapshotId: string, shopId: string, rule: OfflineDiscountRuleSnapshot): Promise<void> {
    await this.writeEntity(this.discountRulesStore, snapshotId, shopId, rule.ruleId, rule);
  }

  async writeActiveLease(snapshotId: string, shopId: string, lease: OfflineActiveLeaseSnapshot): Promise<void> {
    await this.writeEntity(this.activeLeasesStore, snapshotId, shopId, lease.leaseId, lease);
  }

  async markComplete(snapshotId: string, shopId: string, completedAt: string): Promise<void> {
    if (!snapshotId || !shopId || typeof indexedDB === 'undefined') {
      return;
    }

    const database = await this.openDatabase();
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction([this.attemptsStore, this.shopPointersStore], 'readwrite');
      const attempts = transaction.objectStore(this.attemptsStore);
      const pointers = transaction.objectStore(this.shopPointersStore);

      const getAttempt = attempts.get(snapshotId);
      getAttempt.onerror = () => reject(getAttempt.error);
      getAttempt.onsuccess = () => {
        const attempt = getAttempt.result as OfflineSalesSnapshotAttemptRecord | undefined;
        if (!attempt) {
          reject(new Error('Snapshot attempt not found.'));
          return;
        }

        const updatedAttempt: OfflineSalesSnapshotAttemptRecord = {
          ...attempt,
          status: 'complete',
          completedAt,
        };

        const putAttempt = attempts.put(updatedAttempt);
        putAttempt.onerror = () => reject(putAttempt.error);

        const putPointer = pointers.put({
          shopId,
          usableSnapshotId: snapshotId,
          usableCompletedAt: completedAt,
          lastAttemptSnapshotId: snapshotId,
          lastAttemptStartedAt: attempt.startedAt,
          lastAttemptStatus: 'complete',
        } as ShopPointerRecord);
        putPointer.onerror = () => reject(putPointer.error);
      };

      transaction.oncomplete = () => resolve();
      transaction.onerror = () => reject(transaction.error);
    });
  }

  async markFailed(snapshotId: string, shopId: string, errorCode: string, errorMessage: string): Promise<void> {
    if (!snapshotId || !shopId || typeof indexedDB === 'undefined') {
      return;
    }

    const database = await this.openDatabase();
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction([this.attemptsStore, this.shopPointersStore], 'readwrite');
      const attempts = transaction.objectStore(this.attemptsStore);
      const pointers = transaction.objectStore(this.shopPointersStore);

      const getAttempt = attempts.get(snapshotId);
      getAttempt.onerror = () => reject(getAttempt.error);
      getAttempt.onsuccess = () => {
        const attempt = getAttempt.result as OfflineSalesSnapshotAttemptRecord | undefined;
        const updatedAttempt: OfflineSalesSnapshotAttemptRecord = {
          snapshotId,
          shopId,
          schemaVersion: attempt?.schemaVersion ?? 1,
          startedAt: attempt?.startedAt ?? new Date().toISOString(),
          status: 'failed',
          errorCode,
          errorMessage,
        };

        const putAttempt = attempts.put(updatedAttempt);
        putAttempt.onerror = () => reject(putAttempt.error);

        // Do NOT change usable snapshot pointer on failure.
        const getPointer = pointers.get(shopId);
        getPointer.onerror = () => reject(getPointer.error);
        getPointer.onsuccess = () => {
          const pointer = (getPointer.result as ShopPointerRecord | undefined) ?? ({ shopId } as ShopPointerRecord);
          const updatedPointer: ShopPointerRecord = {
            ...pointer,
            shopId,
            lastAttemptSnapshotId: snapshotId,
            lastAttemptStartedAt: updatedAttempt.startedAt,
            lastAttemptStatus: 'failed',
          };
          const putPointer = pointers.put(updatedPointer);
          putPointer.onerror = () => reject(putPointer.error);
        };
      };

      transaction.oncomplete = () => resolve();
      transaction.onerror = () => reject(transaction.error);
    });
  }

  async getUsableSnapshotId(shopId: string): Promise<string | null> {
    if (!shopId || typeof indexedDB === 'undefined') {
      return null;
    }

    const database = await this.openDatabase();
    const pointer = await new Promise<ShopPointerRecord | undefined>((resolve, reject) => {
      const transaction = database.transaction(this.shopPointersStore, 'readonly');
      const request = transaction.objectStore(this.shopPointersStore).get(shopId);
      request.onsuccess = () => resolve(request.result as ShopPointerRecord | undefined);
      request.onerror = () => reject(request.error);
    });

    return pointer?.usableSnapshotId ?? null;
  }

  async getUsableSnapshotInfo(shopId: string): Promise<OfflineUsableSnapshotInfo | null> {
    if (!shopId || typeof indexedDB === 'undefined') {
      return null;
    }

    const database = await this.openDatabase();
    const pointer = await new Promise<ShopPointerRecord | undefined>((resolve, reject) => {
      const transaction = database.transaction(this.shopPointersStore, 'readonly');
      const request = transaction.objectStore(this.shopPointersStore).get(shopId);
      request.onsuccess = () => resolve(request.result as ShopPointerRecord | undefined);
      request.onerror = () => reject(request.error);
    });

    if (!pointer?.usableSnapshotId || !pointer.usableCompletedAt) {
      return null;
    }

    return { snapshotId: pointer.usableSnapshotId, completedAt: pointer.usableCompletedAt };
  }

  async getUsableBatches(shopId: string): Promise<readonly OfflineSellableBatchSnapshot[]> {
    return await this.readUsableEntities(this.batchesStore, shopId);
  }

  async getUsableCustomers(shopId: string): Promise<readonly OfflineCustomerLiteSnapshot[]> {
    return await this.readUsableEntities(this.customersStore, shopId);
  }

  async getUsableDiscountRules(shopId: string): Promise<readonly OfflineDiscountRuleSnapshot[]> {
    return await this.readUsableEntities(this.discountRulesStore, shopId);
  }

  private async readUsableEntities<T>(storeName: string, shopId: string): Promise<readonly T[]> {
    if (!shopId || typeof indexedDB === 'undefined') {
      return [];
    }

    const usableSnapshotId = await this.getUsableSnapshotId(shopId);
    if (!usableSnapshotId) {
      return [];
    }

    const database = await this.openDatabase();
    const all = await new Promise<SnapshotScopedRecord<T>[]>((resolve, reject) => {
      const transaction = database.transaction(storeName, 'readonly');
      const request = transaction.objectStore(storeName).getAll();
      request.onsuccess = () => resolve((request.result as SnapshotScopedRecord<T>[]) ?? []);
      request.onerror = () => reject(request.error);
    });

    return all
      .filter((record) => record.shopId === shopId && record.snapshotId === usableSnapshotId)
      .map((record) => record.entity);
  }

  private async writeEntity<T>(
    storeName: string,
    snapshotId: string,
    shopId: string,
    entityId: string,
    entity: T
  ): Promise<void> {
    if (!snapshotId || !shopId || !entityId || typeof indexedDB === 'undefined') {
      return;
    }

    const database = await this.openDatabase();
    const record: SnapshotScopedRecord<T> = {
      key: `${snapshotId}::${entityId}`,
      snapshotId,
      shopId,
      entityId,
      entity,
      writtenAt: new Date().toISOString(),
    };

    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction(storeName, 'readwrite');
      const request = transaction.objectStore(storeName).put(record);
      request.onsuccess = () => resolve();
      request.onerror = () => reject(request.error);
    });
  }

  private async openDatabase(): Promise<IDBDatabase> {
    return await new Promise((resolve, reject) => {
      const request = indexedDB.open(this.databaseName, this.databaseVersion);

      request.onupgradeneeded = () => {
        const database = request.result;
        if (!database.objectStoreNames.contains(this.attemptsStore)) {
          database.createObjectStore(this.attemptsStore, { keyPath: 'snapshotId' });
        }
        if (!database.objectStoreNames.contains(this.shopPointersStore)) {
          database.createObjectStore(this.shopPointersStore, { keyPath: 'shopId' });
        }
        if (!database.objectStoreNames.contains(this.batchesStore)) {
          database.createObjectStore(this.batchesStore, { keyPath: 'key' });
        }
        if (!database.objectStoreNames.contains(this.customersStore)) {
          database.createObjectStore(this.customersStore, { keyPath: 'key' });
        }
        if (!database.objectStoreNames.contains(this.discountRulesStore)) {
          database.createObjectStore(this.discountRulesStore, { keyPath: 'key' });
        }
        if (!database.objectStoreNames.contains(this.activeLeasesStore)) {
          database.createObjectStore(this.activeLeasesStore, { keyPath: 'key' });
        }
      };

      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }
}
