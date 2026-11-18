import { Injectable } from '@angular/core';
import type { ManualPaymentCommand, PaymentScope } from './manual-payment-api.service';

interface PaymentDraft<T> {
  readonly revision: number;
  readonly pending?: T;
}

const storeName = 'confirmations';

export class PaymentDraftStore<T extends { readonly operationKey: string }> {
  constructor(private readonly databaseName: string) {}
  read(scope: PaymentScope) {
    return this.access(scope, 'readonly', current => current);
  }

  claim(scope: PaymentScope, revision: number, command: T) {
    return this.access(scope, 'readwrite', (current, store, key) => {
      if (current.pending || current.revision !== revision) return current;
      const claimed = { revision, pending: command };
      store.put(claimed, key);
      return claimed;
    });
  }

  settle(scope: PaymentScope, operationKey: string) {
    return this.access(scope, 'readwrite', (current, store, key) => {
      if (current.pending?.operationKey === operationKey)
        store.put({ revision: current.revision + 1 }, key);
    });
  }

  private async access<TResult>(scope: PaymentScope, mode: IDBTransactionMode,
    visit: (value: PaymentDraft<T>, store: IDBObjectStore, key: string) => TResult): Promise<TResult> {
    const db = await this.open();
    try {
      return await new Promise<TResult>((resolve, reject) => {
        const tx = db.transaction(storeName, mode, { durability: 'strict' });
        const store = tx.objectStore(storeName);
        const key = [scope.admin, scope.account.toLowerCase(), scope.order.toLowerCase()].join('.');
        const read = store.get(key);
        let result: TResult;
        read.onsuccess = () => { result = visit(read.result ?? { revision: 0 }, store, key); };
        tx.oncomplete = () => resolve(result);
        tx.onabort = () => reject(tx.error);
      });
    } finally {
      db.close();
    }
  }

  private open() {
    return new Promise<IDBDatabase>((resolve, reject) => {
      const request = indexedDB.open(this.databaseName, 1);
      request.onupgradeneeded = () => request.result.createObjectStore(storeName);
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }
}

@Injectable({ providedIn: 'root' })
export class ManualPaymentDrafts extends PaymentDraftStore<ManualPaymentCommand> {
  constructor() {
    super('intelibill.admin.manual-payments');
  }
}
