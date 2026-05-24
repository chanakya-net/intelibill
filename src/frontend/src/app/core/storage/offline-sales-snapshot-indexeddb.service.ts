import { Injectable } from '@angular/core';

import {
  OFFLINE_SALES_SNAPSHOT_ACTIVE_LEASES_STORE,
  OFFLINE_SALES_SNAPSHOT_ATTEMPTS_STORE,
  OFFLINE_SALES_SNAPSHOT_BATCHES_STORE,
  OFFLINE_SALES_SNAPSHOT_DATABASE_NAME,
  OFFLINE_SALES_SNAPSHOT_DATABASE_VERSION,
  OFFLINE_SALES_SNAPSHOT_CUSTOMERS_STORE,
  OFFLINE_SALES_SNAPSHOT_DISCOUNT_RULES_STORE,
  OFFLINE_SALES_SNAPSHOT_SHOP_POINTERS_STORE,
  type OfflineActiveLeaseSnapshot,
  type OfflineCustomerLiteSnapshot,
  type OfflineDiscountRuleSnapshot,
  type OfflineSalesSnapshotAttemptRecord,
  type OfflineSalesSnapshotAttemptStatus,
  type OfflineSalesSnapshotMetadata,
  type OfflineSalesSnapshotScopedRecord,
  type OfflineSalesSnapshotShopPointerRecord,
  type OfflineUsableSnapshotInfo,
  configureOfflineSalesSnapshotSchema,
  type OfflineSellableBatchSnapshot,
} from '../../features/sales/utils/offline-sales-snapshot.schema';

export type {
  OfflineSalesSnapshotMetadata,
  OfflineSalesSnapshotAttemptStatus,
  OfflineSalesSnapshotAttemptRecord,
  OfflineSellableBatchSnapshot,
  OfflineCustomerLiteSnapshot,
  OfflineDiscountRuleSnapshot,
  OfflineActiveLeaseSnapshot,
  OfflineUsableSnapshotInfo,
} from '../../features/sales/utils/offline-sales-snapshot.schema';

@Injectable({ providedIn: 'root' })
export class OfflineSalesSnapshotIndexedDbService {
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
      const transaction = database.transaction([OFFLINE_SALES_SNAPSHOT_ATTEMPTS_STORE, OFFLINE_SALES_SNAPSHOT_SHOP_POINTERS_STORE], 'readwrite');

      const attempts = transaction.objectStore(OFFLINE_SALES_SNAPSHOT_ATTEMPTS_STORE);
      const pointers = transaction.objectStore(OFFLINE_SALES_SNAPSHOT_SHOP_POINTERS_STORE);

      const putAttempt = attempts.put(attempt);
      putAttempt.onerror = () => reject(putAttempt.error);

      const getPointer = pointers.get(metadata.shopId);
      getPointer.onerror = () => reject(getPointer.error);
      getPointer.onsuccess = () => {
        const pointer = (getPointer.result as OfflineSalesSnapshotShopPointerRecord | undefined) ?? { shopId: metadata.shopId };
        const updatePointer = pointers.put({
          ...pointer,
          shopId: metadata.shopId,
          lastAttemptSnapshotId: metadata.snapshotId,
          lastAttemptStartedAt: metadata.startedAt,
          lastAttemptStatus: 'incomplete',
        } as OfflineSalesSnapshotShopPointerRecord);
        updatePointer.onerror = () => reject(updatePointer.error);
      };

