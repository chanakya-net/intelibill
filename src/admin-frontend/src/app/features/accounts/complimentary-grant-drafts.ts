import type { GrantCommand } from './complimentary-grant-api.service';

/** A confirmation frozen before HTTP: owned by one administrator and one account, never retargeted. */
export interface PendingGrant {
  readonly adminId: string;
  readonly accountId: string;
  readonly command: GrantCommand;
}

const prefix = 'intelibill.admin.complimentary-grant.v1.';
const key = (adminId: string, accountId: string) => `${prefix}${adminId}.${accountId}`;

/**
 * Durable per-administrator, per-account pending grant. Writing must succeed before the request is sent; a
 * storage failure returns false so the caller refuses to send rather than risk an unrecoverable outcome.
 */
export const grantDrafts = {
  write(pending: PendingGrant, storage: Storage = localStorage): boolean {
    try {
      storage.setItem(key(pending.adminId, pending.accountId), JSON.stringify(pending));
      return storage.getItem(key(pending.adminId, pending.accountId)) === JSON.stringify(pending);
    } catch {
      return false;
    }
  },

  read(adminId: string, accountId: string, storage: Storage = localStorage): PendingGrant | null {
    try {
      const raw = storage.getItem(key(adminId, accountId));
      const pending = raw ? JSON.parse(raw) as PendingGrant : null;
      return pending && pending.adminId === adminId && pending.accountId === accountId
        && typeof pending.command?.operationKey === 'string' ? pending : null;
    } catch {
      return null;
    }
  },

  clear(adminId: string, accountId: string, storage: Storage = localStorage) {
    try {
      storage.removeItem(key(adminId, accountId));
    } catch {
      // A retained record only offers recovery of the same original operation again.
    }
  },
};
