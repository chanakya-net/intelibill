import { Injectable } from '@angular/core';

import { BillingAccountDraft } from './billing-account.models';

export const BILLING_ACCOUNT_DRAFT_PREFIX = 'intelibill.billing.accountDraft.v2:';

type DraftIdentity = Pick<BillingAccountDraft, 'ownerUserId' | 'operationKey'>;
type StoredDraft = BillingAccountDraft & { readonly savedAt: number };

/**
 * Unfinished billing account creations of each signed-in person. Every attempt has its own storage
 * entry, written before the request leaves the browser and removed only after a definitive answer
 * for that exact operation key. Tabs and page instances therefore never overwrite or settle each
 * other's attempts, and a reload or a lost response retries with the original key and name instead
 * of creating a second account.
 */
@Injectable({ providedIn: 'root' })
export class BillingAccountDraftStore {
  /** The oldest unfinished attempt of the person, if any. */
  load(ownerUserId: string): BillingAccountDraft | null {
    return this.list(ownerUserId)[0] ?? null;
  }

  /** Every unfinished attempt of the person, oldest first. */
  list(ownerUserId: string): readonly BillingAccountDraft[] {
    const prefix = ownerPrefix(ownerUserId);
    return storageKeys()
      .filter((key) => key.startsWith(prefix))
      .map((key) => readDraft(key))
      .filter((draft): draft is StoredDraft => draft !== null && draft.ownerUserId === ownerUserId)
      .sort((x, y) => x.savedAt - y.savedAt || x.operationKey.localeCompare(y.operationKey))
      .map(({ ownerUserId: owner, operationKey, name }) => ({ ownerUserId: owner, operationKey, name }));
  }

  save(draft: BillingAccountDraft): void {
    const stored: StoredDraft = { ...draft, savedAt: Date.now() };
    globalThis.localStorage?.setItem(entryKey(draft), JSON.stringify(stored));
  }

  /** Settles exactly this attempt; any other attempt of the same person is left untouched. */
  clear(draft: DraftIdentity): void {
    globalThis.localStorage?.removeItem(entryKey(draft));
  }
}

function ownerPrefix(ownerUserId: string): string {
  return `${BILLING_ACCOUNT_DRAFT_PREFIX}${encodeURIComponent(ownerUserId)}:`;
}

function entryKey(draft: DraftIdentity): string {
  return ownerPrefix(draft.ownerUserId) + encodeURIComponent(draft.operationKey);
}

function storageKeys(): string[] {
  const storage = globalThis.localStorage;
  if (!storage) return [];
  const keys: string[] = [];
  for (let index = 0; index < storage.length; index++) {
    const key = storage.key(index);
    if (key !== null) keys.push(key);
  }
  return keys;
}

function readDraft(key: string): StoredDraft | null {
  try {
    const value: unknown = JSON.parse(globalThis.localStorage?.getItem(key) ?? 'null');
    return isStoredDraft(value) && entryKey(value) === key ? value : null;
  } catch {
    return null;
  }
}

function isStoredDraft(value: unknown): value is StoredDraft {
  const draft = value as Partial<Record<keyof StoredDraft, unknown>> | null;
  return (
    typeof draft === 'object' &&
    draft !== null &&
    typeof draft.ownerUserId === 'string' &&
    typeof draft.operationKey === 'string' &&
    draft.operationKey.length > 0 &&
    typeof draft.name === 'string' &&
    typeof draft.savedAt === 'number'
  );
}
