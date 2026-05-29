import { Injectable } from '@angular/core';

export interface SalesCartDraftItem {
  readonly clientLineKey: string;
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
  readonly itemDiscountType: number;
  readonly itemDiscountValue: number;
  readonly hsnCode: string | null;
}

export interface SalesCartDraftServiceItem {
  readonly kind: 'service';
  readonly clientLineKey: string;
  readonly serviceId: string;
  readonly serviceName: string;
  readonly serviceCode: string;
  readonly quantity: number;
  readonly unitPrice: number;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
  readonly hsnCode: string | null;
}

interface SalesCartRecord {
  readonly shopId: string;
  readonly items: readonly unknown[];
  readonly serviceItems?: readonly unknown[];
  readonly updatedAt: string;
}

/** Migrates a persisted goods item that may be missing fields added in later schema versions. */
export function migrateLegacyCartItem(item: unknown): SalesCartDraftItem | null {
  if (!item || typeof item !== 'object') {
    return null;
  }
  const obj = item as Record<string, unknown>;
  if (typeof obj['inventoryBatchId'] !== 'string') {
    return null;
  }
  return {
    clientLineKey: typeof obj['clientLineKey'] === 'string' ? obj['clientLineKey'] : crypto.randomUUID(),
    barcode: typeof obj['barcode'] === 'string' ? obj['barcode'] : '',
    itemName: typeof obj['itemName'] === 'string' ? obj['itemName'] : '',
    batchNumber: typeof obj['batchNumber'] === 'string' ? obj['batchNumber'] : '',
    inventoryBatchId: obj['inventoryBatchId'] as string,
    quantity: typeof obj['quantity'] === 'number' ? obj['quantity'] : 0,
    availableQuantity: typeof obj['availableQuantity'] === 'number' ? obj['availableQuantity'] : 0,
    salesPrice: typeof obj['salesPrice'] === 'number' ? obj['salesPrice'] : 0,
    mrp: typeof obj['mrp'] === 'number' ? obj['mrp'] : 0,
    taxRatePercent: typeof obj['taxRatePercent'] === 'number' ? obj['taxRatePercent'] : 0,
    taxIncluded: typeof obj['taxIncluded'] === 'boolean' ? obj['taxIncluded'] : false,
    costPrice: typeof obj['costPrice'] === 'number' ? obj['costPrice'] : 0,
    itemDiscountType: typeof obj['itemDiscountType'] === 'number' ? obj['itemDiscountType'] : 0,
    itemDiscountValue: typeof obj['itemDiscountValue'] === 'number' ? obj['itemDiscountValue'] : 0,
    hsnCode: typeof obj['hsnCode'] === 'string' ? obj['hsnCode'] : null,
  };
}

/** Migrates a persisted service cart item. */
export function migrateLegacyServiceCartItem(item: unknown): SalesCartDraftServiceItem | null {
  if (!item || typeof item !== 'object') {
    return null;
  }
  const obj = item as Record<string, unknown>;
  if (obj['kind'] !== 'service' || typeof obj['serviceId'] !== 'string') {
    return null;
  }
  return {
    kind: 'service',
    clientLineKey: typeof obj['clientLineKey'] === 'string' ? obj['clientLineKey'] : crypto.randomUUID(),
    serviceId: obj['serviceId'] as string,
    serviceName: typeof obj['serviceName'] === 'string' ? obj['serviceName'] : '',
    serviceCode: typeof obj['serviceCode'] === 'string' ? obj['serviceCode'] : '',
    quantity: typeof obj['quantity'] === 'number' ? obj['quantity'] : 1,
    unitPrice: typeof obj['unitPrice'] === 'number' ? obj['unitPrice'] : 0,
    taxRatePercent: typeof obj['taxRatePercent'] === 'number' ? obj['taxRatePercent'] : 0,
    taxIncluded: typeof obj['taxIncluded'] === 'boolean' ? obj['taxIncluded'] : false,
    hsnCode: typeof obj['hsnCode'] === 'string' ? obj['hsnCode'] : null,
  };
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

    const rawItems = record.items ?? [];
    if (!Array.isArray(rawItems) || rawItems.length === 0) {
      return [];
    }

    const migratedItems = rawItems.map(migrateLegacyCartItem);
    if (migratedItems.some((item) => item === null)) {
      await this.clearCart(shopId);
      return [];
    }

    return migratedItems as SalesCartDraftItem[];
  }

  async loadServiceCart(shopId: string, maxAgeMs: number): Promise<readonly SalesCartDraftServiceItem[]> {
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
      return [];
    }

    const rawItems = record.serviceItems ?? [];
    if (!Array.isArray(rawItems) || rawItems.length === 0) {
      return [];
    }

    const migrated = rawItems.map(migrateLegacyServiceCartItem);
    return migrated.filter((item): item is SalesCartDraftServiceItem => item !== null);
  }

  async saveCart(
    shopId: string,
    items: readonly SalesCartDraftItem[],
    serviceItems: readonly SalesCartDraftServiceItem[] = [],
  ): Promise<void> {
    if (!shopId || typeof indexedDB === 'undefined') {
      return;
    }

    const database = await this.openDatabase();
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction(this.storeName, 'readwrite');
      const request = transaction.objectStore(this.storeName).put({
        shopId,
        items,
        serviceItems,
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
}
