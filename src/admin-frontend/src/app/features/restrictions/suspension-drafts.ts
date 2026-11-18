import { Injectable } from '@angular/core';
import type { SuspensionCommand } from './suspension-api.service';

export interface PendingSuspension {
  readonly adminId: string;
  readonly accountId: string;
  readonly command: SuspensionCommand;
}

const storeName = 'pending';
const scopeKey = (adminId: string, accountId: string) => `${adminId.toLowerCase()}.${accountId.toLowerCase()}`;

/** IndexedDB serializes the read and claim across tabs and confirms persistence before the request is sent. */
@Injectable({ providedIn: 'root' })
export class SuspensionDrafts {
  read(adminId: string, accountId: string) {
    return this.access(adminId, accountId, 'readonly', current => current);
  }

  claim(pending: PendingSuspension) {
    return this.access(pending.adminId, pending.accountId, 'readwrite', (current, store, key) => {
      if (current) return current;
      store.add(pending, key);
      return pending;
    });
  }

  clear(pending: PendingSuspension) {
    return this.access(pending.adminId, pending.accountId, 'readwrite', (current, store, key) => {
      if (current?.command.operationKey === pending.command.operationKey) store.delete(key);
    });
  }

  private async access<T>(adminId: string, accountId: string, mode: IDBTransactionMode,
    visit: (current: PendingSuspension | null, store: IDBObjectStore, key: string) => T): Promise<T> {
    const db = await this.open();
    try {
      return await new Promise<T>((resolve, reject) => {
        const tx = db.transaction(storeName, mode, { durability: 'strict' });
        const store = tx.objectStore(storeName);
        const key = scopeKey(adminId, accountId);
        const read = store.get(key);
        let result: T;
        read.onsuccess = () => { result = visit(read.result ?? null, store, key); };
        tx.oncomplete = () => resolve(result);
        tx.onabort = () => reject(tx.error);
      });
    } finally {
      db.close();
    }
  }

  private open() {
    return new Promise<IDBDatabase>((resolve, reject) => {
      const request = indexedDB.open('intelibill.admin.suspensions', 1);
      request.onupgradeneeded = () => request.result.createObjectStore(storeName);
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }
}
