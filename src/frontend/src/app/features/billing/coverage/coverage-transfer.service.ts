import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { scopedRequestContext } from '../../../core/context/scoped-request';
import { isUuid, type CheckoutScope } from '../checkout/checkout.models';

/** Route scope: `term` on the request page, `target` + `transferId` on the named shop owner's page. */
export interface TransferScope extends CheckoutScope {
  term: string;
  target: string;
  transferId: string;
}

export type TransferTreatment = 'RetainUntilPaidThrough' | 'ReviewedAllocation';
export type TransferPurpose = 'SourceBillingOwner' | 'DestinationBillingOwner' | 'ShopOwner';
export type TransferDecision = 'accept' | 'revoke';

export interface CoveredShop { shopId: string; shopName: string }

export interface TransferPreview {
  sourceAccountId: string;
  destinationAccountId: string;
  termId: string;
  shopId: string;
  shopName: string;
  sourceAffiliationId: string;
  sourcePaidThroughAt: string;
  sourceOwnerRevision: number;
  evidenceDigest: string;
  treatment: TransferTreatment;
  cutoverAt: string | null;
  observedPermitCount: number;
  observedPermitDeadline: string | null;
  disconnectedQueuesUnknown: boolean;
  disconnectedQueuesNotice: string;
  proposedExpiresAt: string;
}

export interface TransferConsent { purpose: TransferPurpose; status: string; generation: number; recordedAt: string | null }

/** Fields shared by the participant view and the named shop owner's view. */
export interface TransferTerms {
  transferId: string;
  proposalRevision: number;
  termsDigest: string;
  sourceAccountId: string;
  sourceAccountName: string;
  destinationAccountId: string;
  destinationAccountName: string | null;
  shopId: string;
  shopName: string;
  treatment: TransferTreatment;
  cutoverAt: string | null;
  sourcePaidThroughAt: string;
  observedPermitCount: number;
  observedPermitDeadline: string | null;
  disconnectedQueuesUnknown: boolean;
  expiresAt: string;
  state: string;
}

export interface TransferItem extends TransferTerms {
  stateReason: string | null;
  consents: TransferConsent[];
  consentsCollected: boolean;
  awaitingProviderReview: boolean;
  viewerPurposes: TransferPurpose[];
  viewerAuthorityRevision: number | null;
}

export interface TransferShopView extends TransferTerms {
  consentStatus: string;
  delivery: 'None' | 'Pending' | 'Recorded';
  authorityRevision: number;
}

export interface RequestTransferBody {
  operationKey: string;
  destinationAccountId: string;
  shopId: string;
  treatment: TransferTreatment;
  cutoverAt: string | null;
  reason: string;
  expectedSourceAffiliationId: string;
  expectedSourcePaidThroughAt: string;
  expectedSourceOwnerRevision: number;
  expectedEvidenceDigest: string;
  acknowledgedNamedShop: true;
  acknowledgedRemainingAccessTreatment: true;
  acknowledgedExistingPermits: true;
  acknowledgedUnknownDisconnectedQueues: true;
  acknowledgedHistoricalBillsStay: true;
}

export interface DecisionBody {
  operationKey: string;
  expectedProposalRevision: number;
  expectedTermsDigest: string;
  expectedAuthorityRevision: number;
}

/** One frozen user decision. It binds the acting account (scope), action, purpose, target and displayed revision. */
export type TransferIntent =
  | { action: 'request'; label: string; body: RequestTransferBody }
  | { action: TransferDecision; label: string; transferId: string; purpose: TransferPurpose; body: DecisionBody };

const segments: Record<TransferPurpose, string> = {
  SourceBillingOwner: 'source-billing-owner', DestinationBillingOwner: 'destination-billing-owner', ShopOwner: '',
};

@Injectable({ providedIn: 'root' })
export class CoverageTransferService {
  private readonly http = inject(HttpClient);

  private account(s: TransferScope) {
    return `${API_BASE_URL}/billing/accounts/${encodeURIComponent(s.account)}`;
  }

  private requests(s: TransferScope) {
    return `${this.account(s)}/subscriptions/${encodeURIComponent(s.term)}/coverage-transfers`;
  }

