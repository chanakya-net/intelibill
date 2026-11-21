import { Injectable } from '@angular/core';
import type { ReopeningCommand, ReopeningTarget } from './reopening-api.service';

export interface PendingReopening extends ReopeningTarget {
  readonly adminId: string;
  readonly command: ReopeningCommand;
}

export class InvalidReopeningDraft extends Error {}

const uuid = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;

export function validateReopeningDraft(value: unknown, adminId: string, accountId: string): PendingReopening {
  const draft = value as PendingReopening | null;
  if (!draft || typeof draft !== 'object' || !isId(draft.adminId) || !isId(draft.accountId) ||
    draft.adminId.toLowerCase() !== adminId.toLowerCase() || draft.accountId.toLowerCase() !== accountId.toLowerCase() ||
    !isId(draft.closureId) || !isId(draft.shopId) || !validCommand(draft.command)) {
    throw new InvalidReopeningDraft();
  }
  return draft;
}

function isId(value: unknown): value is string {
  return typeof value === 'string' && uuid.test(value);
}

function validCommand(command: ReopeningCommand | null | undefined): boolean {
  return !!command && typeof command === 'object' && isId(command.operationKey) && isId(command.ownerConsentUserId) &&
    Number.isSafeInteger(command.expectedVersion) && command.expectedVersion > 0 &&
    typeof command.impactDigest === 'string' && /^[a-f0-9]{64}$/.test(command.impactDigest) &&
    command.ownerConsentConfirmed === true && validInstant(command.ownerConsentAt) &&
    validText(command.reason, 1000) && validText(command.ownerConsentReference, 120) &&
    validText(command.ownerConsentEvidence, 2000);
}

function validInstant(value: unknown): boolean {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{3})?Z$/.test(value)) return false;
  const instant = new Date(value);
  const year = instant.getUTCFullYear();
  if (!(year >= 2000 && year <= 9998)) return false;
  const canonical = instant.toISOString();
  return value === canonical || value === canonical.replace('.000Z', 'Z');
}

function validText(value: unknown, max: number): boolean {
  return typeof value === 'string' && value.length <= max && value.trim().length > 0 &&
    [...value].every(character => character === '\n' || (character.charCodeAt(0) >= 32 &&
      !(character.charCodeAt(0) >= 127 && character.charCodeAt(0) <= 159)));
}

const storeName = 'pending';
const scopeKey = (adminId: string, accountId: string) => `${adminId.toLowerCase()}.${accountId.toLowerCase()}`;

/** IndexedDB serializes the read and claim across tabs and confirms persistence before the request is sent. */
@Injectable({ providedIn: 'root' })
export class ReopeningDrafts {
  read(adminId: string, accountId: string) {
    return this.access(adminId, accountId, 'readonly', current => current);
  }

  claim(pending: PendingReopening) {
    validateReopeningDraft(pending, pending.adminId, pending.accountId);
    return this.access(pending.adminId, pending.accountId, 'readwrite', (current, store, key) => {
      if (current) return current;
      store.add(pending, key);
      return pending;
    });
  }

  clear(pending: PendingReopening) {
    return this.access(pending.adminId, pending.accountId, 'readwrite', (current, store, key) => {
      if (current?.command.operationKey === pending.command.operationKey) store.delete(key);
    });
  }

  private async access<T>(adminId: string, accountId: string, mode: IDBTransactionMode,
    visit: (current: PendingReopening | null, store: IDBObjectStore, key: string) => T): Promise<T> {
    const db = await this.open();
    try {
      return await new Promise<T>((resolve, reject) => {
        const tx = db.transaction(storeName, mode, { durability: 'strict' });
        const store = tx.objectStore(storeName);
        const key = scopeKey(adminId, accountId);
        const read = store.get(key);
        let result: T;
        let failure: unknown;
        read.onsuccess = () => {
          try {
            const current = read.result === undefined ? null : validateReopeningDraft(read.result, adminId, accountId);
            result = visit(current, store, key);
          } catch (error) {
            failure = error;
            tx.abort();
          }
        };
        tx.oncomplete = () => resolve(result);
        tx.onabort = () => reject(failure ?? tx.error);
      });
    } finally {
      db.close();
    }
  }

  private open() {
    return new Promise<IDBDatabase>((resolve, reject) => {
      const request = indexedDB.open('intelibill.admin.reopenings', 1);
      request.onupgradeneeded = () => request.result.createObjectStore(storeName);
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }
}
