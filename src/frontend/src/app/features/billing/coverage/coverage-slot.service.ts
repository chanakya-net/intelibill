import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { scopedRequestContext } from '../../../core/context/scoped-request';
import { isUuid, type CheckoutScope } from '../checkout/checkout.models';

export interface SlotScope extends CheckoutScope { term: string; assignment: string; target: string }
export interface Assignment {
  assignmentId: string; accountId: string; termId: string; shopId: string; shopName: string;
  state: string; reason: string | null; activation: string; coverageEndsAt: string;
}
export interface SlotTerm {
  termId: string; capacity: number; unusedSlots: number; paidThrough: string;
  eligibleShops: {shopId: string; shopName: string}[]; assignments: Assignment[]; hasMore: boolean;
}
export type Decision = 'grant' | 'accept' | 'revoke';
export interface SlotIntent {
  operationKey: string; action: 'assign' | Decision; shopId: string;
  observedCapacity: number; observedPaidThrough: string;
}
export interface OwnerConsent { assignment: Assignment; status: string }

@Injectable({ providedIn: 'root' })
export class CoverageSlotService {
  private readonly http = inject(HttpClient);

  read(scope: SlotScope, page: number) {
    return firstValueFrom(this.http.get<SlotTerm>(this.path(scope), { params: { page }, context: scopedRequestContext(scope) }));
  }

  owner(scope: SlotScope) {
    return firstValueFrom(this.http.get<OwnerConsent>(this.path(scope), { context: scopedRequestContext(scope) }));
  }

  async submit(scope: SlotScope, intent: SlotIntent) {
    const options = { context: scopedRequestContext(scope) };
    if (intent.action === 'assign') {
      const { action: _action, ...request } = intent;
      const row = await firstValueFrom(this.http.post<Assignment>(this.path(scope),
        { ...request, acknowledgedSharedEnd: true, acknowledgedNoNewFee: true }, options));
      if (row.accountId !== scope.account || row.termId !== scope.term || row.shopId !== intent.shopId ||
          Date.parse(row.coverageEndsAt) !== Date.parse(intent.observedPaidThrough)) throw new Error('Unexpected assignment');
    } else {
      const row = await firstValueFrom(this.http.post<OwnerConsent>(this.path(scope) + '/' + intent.action,
        { operationKey: intent.operationKey }, options));
      if (row.assignment.assignmentId !== scope.assignment || row.assignment.shopId !== scope.target)
        throw new Error('Unexpected consent');
    }
  }

  private path(s: SlotScope) {
    const prefix = API_BASE_URL + '/billing/accounts/' + encodeURIComponent(s.account);
    return s.assignment ? prefix + '/slot-assignments/' + encodeURIComponent(s.target) + '/' + encodeURIComponent(s.assignment) + '/consent'
      : prefix + '/subscriptions/' + encodeURIComponent(s.term) + '/slot-assignments';
  }
}

@Injectable({ providedIn: 'root' })
export class CoverageSlotDrafts {
  private key(s: SlotScope) {
    return 'intelibill.billing.slots.v1:' + JSON.stringify([s.userId, s.shopId, s.account, s.term, s.target, s.assignment]);
  }

  read(s: SlotScope): SlotIntent | null {
    const raw = localStorage.getItem(this.key(s));
    if (raw === null) return null;
    const draft = JSON.parse(raw) as SlotIntent;
    if (!isUuid(draft?.operationKey) || !isUuid(draft.shopId) || !Number.isFinite(Date.parse(draft.observedPaidThrough)) ||
        !Number.isSafeInteger(draft.observedCapacity) || draft.observedCapacity < 1 ||
        (s.assignment ? !['grant','accept','revoke'].includes(draft.action) || draft.shopId !== s.target : draft.action !== 'assign'))
      throw new Error('Invalid coverage slot draft');
    return draft;
  }

  freeze(s: SlotScope, command: SlotIntent) {
    return navigator.locks.request(this.key(s), () => {
      const saved = this.read(s);
      if (saved) return saved;
      localStorage.setItem(this.key(s), JSON.stringify(command));
      return command;
    });
  }

  settle(s: SlotScope, key: string) {
    return navigator.locks.request(this.key(s), () => {
      if (this.read(s)?.operationKey === key) localStorage.removeItem(this.key(s));
    });
  }
}
