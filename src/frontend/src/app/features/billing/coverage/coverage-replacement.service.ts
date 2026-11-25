import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { scopedRequestContext } from '../../../core/context/scoped-request';
import { isUuid, type CheckoutScope } from '../checkout/checkout.models';

export interface ReplacementScope extends CheckoutScope { term: string; target: string; replacement: string }
export interface Shop { shopId: string; shopName: string }
export interface CoveredShop extends Shop { permitDeadline: string | null }
export interface Replacement {
  replacementId: string; accountId: string; termId: string; outgoingShopId: string; outgoingShopName: string;
  incomingShopId: string; incomingShopName: string; permitDeadline: string | null; state: string; activation: string;
}
export interface Preview {
  termId: string; canSchedule: boolean; capacity: number; memberAllowance: number; effectiveAt: string; coveredShops: CoveredShop[];
  eligibleShops: Shop[]; replacements: Replacement[]; hasMore: boolean;
}
export interface OwnerView extends Shop { replacementId: string; effectiveAt: string; state: string; status: string; activation: string }
export type Decision = 'accept' | 'grant' | 'revoke';
export interface Schedule {
  operationKey: string; outgoingShopId: string; incomingShopId: string; observedCapacity: number;
  observedMemberAllowance: number; observedEffectiveAt: string; observedPermitDeadline: string | null;
  acknowledgedRenewal: true; acknowledgedPermitWait: true; acknowledgedShopSelection: true;
}
export type Intent = { action: 'schedule'; label: string; body: Schedule } |
  { action: Decision; label: string; body: { operationKey: string } };

@Injectable({ providedIn: 'root' })
export class CoverageReplacementService {
  private readonly http = inject(HttpClient);

  private path(s: ReplacementScope) {
    const base = API_BASE_URL + '/billing/accounts/' + encodeURIComponent(s.account);
    return s.replacement ? base + '/coverage-replacements/' + encodeURIComponent(s.target) + '/' + encodeURIComponent(s.replacement) + '/consent'
      : base + '/subscriptions/' + encodeURIComponent(s.term) + '/coverage-replacements';
  }

  read(s: ReplacementScope, page: number) {
    return firstValueFrom(this.http.get<Preview>(this.path(s), { params: { page }, context: scopedRequestContext(s) }));
  }

  owner(s: ReplacementScope) {
    return firstValueFrom(this.http.get<OwnerView>(this.path(s), { context: scopedRequestContext(s) }));
  }

  async send(s: ReplacementScope, intent: Intent) {
    const options = { context: scopedRequestContext(s) };
    if (intent.action === 'schedule') {
      const row = await firstValueFrom(this.http.post<Replacement>(this.path(s), intent.body, options));
      if (row.accountId !== s.account || row.termId !== s.term || row.incomingShopId !== intent.body.incomingShopId ||
          row.outgoingShopId !== intent.body.outgoingShopId) throw new Error('Unexpected replacement');
    } else {
      const row = await firstValueFrom(this.http.post<OwnerView>(this.path(s) + '/' + intent.action, intent.body, options));
      if (row.replacementId !== s.replacement || row.shopId !== s.target) throw new Error('Unexpected owner decision');
    }
  }
}

@Injectable({ providedIn: 'root' })
export class ReplacementDrafts {
  private key(s: ReplacementScope) {
    return 'intelibill.billing.replacement.v1:' + JSON.stringify([s.userId, s.shopId, s.account, s.term, s.target, s.replacement]);
  }

  read(s: ReplacementScope): Intent | null {
    const raw = localStorage.getItem(this.key(s));
    if (raw === null) return null;
    const draft = JSON.parse(raw) as Intent;
    if (!isUuid(draft?.body?.operationKey) || !draft.label ||
        (s.replacement ? !['grant', 'accept', 'revoke'].includes(draft.action) :
          draft.action !== 'schedule' || !isUuid(draft.body.outgoingShopId) || !isUuid(draft.body.incomingShopId)))
      throw new Error('Invalid replacement draft');
    return draft;
  }

  freeze(s: ReplacementScope, command: Intent) {
    return navigator.locks.request(this.key(s), () => {
      const existing = this.read(s);
      if (existing) return existing;
      localStorage.setItem(this.key(s), JSON.stringify(command));
      return command;
    });
  }

  settle(s: ReplacementScope, key: string) {
    return navigator.locks.request(this.key(s), () => {
      if (this.read(s)?.body.operationKey === key) localStorage.removeItem(this.key(s));
    });
  }
}
