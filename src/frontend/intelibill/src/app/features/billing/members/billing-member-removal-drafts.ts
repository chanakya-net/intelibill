import { Injectable } from '@angular/core';
import type { RemoveManager } from './billing-members.service';

export interface RemovalDraft extends RemoveManager {
  readonly memberId: string;
}

type StoredDraft = RemovalDraft & { readonly savedAt: number };

@Injectable({ providedIn: 'root' })
export class BillingMemberRemovalDrafts {
  load(user: string, account: string): RemovalDraft | null {
    const prefix = this.prefix(user, account);
    const attempts: StoredDraft[] = [];
    for (let index = 0; index < localStorage.length; index++) {
      const key = localStorage.key(index);
      if (!key?.startsWith(prefix)) continue;
      const raw = localStorage.getItem(key);
      if (raw === null) continue;
      const draft = JSON.parse(raw) as StoredDraft;
      if (!draft || key !== prefix + draft.operationKey || typeof draft.operationKey !== 'string' ||
          typeof draft.memberId !== 'string' || typeof draft.reason !== 'string' ||
          !Number.isSafeInteger(draft.expectedVersion) || draft.expectedVersion < 1 || !Number.isFinite(draft.savedAt)) {
        throw new Error('Invalid billing member removal draft');
      }
      attempts.push(draft);
    }
    return attempts.sort((a, b) => a.savedAt - b.savedAt || a.operationKey.localeCompare(b.operationKey))[0] ?? null;
  }

  save(user: string, account: string, draft: RemovalDraft): void {
    localStorage.setItem(this.prefix(user, account) + draft.operationKey,
      JSON.stringify({ ...draft, savedAt: Date.now() }));
  }

  settle(user: string, account: string, key: string): void {
    localStorage.removeItem(this.prefix(user, account) + key);
  }

  private prefix(user: string, account: string): string {
    return 'intelibill.billing.memberRemoval.v1:' + JSON.stringify([user, account]) + ':';
  }
}
