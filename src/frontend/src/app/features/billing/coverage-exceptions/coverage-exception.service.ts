import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { scopedRequestContext } from '../../../core/context/scoped-request';
import { isUuid, type CheckoutScope } from '../checkout/checkout.models';

export interface EarlyReplacementScope extends CheckoutScope {
  term: string;
  target: string;
  exceptionId: string;
}

export interface EligibleShop { shopId: string; shopName: string }
export interface CoveredShop extends EligibleShop {
  observedPermitDeadline: string | null;
  lastContactAt: string | null;
  lastSynchronizedAt: string | null;
  outstandingPermitCount: number;
}

export type OverlapDisposition = 'Wait' | 'CourtesyBridge';
export type ConsentDecision = 'grant' | 'accept' | 'revoke';

export interface CoverageExceptionItem {
  exceptionId: string;
  accountId: string;
  termId: string;
  proposalRevision: number;
  termsDigest: string;
  outgoingShopId: string;
  outgoingShopName: string;
  incomingShopId: string;
  incomingShopName: string;
  capacity: number;
  memberAllowance: number;
  normalRenewalAt: string;
  requestedEarlyStartAt: string;
  paidCutoverAt: string;
  overlapDisposition: OverlapDisposition;
  observedPermitDeadline: string | null;
  disconnectedQueuesUnknown: boolean;
  requestReason: string;
  state: string;
  reason: string | null;
  outgoingConsentState: string;
  incomingConsentState: string;
  courtesyGrantId: string | null;
  courtesyStartsAt: string | null;
  courtesyEndsAt: string | null;
  outgoingActivation: string;
  incomingActivation: string;
  courtesyActivation: string;
}

export interface EarlyReplacementPreview {
  termId: string;
  capacity: number;
  memberAllowance: number;
  normalRenewalAt: string;
  observedAt: string;
  canRequest: boolean;
  disconnectedQueuesNotice: string;
  coveredShops: CoveredShop[];
  eligibleShops: EligibleShop[];
  exceptions: CoverageExceptionItem[];
  hasMore: boolean;
}

export interface EarlyReplacementOwnerView {
  exceptionId: string;
  accountId: string;
  termId: string;
  proposalRevision: number;
  termsDigest: string;
  leg: 'Outgoing' | 'Incoming';
  shopId: string;
  shopName: string;
  counterpartyShopName: string;
  normalRenewalAt: string;
  requestedEarlyStartAt: string;
  paidCutoverAt: string;
  overlapDisposition: OverlapDisposition;
  memberAllowance: number;
  observedPermitDeadline: string | null;
  disconnectedQueuesUnknown: boolean;
  state: string;
  consentStatus: string;
  legActivation: string;
  expiresAt: string;
}

export interface RequestEarlyReplacementBody {
  operationKey: string;
  outgoingShopId: string;
  incomingShopId: string;
  requestedEarlyStartAt: string;
  proposedPaidCutoverAt: string;
  overlapDisposition: OverlapDisposition;
  reason: string;
  observedCapacity: number;
  observedMemberAllowance: number;
  observedNormalRenewalAt: string;
  observedPermitDeadline: string | null;
  acknowledgedShopSelection: true;
  acknowledgedRemainingAccessAllocation: true;
  acknowledgedExistingPermits: true;
  acknowledgedUnknownDisconnectedQueues: true;
}

export type EarlyReplacementIntent =
  | { action: 'request'; label: string; body: RequestEarlyReplacementBody }
  | { action: ConsentDecision; label: string; body: { operationKey: string } };

@Injectable({ providedIn: 'root' })
export class CoverageExceptionService {
  private readonly http = inject(HttpClient);

  private path(s: EarlyReplacementScope) {
    const base = `${API_BASE_URL}/billing/accounts/${encodeURIComponent(s.account)}`;
    return s.exceptionId
      ? `${base}/coverage-replacement-exceptions/${encodeURIComponent(s.target)}/${encodeURIComponent(s.exceptionId)}/consent`
      : `${base}/subscriptions/${encodeURIComponent(s.term)}/coverage-replacement-exceptions`;
  }

  read(s: EarlyReplacementScope, page: number) {
    return firstValueFrom(this.http.get<EarlyReplacementPreview>(this.path(s), {
      params: { page }, context: scopedRequestContext(s),
    }));
  }

  owner(s: EarlyReplacementScope) {
    return firstValueFrom(this.http.get<EarlyReplacementOwnerView>(this.path(s), {
      context: scopedRequestContext(s),
    }));
  }

  recover(s: EarlyReplacementScope, key: string) {
    return firstValueFrom(this.http.get<CoverageExceptionItem>(
      `${this.path(s)}/operations/${encodeURIComponent(key)}`, { context: scopedRequestContext(s) },
    ));
  }

  async send(s: EarlyReplacementScope, intent: EarlyReplacementIntent) {
    const options = { context: scopedRequestContext(s) };
    if (intent.action === 'request') {
      const row = await firstValueFrom(this.http.post<CoverageExceptionItem>(this.path(s), intent.body, options));
      if (row.accountId !== s.account || row.termId !== s.term
        || row.outgoingShopId !== intent.body.outgoingShopId || row.incomingShopId !== intent.body.incomingShopId) {
        throw new Error('Unexpected early replacement response');
      }
    } else {
      const row = await firstValueFrom(this.http.post<EarlyReplacementOwnerView>(
        `${this.path(s)}/${intent.action}`, intent.body, options,
      ));
      if (row.exceptionId !== s.exceptionId || row.shopId !== s.target) {
        throw new Error('Unexpected owner consent response');
      }
    }
  }
}

@Injectable({ providedIn: 'root' })
export class CoverageExceptionDrafts {
  private key(s: EarlyReplacementScope) {
    return 'intelibill.billing.early-replacement.v1:' + JSON.stringify([s.userId, s.shopId, s.account, s.term, s.target, s.exceptionId]);
  }

  read(s: EarlyReplacementScope): EarlyReplacementIntent | null {
    const raw = localStorage.getItem(this.key(s));
    if (raw === null) return null;
    const draft = JSON.parse(raw) as EarlyReplacementIntent;
    if (!isUuid(draft?.body?.operationKey) || !draft.label
      || (s.exceptionId ? !['grant', 'accept', 'revoke'].includes(draft.action)
        : draft.action !== 'request' || !isUuid(draft.body.outgoingShopId) || !isUuid(draft.body.incomingShopId))) {
      throw new Error('Invalid early replacement draft');
    }
    return draft;
  }

  freeze(s: EarlyReplacementScope, command: EarlyReplacementIntent) {
    return navigator.locks.request(this.key(s), () => {
      const existing = this.read(s);
      if (existing) return existing;
      localStorage.setItem(this.key(s), JSON.stringify(command));
      return command;
    });
  }

  settle(s: EarlyReplacementScope, key: string) {
    return navigator.locks.request(this.key(s), () => {
      if (this.read(s)?.body.operationKey === key) localStorage.removeItem(this.key(s));
    });
  }
}
