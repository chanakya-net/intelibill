import { Injectable } from '@angular/core';

import { QUANTITY_SCALE_MAX } from '../../shared/quantity/quantity-units';
import { ScopeOwner, ownerKey } from '../context/shop-scope';

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
  /** The item's unit label; '' on lines saved before units. */
  readonly uom: string;
  /** Decimal places the line's quantity may carry; lines saved before units are whole units (0). */
  readonly quantityScale: number;
  /** Unit code its offline document prints; NOS on lines saved before units. */
  readonly documentUnit: string;
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

/**
 * One owner's cart. Drafts were once keyed by shop alone; such a record cannot say which user built
 * it, so it is never read again and never handed to whoever signs in next.
 */
interface SalesCartRecord {
  readonly ownerKey: string;
  readonly userId: string;
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
    // A line saved before units keeps its place in the draft, as whole units printed NOS; refreshing the line from a
    // search brings its unit back.
    uom: typeof obj['uom'] === 'string' ? obj['uom'] : '',
    quantityScale: isQuantityScale(obj['quantityScale']) ? obj['quantityScale'] : 0,
    documentUnit:
      typeof obj['documentUnit'] === 'string' && obj['documentUnit'].trim() ? obj['documentUnit'] : 'NOS',
  };
}

function isQuantityScale(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= QUANTITY_SCALE_MAX;
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
  // Version 2 adds the owner-keyed store; the shop-keyed `sales-cart` store of version 1 is left as
  // it was and no longer read.
  private readonly databaseVersion = 2;
  private readonly storeName = 'sales-cart-by-owner';
  private connection: Promise<IDBDatabase> | null = null;

  async loadCart(owner: ScopeOwner, maxAgeMs: number): Promise<readonly SalesCartDraftItem[]> {
    const record = await this.readFreshRecord(owner, maxAgeMs);
    if (!record) {
      return [];
    }

    const rawItems = record.items ?? [];
    if (!Array.isArray(rawItems) || rawItems.length === 0) {
      return [];
    }

    const migratedItems = rawItems.map(migrateLegacyCartItem);
    if (migratedItems.some((item) => item === null)) {
      await this.clearCart(owner);
      return [];
    }

    return migratedItems as SalesCartDraftItem[];
  }

  async loadServiceCart(owner: ScopeOwner, maxAgeMs: number): Promise<readonly SalesCartDraftServiceItem[]> {
    const record = await this.readFreshRecord(owner, maxAgeMs, { clearExpired: false });
    if (!record) {
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
    owner: ScopeOwner,
    items: readonly SalesCartDraftItem[],
    serviceItems: readonly SalesCartDraftServiceItem[] = [],
  ): Promise<void> {
    if (!isComplete(owner) || typeof indexedDB === 'undefined') {
      return;
    }

    const database = await this.openDatabase();
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction(this.storeName, 'readwrite');
      const request = transaction.objectStore(this.storeName).put({
        ownerKey: ownerKey(owner),
        userId: owner.userId,
        shopId: owner.shopId,
        items,
        serviceItems,
        updatedAt: new Date().toISOString(),
      } satisfies SalesCartRecord);

      request.onsuccess = () => resolve();
      request.onerror = () => reject(request.error);
    });
  }

  async clearCart(owner: ScopeOwner): Promise<void> {
    if (!isComplete(owner) || typeof indexedDB === 'undefined') {
      return;
    }

    const database = await this.openDatabase();
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction(this.storeName, 'readwrite');
      const request = transaction.objectStore(this.storeName).delete(ownerKey(owner));

      request.onsuccess = () => resolve();
      request.onerror = () => reject(request.error);
    });
  }

  /** The owner's record, or null when there is none, it belongs to someone else, or it expired. */
  private async readFreshRecord(
    owner: ScopeOwner,
    maxAgeMs: number,
    options: { readonly clearExpired: boolean } = { clearExpired: true },
  ): Promise<SalesCartRecord | null> {
    if (!isComplete(owner) || typeof indexedDB === 'undefined') {
      return null;
    }

    const database = await this.openDatabase();
    const record = await new Promise<SalesCartRecord | undefined>((resolve, reject) => {
      const transaction = database.transaction(this.storeName, 'readonly');
      const request = transaction.objectStore(this.storeName).get(ownerKey(owner));

      request.onsuccess = () => resolve(request.result as SalesCartRecord | undefined);
      request.onerror = () => reject(request.error);
    });

    if (!record || record.userId !== owner.userId || record.shopId !== owner.shopId) {
      return null;
    }

    const updatedAtMs = Date.parse(record.updatedAt);
    const isExpired =
      Number.isNaN(updatedAtMs) ||
      maxAgeMs <= 0 ||
      Date.now() - updatedAtMs > maxAgeMs;

    if (isExpired) {
      if (options.clearExpired) {
        await this.clearCart(owner);
      }
      return null;
    }

    return record;
  }

  /**
   * One connection per tab, closed when a newer tab upgrades the schema. A tab still on an older
   * version blocks the upgrade; that is reported as a failure instead of waiting on it forever.
   */
  private openDatabase(): Promise<IDBDatabase> {
    this.connection ??= new Promise<IDBDatabase>((resolve, reject) => {
      const request = indexedDB.open(this.databaseName, this.databaseVersion);

      request.onupgradeneeded = () => {
        const database = request.result;
        if (!database.objectStoreNames.contains(this.storeName)) {
          database.createObjectStore(this.storeName, { keyPath: 'ownerKey' });
        }
      };

      request.onsuccess = () => {
        const database = request.result;
        database.onversionchange = () => {
          database.close();
          this.connection = null;
        };
        resolve(database);
      };
      request.onerror = () => reject(request.error);
      request.onblocked = () => reject(new Error('The cart is open in an older tab; reload it to continue.'));
    }).catch((error: unknown) => {
      this.connection = null;
      throw error;
    });

    return this.connection;
  }
}

function isComplete(owner: ScopeOwner): boolean {
  return !!owner.userId && !!owner.shopId;
}
