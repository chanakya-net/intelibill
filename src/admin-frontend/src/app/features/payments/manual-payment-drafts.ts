import { Injectable } from '@angular/core';
import type { ManualPaymentCommand, PaymentScope } from './manual-payment-api.service';

interface PaymentDraft {
  readonly revision: number;
  readonly pending?: ManualPaymentCommand;
}

const storeName = 'confirmations';

@Injectable({ providedIn: 'root' })
export class ManualPaymentDrafts {
  read(scope: PaymentScope) {
    return this.access(scope, 'readonly', current => current);
  }

  claim(scope: PaymentScope, revision: number, command: ManualPaymentCommand) {
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

  private async access<T>(scope: PaymentScope, mode: IDBTransactionMode,
    visit: (value: PaymentDraft, store: IDBObjectStore, key: string) => T): Promise<T> {
    const db = await this.open();
    try {
      return await new Promise<T>((resolve, reject) => {
        const tx = db.transaction(storeName, mode, { durability: 'strict' });
        const store = tx.objectStore(storeName);
        const key = [scope.admin, scope.account.toLowerCase(), scope.order.toLowerCase()].join('.');
        const read = store.get(key);
        let result: T;
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
      const request = indexedDB.open('intelibill.admin.manual-payments', 1);
      request.onupgradeneeded = () => request.result.createObjectStore(storeName);
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }
}