      transaction.oncomplete = () => resolve();
      transaction.onerror = () => reject(transaction.error);
    });
  }

  async writeBatch(snapshotId: string, shopId: string, batch: OfflineSellableBatchSnapshot): Promise<void> {
    await this.writeEntity(OFFLINE_SALES_SNAPSHOT_BATCHES_STORE, snapshotId, shopId, batch.batchId, batch);
  }

  async writeCustomer(snapshotId: string, shopId: string, customer: OfflineCustomerLiteSnapshot): Promise<void> {
    await this.writeEntity(OFFLINE_SALES_SNAPSHOT_CUSTOMERS_STORE, snapshotId, shopId, customer.customerId, customer);
  }

  async writeDiscountRule(snapshotId: string, shopId: string, rule: OfflineDiscountRuleSnapshot): Promise<void> {
    await this.writeEntity(OFFLINE_SALES_SNAPSHOT_DISCOUNT_RULES_STORE, snapshotId, shopId, rule.ruleId, rule);
  }

  async writeActiveLease(snapshotId: string, shopId: string, lease: OfflineActiveLeaseSnapshot): Promise<void> {
    await this.writeEntity(OFFLINE_SALES_SNAPSHOT_ACTIVE_LEASES_STORE, snapshotId, shopId, lease.leaseId, lease);
  }

  async markComplete(snapshotId: string, shopId: string, completedAt: string): Promise<void> {
    if (!snapshotId || !shopId || typeof indexedDB === 'undefined') {
      return;
    }

    const database = await this.openDatabase();
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction([OFFLINE_SALES_SNAPSHOT_ATTEMPTS_STORE, OFFLINE_SALES_SNAPSHOT_SHOP_POINTERS_STORE], 'readwrite');
      const attempts = transaction.objectStore(OFFLINE_SALES_SNAPSHOT_ATTEMPTS_STORE);
      const pointers = transaction.objectStore(OFFLINE_SALES_SNAPSHOT_SHOP_POINTERS_STORE);

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
        } as OfflineSalesSnapshotShopPointerRecord);
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
      const transaction = database.transaction([OFFLINE_SALES_SNAPSHOT_ATTEMPTS_STORE, OFFLINE_SALES_SNAPSHOT_SHOP_POINTERS_STORE], 'readwrite');
      const attempts = transaction.objectStore(OFFLINE_SALES_SNAPSHOT_ATTEMPTS_STORE);
      const pointers = transaction.objectStore(OFFLINE_SALES_SNAPSHOT_SHOP_POINTERS_STORE);

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
          const pointer = (getPointer.result as OfflineSalesSnapshotShopPointerRecord | undefined) ?? ({ shopId } as OfflineSalesSnapshotShopPointerRecord);
          const updatedPointer: OfflineSalesSnapshotShopPointerRecord = {
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
    const pointer = await new Promise<OfflineSalesSnapshotShopPointerRecord | undefined>((resolve, reject) => {
      const transaction = database.transaction(OFFLINE_SALES_SNAPSHOT_SHOP_POINTERS_STORE, 'readonly');
      const request = transaction.objectStore(OFFLINE_SALES_SNAPSHOT_SHOP_POINTERS_STORE).get(shopId);
      request.onsuccess = () => resolve(request.result as OfflineSalesSnapshotShopPointerRecord | undefined);
      request.onerror = () => reject(request.error);
    });

    return pointer?.usableSnapshotId ?? null;
  }

  async getUsableSnapshotInfo(shopId: string): Promise<OfflineUsableSnapshotInfo | null> {
    if (!shopId || typeof indexedDB === 'undefined') {
      return null;
    }

    const database = await this.openDatabase();
    const pointer = await new Promise<OfflineSalesSnapshotShopPointerRecord | undefined>((resolve, reject) => {
      const transaction = database.transaction(OFFLINE_SALES_SNAPSHOT_SHOP_POINTERS_STORE, 'readonly');
      const request = transaction.objectStore(OFFLINE_SALES_SNAPSHOT_SHOP_POINTERS_STORE).get(shopId);
      request.onsuccess = () => resolve(request.result as OfflineSalesSnapshotShopPointerRecord | undefined);
      request.onerror = () => reject(request.error);
    });

    if (!pointer?.usableSnapshotId || !pointer.usableCompletedAt) {
      return null;
    }

    return { snapshotId: pointer.usableSnapshotId, completedAt: pointer.usableCompletedAt };
  }

  async getUsableBatches(shopId: string): Promise<readonly OfflineSellableBatchSnapshot[]> {
    return await this.readUsableEntities(OFFLINE_SALES_SNAPSHOT_BATCHES_STORE, shopId);
  }

  async getUsableCustomers(shopId: string): Promise<readonly OfflineCustomerLiteSnapshot[]> {
    return await this.readUsableEntities(OFFLINE_SALES_SNAPSHOT_CUSTOMERS_STORE, shopId);
  }

  async getUsableDiscountRules(shopId: string): Promise<readonly OfflineDiscountRuleSnapshot[]> {
    return await this.readUsableEntities(OFFLINE_SALES_SNAPSHOT_DISCOUNT_RULES_STORE, shopId);
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
    const all = await new Promise<OfflineSalesSnapshotScopedRecord<T>[]>((resolve, reject) => {
      const transaction = database.transaction(storeName, 'readonly');
      const request = transaction.objectStore(storeName).getAll();
      request.onsuccess = () => resolve((request.result as OfflineSalesSnapshotScopedRecord<T>[]) ?? []);
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
    const record: OfflineSalesSnapshotScopedRecord<T> = {
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
      const request = indexedDB.open(OFFLINE_SALES_SNAPSHOT_DATABASE_NAME, OFFLINE_SALES_SNAPSHOT_DATABASE_VERSION);

      request.onupgradeneeded = () => configureOfflineSalesSnapshotSchema(request.result);
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }
}
