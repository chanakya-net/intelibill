import { Injectable } from '@angular/core';

import { BillingAccountDraft } from './billing-account.models';

export const BILLING_ACCOUNT_DRAFT_KEY = 'intelibill.billing.accountDraft.v1';

type StoredDrafts = Record<string, { operationKey: string; name: string }>;

/**
 * Unfinished billing account creations, one per signed-in person. A draft is written before the
 * request leaves the browser and removed only after a definitive answer, so a reload or a lost
 * response retries with the same operation key and name instead of creating a second account.
 */
@Injectable({ providedIn: 'root' })
export class BillingAccountDraftStore {
  load(ownerUserId: string): BillingAccountDraft | null {
    const stored = this.read()[ownerUserId];
    return stored ? { ownerUserId, operationKey: stored.operationKey, name: stored.name } : null;
  }

  save(draft: BillingAccountDraft): void {
    this.write({
      ...this.read(),
      [draft.ownerUserId]: { operationKey: draft.operationKey, name: draft.name },
    });
  }

  clear(ownerUserId: string): void {
    const drafts = this.read();
    if (!(ownerUserId in drafts)) return;
    delete drafts[ownerUserId];
    this.write(drafts);
  }

  private read(): StoredDrafts {
    try {
      const parsed: unknown = JSON.parse(globalThis.localStorage?.getItem(BILLING_ACCOUNT_DRAFT_KEY) ?? '{}');
      if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return {};
      return Object.fromEntries(
        Object.entries(parsed as Record<string, unknown>).filter(([, value]) => isStoredDraft(value)),
      ) as StoredDrafts;
    } catch {
      return {};
    }
  }

  private write(drafts: StoredDrafts): void {
    const storage = globalThis.localStorage;
    if (Object.keys(drafts).length === 0) storage?.removeItem(BILLING_ACCOUNT_DRAFT_KEY);
    else storage?.setItem(BILLING_ACCOUNT_DRAFT_KEY, JSON.stringify(drafts));
  }
}

function isStoredDraft(value: unknown): value is { operationKey: string; name: string } {
  const draft = value as { operationKey?: unknown; name?: unknown } | null;
  return (
    typeof draft === 'object' &&
    draft !== null &&
    typeof draft.operationKey === 'string' &&
    draft.operationKey.length > 0 &&
    typeof draft.name === 'string'
  );
}
