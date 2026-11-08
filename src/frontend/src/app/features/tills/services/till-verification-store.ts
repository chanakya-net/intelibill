import { Injectable } from '@angular/core';
import { ScopeOwner } from '../../../core/context/shop-scope';
import { TillCapabilities, TillSession } from './till.models';
export interface TillVerification extends ScopeOwner {
  capability: TillCapabilities;
  session: TillSession | null;
  verifiedAt: string;
}
@Injectable({ providedIn: 'root' })
export class TillVerificationStore {
  async load(owner: ScopeOwner): Promise<TillVerification | null> {
    return this.transaction(owner, null);
  }
  async save(value: TillVerification): Promise<void> {
    await this.transaction(value, value);
  }
  private async transaction(
    owner: ScopeOwner,
    value: TillVerification | null,
  ): Promise<TillVerification | null> {
    if (typeof indexedDB === 'undefined') throw new Error('Till verification storage unavailable.');
    const db = await new Promise<IDBDatabase>((resolve, reject) => {
      const req = indexedDB.open('intelibill-till-verification', 1);
      req.onupgradeneeded = () =>
        req.result.createObjectStore('verified', { keyPath: ['userId', 'shopId'] });
      req.onerror = () => reject(req.error);
      req.onblocked = () => reject(new Error('Till verification storage blocked.'));
      req.onsuccess = () => resolve(req.result);
    });
    try {
      return await new Promise((resolve, reject) => {
        const tx = db.transaction('verified', value ? 'readwrite' : 'readonly');
        let result: TillVerification | null = null;
        tx.oncomplete = () => resolve(result);
        tx.onerror = tx.onabort = () =>
          reject(tx.error ?? new Error('Till verification write failed.'));
        const store = tx.objectStore('verified');
        if (value) {
          store.put(value);
          result = value;
        } else {
          const req = store.get([owner.userId, owner.shopId]);
          req.onsuccess = () => (result = req.result ?? null);
        }
      });
    } finally {
      db.close();
    }
  }
}
