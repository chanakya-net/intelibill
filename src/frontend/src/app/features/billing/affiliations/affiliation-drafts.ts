import { Injectable } from '@angular/core';

interface Draft {
  readonly operationKey: string;
  readonly shopId?: string;
  readonly termId?: string | null;
  readonly withoutCoverage?: boolean;
  readonly decision?: 'grant' | 'revoke' | 'accept';
}

type StoredDraft = Draft & { readonly savedAt: number };

@Injectable({ providedIn: 'root' })
export class AffiliationDrafts {
  read(scope: readonly string[]): Draft | null {
    const prefix = this.prefix(scope);
    const entries: StoredDraft[] = [];
    for (let index = 0; index < localStorage.length; index++) {
      const key = localStorage.key(index);
      if (!key?.startsWith(prefix)) continue;
      const stored = localStorage.getItem(key);
      if (stored === null) continue;
      const draft = JSON.parse(stored) as StoredDraft;
      if (typeof draft?.operationKey !== 'string' || typeof draft.savedAt !== 'number' ||
        key !== prefix + draft.operationKey) throw new Error('Invalid affiliation draft');
      entries.push(draft);
    }
    const oldest = entries.sort((a, b) => a.savedAt - b.savedAt || a.operationKey.localeCompare(b.operationKey))[0];
    if (!oldest) return null;
    const { savedAt: _savedAt, ...draft } = oldest;
    return draft;
  }

  freeze(scope: readonly string[], body: Omit<Draft, 'operationKey'>): Draft {
    const existing = this.read(scope);
    if (existing) return existing;
    const draft = { ...body, operationKey: crypto.randomUUID() };
    // Distinct entries preserve both attempts if two tabs start before either sees the other's save.
    localStorage.setItem(this.prefix(scope) + draft.operationKey, JSON.stringify({ ...draft, savedAt: Date.now() }));
    return draft;
  }

  settle(scope: readonly string[], operationKey: string): void {
    localStorage.removeItem(this.prefix(scope) + operationKey);
  }

  private prefix(scope: readonly string[]): string {
    return 'intelibill.billing.affiliation.v1:' + JSON.stringify(scope) + ':';
  }
}
