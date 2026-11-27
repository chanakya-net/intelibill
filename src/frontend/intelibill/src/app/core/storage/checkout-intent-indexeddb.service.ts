import { Injectable } from '@angular/core';

import type { RecordSaleRequest } from '../../features/sales/services/sale.models';

export type CheckoutIntentStatus = 'prepared' | 'sending' | 'uncertain' | 'confirmed' | 'rejected';

export const UNRESOLVED_CHECKOUT_STATUSES: readonly CheckoutIntentStatus[] = ['prepared', 'sending', 'uncertain'];

/**
 * One cashier checkout, frozen before it leaves the browser. The request (and therefore its
 * idempotency key) never changes after `prepare`, so every retry replays the same operation.
 */
export interface CheckoutIntent {
  readonly schemaVersion: 1;
  readonly intentId: string;
  readonly userId: string;
  readonly shopId: string;
  readonly idempotencyKey: string;
  readonly request: RecordSaleRequest;
  readonly createdAt: string;
  readonly updatedAt: string;
  readonly status: CheckoutIntentStatus;
  /** Owner tab's send lease; another tab may take over a `sending` intent only after it lapses. */
  readonly sendingUntil: string | null;
  readonly saleId: string | null;
  readonly invoiceNumber: string | null;
  readonly rejectionCode: string | null;
}

export type CheckoutIntentUpdate = (current: CheckoutIntent) => CheckoutIntent | null;

/**
 * Durable store for checkout intents. Writes resolve only once the IndexedDB transaction has
 * committed, so callers can rely on "stored" meaning "survives a reload". Updates run as a
 * read-modify-write inside one readwrite transaction, which IndexedDB serializes across tabs,
 * giving compare-and-set semantics.
 */
@Injectable({ providedIn: 'root' })
export class CheckoutIntentIndexedDbService {
  private readonly databaseName = 'intelibill-checkout-intents';
  private readonly databaseVersion = 1;
  private readonly storeName = 'checkout-intents';

  async add(intent: CheckoutIntent): Promise<void> {
    const database = await this.openDatabase();
    try {
      await new Promise<void>((resolve, reject) => {
        const transaction = database.transaction(this.storeName, 'readwrite');
        transaction.oncomplete = () => resolve();
        transaction.onerror = () => reject(transaction.error);
        transaction.onabort = () => reject(transaction.error ?? new Error('Checkout intent write aborted.'));
        transaction.objectStore(this.storeName).add(intent);
      });
    } finally {
      database.close();
    }
  }

  async get(intentId: string): Promise<CheckoutIntent | null> {
    const database = await this.openDatabase();
    try {
      return await new Promise<CheckoutIntent | null>((resolve, reject) => {
        const transaction = database.transaction(this.storeName, 'readonly');
        const request = transaction.objectStore(this.storeName).get(intentId);
        request.onsuccess = () => resolve((request.result as CheckoutIntent | undefined) ?? null);
        request.onerror = () => reject(request.error);
      });
    } finally {
      database.close();
    }
  }

  async listByScope(userId: string, shopId: string): Promise<readonly CheckoutIntent[]> {
    const database = await this.openDatabase();
    try {
      const all = await new Promise<readonly CheckoutIntent[]>((resolve, reject) => {
        const transaction = database.transaction(this.storeName, 'readonly');
        const request = transaction.objectStore(this.storeName).getAll();
        request.onsuccess = () => resolve((request.result as CheckoutIntent[] | undefined) ?? []);
        request.onerror = () => reject(request.error);
      });
      return all.filter((intent) => intent.userId === userId && intent.shopId === shopId);
    } finally {
      database.close();
    }
  }

  /**
   * Applies `update` to the stored intent atomically. Returns the written intent, or null when
   * the intent is missing or `update` declined (returned null) because its precondition failed.
   */
  async update(intentId: string, update: CheckoutIntentUpdate): Promise<CheckoutIntent | null> {
    const database = await this.openDatabase();
    try {
      return await new Promise<CheckoutIntent | null>((resolve, reject) => {
        const transaction = database.transaction(this.storeName, 'readwrite');
        const store = transaction.objectStore(this.storeName);
        let written: CheckoutIntent | null = null;
        transaction.oncomplete = () => resolve(written);
        transaction.onerror = () => reject(transaction.error);
        transaction.onabort = () => reject(transaction.error ?? new Error('Checkout intent update aborted.'));

        const request = store.get(intentId);
        request.onsuccess = () => {
          const current = request.result as CheckoutIntent | undefined;
          if (!current) {
            return;
          }
          const next = update(current);
          if (!next) {
            return;
          }
          written = next;
          store.put(next);
        };
      });
    } finally {
      database.close();
    }
  }

  private openDatabase(): Promise<IDBDatabase> {
    if (typeof indexedDB === 'undefined') {
      return Promise.reject(new Error('IndexedDB is unavailable.'));
    }

    return new Promise<IDBDatabase>((resolve, reject) => {
      const request = indexedDB.open(this.databaseName, this.databaseVersion);
      request.onupgradeneeded = () => {
        const database = request.result;
        if (!database.objectStoreNames.contains(this.storeName)) {
          database.createObjectStore(this.storeName, { keyPath: 'intentId' });
        }
      };
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }
}
