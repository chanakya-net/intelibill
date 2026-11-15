import { Injectable } from '@angular/core';

import { OWNERSHIP_POLICY, type OwnershipDraft } from './billing-ownership-transfer.service';

@Injectable({ providedIn: 'root' })
export class BillingOwnershipTransferDrafts {
  list(actor: string): OwnershipDraft[] {
    const prefix = this.prefix(actor);
    const drafts: OwnershipDraft[] = [];
    for (let index = 0; index < localStorage.length; index++) {
      const key = localStorage.key(index);
      if (!key?.startsWith(prefix)) continue;
      const raw = localStorage.getItem(key);
      if (raw === null) continue;
      const draft = JSON.parse(raw) as OwnershipDraft;
      if (!this.valid(draft) || key !== this.key(actor, draft)) {
        throw new Error('Invalid billing ownership transfer draft');
      }
      drafts.push(draft);
    }
    return drafts;
  }

  save(actor: string, draft: OwnershipDraft): void {
    if (!this.valid(draft)) throw new Error('Invalid billing ownership transfer draft');
    const key = this.key(actor, draft);
    const payload = JSON.stringify(draft);
    const previous = localStorage.getItem(key);
    if (previous !== null && previous !== payload) throw new Error('Billing operation already has a payload');
    localStorage.setItem(key, payload);
  }

  settle(actor: string, draft: OwnershipDraft): void {
    localStorage.removeItem(this.key(actor, draft));
  }

  private prefix(actor: string): string {
    return 'intelibill.billing.ownership.v1:' + JSON.stringify(actor) + ':';
  }

  private key(actor: string, draft: OwnershipDraft): string {
    return this.prefix(actor) + JSON.stringify([draft.action, draft.accountId, draft.target, draft.body.operationKey]);
  }

  private valid(draft: OwnershipDraft | null): draft is OwnershipDraft {
    if (!draft?.body || !this.id(draft.target) || !this.id(draft.accountId) || !this.id(draft.body.operationKey) ||
        !Number.isSafeInteger(draft.body.expectedVersion) || draft.body.expectedVersion < 1) return false;
    if (draft.action === 'accept') {
      return draft.body.acceptResponsibility === true && draft.body.responsibilityPolicyVersion === OWNERSHIP_POLICY;
    }
    return draft.action === 'issue' && draft.target === draft.accountId && draft.body.confirmTransfer === true &&
      ['email', 'phone'].includes(draft.body.recipientKind) &&
      typeof draft.body.recipient === 'string' && draft.body.recipient.length > 0 && draft.body.recipient.length <= 254 &&
      typeof draft.body.reason === 'string' && draft.body.reason.length > 0 && draft.body.reason.length <= 500;
  }

  private id(value: unknown): value is string {
    return typeof value === 'string' && value.length > 0 && value.length <= 64;
  }
}
