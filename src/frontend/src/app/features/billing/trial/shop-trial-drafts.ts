import { Injectable } from '@angular/core';
import { ownerKey, type ScopeOwner } from '../../../core/context/shop-scope';
import type { TrialAcceptance, TrialPolicy } from './shop-trial.service';

interface SavedTrial {
  readonly savedAt: number;
  readonly acceptance: TrialAcceptance;
}

@Injectable({ providedIn: 'root' })
export class ShopTrialDrafts {
  read(owner: ScopeOwner): TrialAcceptance | null {
    const prefix = this.prefix(owner);
    const entries: SavedTrial[] = [];
    for (let index = 0; index < localStorage.length; index++) {
      const key = localStorage.key(index);
      if (!key?.startsWith(prefix)) continue;
      const value = localStorage.getItem(key);
      if (value === null) continue;
      const data = JSON.parse(value) as SavedTrial;
      const acceptance = data?.acceptance;
      if (!acceptance || acceptance.termsAccepted !== true || typeof acceptance.policyId !== 'string' ||
          !Number.isSafeInteger(acceptance.policyRevision) || typeof acceptance.termsVersion !== 'string' ||
          typeof data.savedAt !== 'number' || key !== prefix + acceptance.operationKey)
        throw new Error('Invalid recorded trial acceptance');
      entries.push(data);
    }
    return entries.sort((a, b) => a.savedAt - b.savedAt || a.acceptance.operationKey.localeCompare(b.acceptance.operationKey))
      [0]?.acceptance ?? null;
  }

  freeze(owner: ScopeOwner, policy: TrialPolicy): { acceptance: TrialAcceptance; created: boolean } {
    const existing = this.read(owner);
    if (existing) return { acceptance: existing, created: false };
    const { policyId, policyRevision, termsVersion } = policy;
    const acceptance: TrialAcceptance = { operationKey: crypto.randomUUID(), policyId, policyRevision,
      termsVersion, termsAccepted: true };
    localStorage.setItem(this.prefix(owner) + acceptance.operationKey, JSON.stringify({ acceptance, savedAt: Date.now() }));
    return { acceptance, created: true };
  }

  settle(owner: ScopeOwner, operationKey: string): void {
    localStorage.removeItem(this.prefix(owner) + operationKey);
  }

  private prefix(owner: ScopeOwner): string {
    return 'intelibill.billing.trial.v1:' + ownerKey(owner) + ':';
  }
}
