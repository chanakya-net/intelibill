import { Injectable } from '@angular/core';

import type { CreatePurchaseOrderLineRequest } from '../../features/purchase-orders/services/purchase-order.service';

export interface PurchaseOrderDraftRecord {
  readonly shopId: string;
  readonly purchaseOrderId: string | null;
  readonly supplier: { readonly id: string; readonly name: string } | null;
  readonly orderDate: string | null;
  readonly expectedDeliveryDate: string | null;
  readonly supplierReferenceNumber: string | null;
  readonly notes: string | null;
  readonly lines: readonly CreatePurchaseOrderLineRequest[];
  readonly updatedAt: string;
}

@Injectable({ providedIn: 'root' })
export class PurchaseOrderDraftIndexedDbService {
  private readonly databaseName = 'intelibill-purchase-order-drafts';
  private readonly databaseVersion = 1;
  private readonly storeName = 'purchase-order-drafts';

  async loadDraft(shopId: string): Promise<PurchaseOrderDraftRecord | null> {
    if (!shopId || typeof indexedDB === 'undefined') {
      return null;
    }

    const database = await this.openDatabase();
    return await new Promise((resolve, reject) => {
      const transaction = database.transaction(this.storeName, 'readonly');
      const request = transaction.objectStore(this.storeName).get(shopId);

      request.onsuccess = () => resolve((request.result as PurchaseOrderDraftRecord | undefined) ?? null);
      request.onerror = () => reject(request.error);
    });
  }

  async saveDraft(record: PurchaseOrderDraftRecord): Promise<void> {
    if (!record.shopId || typeof indexedDB === 'undefined') {
      return;
    }

    const database = await this.openDatabase();
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction(this.storeName, 'readwrite');
      const request = transaction.objectStore(this.storeName).put({
        ...record,
        updatedAt: new Date().toISOString(),
      } satisfies PurchaseOrderDraftRecord);

      request.onsuccess = () => resolve();
      request.onerror = () => reject(request.error);
    });
  }

  async clearDraft(shopId: string): Promise<void> {
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
