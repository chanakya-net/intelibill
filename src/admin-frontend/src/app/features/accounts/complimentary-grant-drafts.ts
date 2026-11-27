import type { GrantCommand } from './complimentary-grant-api.service';

/** A confirmation frozen before HTTP: owned by one administrator and one account, never retargeted. */
export interface PendingGrant {
  readonly adminId: string;
  readonly accountId: string;
  readonly command: GrantCommand;
  /** When this browser froze the request. */
  readonly createdAt: number;
}

const prefix = 'intelibill.admin.complimentary-grant.v1.';
const accountKey = (adminId: string, accountId: string) => `${prefix}${adminId}.${accountId}`;
const operationKey = (pending: PendingGrant) => `${accountKey(pending.adminId, pending.accountId)}.${pending.command.operationKey}`;

function parse(storage: Storage, key: string, adminId: string, accountId: string): PendingGrant | null {
  try {
    const raw = storage.getItem(key);
    const pending = raw ? JSON.parse(raw) as PendingGrant : null;
    if (!pending || pending.adminId !== adminId || pending.accountId !== accountId
      || typeof pending.command?.operationKey !== 'string') return null;
    return key === operationKey(pending) ? pending : null;
  } catch {
    return null;
  }
}

function storedKeys(storage: Storage, adminId: string, accountId: string): string[] {
  const base = `${accountKey(adminId, accountId)}.`;
  const keys: string[] = [];
  for (let index = 0; index < storage.length; index++) {
    const key = storage.key(index);
    if (key?.startsWith(base)) keys.push(key);
  }
  return keys;
}

const order = (left: PendingGrant, right: PendingGrant) => left.createdAt - right.createdAt
  || left.command.operationKey.localeCompare(right.command.operationKey);

/**
 * Durable per-administrator, per-account pending grants, one record per operation. Tabs never overwrite or clear
 * another tab's operation: each record is written and removed only under its own operation key. Writing must
 * succeed before the request is sent; a storage failure returns false so the caller refuses to send.
 */
export const grantDrafts = {
  write(pending: PendingGrant, storage: Storage = localStorage): boolean {
    try {
      storage.setItem(operationKey(pending), JSON.stringify(pending));
      return storage.getItem(operationKey(pending)) === JSON.stringify(pending);
    } catch {
      return false;
    }
  },

  /** Every unresolved operation for this administrator and account, oldest first. */
  list(adminId: string, accountId: string, storage: Storage = localStorage): PendingGrant[] {
    try {
      return storedKeys(storage, adminId, accountId)
        .map(key => parse(storage, key, adminId, accountId))
        .filter((pending): pending is PendingGrant => pending !== null)
        .sort(order);
    } catch {
      return [];
    }
  },

  read(adminId: string, accountId: string, storage: Storage = localStorage): PendingGrant | null {
    return grantDrafts.list(adminId, accountId, storage)[0] ?? null;
  },

  holds(pending: PendingGrant, storage: Storage = localStorage): boolean {
    return grantDrafts.list(pending.adminId, pending.accountId, storage)
      .some(item => item.command.operationKey === pending.command.operationKey);
  },

  /**
   * Claims the account for a new operation. Returns the operation that must proceed: `pending` itself when no other
   * operation is unresolved, otherwise the oldest existing one (and `pending` leaves no record). Null when the
   * browser could not keep the request, which must then not be sent.
   */
  acquire(pending: PendingGrant, storage: Storage = localStorage): PendingGrant | null {
    const existing = grantDrafts.read(pending.adminId, pending.accountId, storage);
    if (existing) return existing;
    if (!grantDrafts.write(pending, storage)) return null;
    const first = grantDrafts.read(pending.adminId, pending.accountId, storage);
    if (!first || first.command.operationKey === pending.command.operationKey) return pending;
    grantDrafts.clear(pending, storage);
    return first;
  },

  /** Removes only this exact operation, wherever this browser kept it. */
  clear(pending: PendingGrant, storage: Storage = localStorage) {
    try {
      for (const key of storedKeys(storage, pending.adminId, pending.accountId)) {
        if (parse(storage, key, pending.adminId, pending.accountId)?.command.operationKey === pending.command.operationKey)
          storage.removeItem(key);
      }
    } catch {
      // A retained record only offers recovery of the same original operation again.
    }
  },
};