  private shop(s: TransferScope) {
    return `${this.account(s)}/coverage-transfer-shops/${encodeURIComponent(s.target)}/${encodeURIComponent(s.transferId)}/consent`;
  }

  /** The named shops this term covers, from the existing coverage read. */
  async coveredShops(s: TransferScope) {
    const row = await firstValueFrom(this.http.get<{ coveredShops: CoveredShop[] }>(
      `${this.account(s)}/subscriptions/${encodeURIComponent(s.term)}/coverage-replacement-exceptions`,
      { context: scopedRequestContext(s) }));
    return row.coveredShops.map(({ shopId, shopName }) => ({ shopId, shopName }));
  }

  preview(s: TransferScope, body: Pick<RequestTransferBody, 'destinationAccountId' | 'shopId' | 'treatment' | 'cutoverAt'>) {
    return firstValueFrom(this.http.post<TransferPreview>(`${this.requests(s)}/preview`, body, { context: scopedRequestContext(s) }));
  }

  async list(s: TransferScope) {
    const page = await firstValueFrom(this.http.get<{ accountId: string; items: TransferItem[] }>(
      `${this.account(s)}/coverage-transfers`, { context: scopedRequestContext(s) }));
    if (page.accountId !== s.account) throw new Error('Unexpected coverage transfer list');
    return page.items;
  }

  owner(s: TransferScope) {
    return firstValueFrom(this.http.get<TransferShopView>(this.shop(s), { context: scopedRequestContext(s) }));
  }

  async send(s: TransferScope, intent: TransferIntent) {
    const options = { context: scopedRequestContext(s) };
    if (intent.action === 'request') {
      const row = await firstValueFrom(this.http.post<TransferItem>(this.requests(s), intent.body, options));
      if (row.sourceAccountId !== s.account || row.shopId !== intent.body.shopId
        || row.destinationAccountId !== intent.body.destinationAccountId) throw new Error('Unexpected coverage transfer response');
    } else if (intent.purpose === 'ShopOwner') {
      const row = await firstValueFrom(this.http.post<TransferShopView>(`${this.shop(s)}/${intent.action}`, intent.body, options));
      if (row.transferId !== s.transferId || row.shopId !== s.target) throw new Error('Unexpected shop consent response');
    } else {
      const row = await firstValueFrom(this.http.post<TransferItem>(
        `${this.account(s)}/coverage-transfers/${encodeURIComponent(intent.transferId)}/consents/${segments[intent.purpose]}/${intent.action}`,
        intent.body, options));
      if (row.transferId !== intent.transferId) throw new Error('Unexpected consent response');
    }
  }
}

/** Durable per-owner intents: frozen before any HTTP call and removed only by a matching settled outcome. */
@Injectable({ providedIn: 'root' })
export class CoverageTransferDrafts {
  private key(s: TransferScope) {
    return 'intelibill.billing.coverage-transfer.v1:' + JSON.stringify([s.userId, s.shopId, s.account, s.term, s.target, s.transferId]);
  }

  read(s: TransferScope): TransferIntent | null {
    const raw = localStorage.getItem(this.key(s));
    if (raw === null) return null;
    const draft = JSON.parse(raw) as TransferIntent;
    const valid = isUuid(draft?.body?.operationKey) && !!draft.label && (draft.action === 'request'
      ? !!s.term && isUuid(draft.body.shopId) && isUuid(draft.body.destinationAccountId)
      : ['accept', 'revoke'].includes(draft.action) && isUuid(draft.transferId) && draft.purpose in segments);
    if (!valid) throw new Error('Invalid coverage transfer draft');
    return draft;
  }

  freeze(s: TransferScope, command: TransferIntent) {
    return navigator.locks.request(this.key(s), () => {
      const existing = this.read(s);
      if (existing) return existing;
      localStorage.setItem(this.key(s), JSON.stringify(command));
      return command;
    });
  }

  settle(s: TransferScope, key: string) {
    return navigator.locks.request(this.key(s), () => {
      if (this.read(s)?.body.operationKey === key) localStorage.removeItem(this.key(s));
    });
  }
}
