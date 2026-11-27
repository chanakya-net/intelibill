import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, switchMap } from 'rxjs';

export type OverlapDisposition = 'Wait' | 'CourtesyBridge';

export interface CoverageExceptionSummary {
  readonly exceptionId: string;
  readonly accountId: string;
  readonly termId: string;
  readonly proposalRevision: number;
  readonly outgoingShopId: string;
  readonly outgoingShopName: string;
  readonly incomingShopId: string;
  readonly incomingShopName: string;
  readonly normalRenewalAt: string;
  readonly requestedEarlyStartAt: string;
  readonly paidCutoverAt: string;
  readonly overlapDisposition: OverlapDisposition;
  readonly observedPermitDeadline: string | null;
  readonly state: string;
  readonly outgoingConsentState: string;
  readonly incomingConsentState: string;
  readonly outgoingActivation: string;
  readonly incomingActivation: string;
  readonly courtesyActivation: string;
  readonly courtesyGrantId: string | null;
  readonly createdAt: string;
}

export interface CoverageExceptionList {
  readonly accountId: string;
  readonly evaluatedAt: string;
  readonly items: readonly CoverageExceptionSummary[];
}

export interface CoverageExceptionConsent {
  readonly leg: 'Outgoing' | 'Incoming';
  readonly shopId: string;
  readonly shopName: string;
  readonly status: string;
  readonly eventId: string | null;
  readonly actorId: string | null;
  readonly authorityVersion: number | null;
  readonly activeMembers: number | null;
  readonly recordedAt: string | null;
}

export interface CoverageExceptionApproval {
  readonly approvalId: string;
  readonly exceptionId: string;
  readonly accountId: string;
  readonly termId: string;
  readonly generation: number;
  readonly operationKey: string;
  readonly adminId: string;
  readonly disposition: OverlapDisposition;
  readonly reason: string;
  readonly decidedAt: string;
  readonly state: string;
  readonly outcomeReason: string | null;
  readonly merchantPermitCount: number | null;
  readonly merchantMaxPermitEnd: string | null;
  readonly courtesyGrantId: string | null;
  readonly courtesyTermId: string | null;
  readonly courtesyStartsAt: string | null;
  readonly courtesyEndsAt: string | null;
  readonly paidCutoverAt: string;
  readonly normalRenewalAt: string;
  readonly outgoingActivation: string;
  readonly incomingActivation: string;
  readonly courtesyActivation: string;
  readonly appliedAt: string | null;
}

export interface CoverageExceptionPreview {
  readonly exceptionId: string;
  readonly accountId: string;
  readonly termId: string;
  readonly proposalRevision: number;
  readonly sourceHash: string;
  readonly termsDigest: string;
  readonly grantScopeVersion: number;
  readonly outgoingShopId: string;
  readonly outgoingShopName: string;
  readonly outgoingAffiliationId: string;
  readonly incomingShopId: string;
  readonly incomingShopName: string;
  readonly incomingAffiliationId: string;
  readonly capacity: number;
  readonly memberAllowance: number;
  readonly normalRenewalAt: string;
  readonly requestedEarlyStartAt: string;
  readonly paidCutoverAt: string;
  readonly overlapDisposition: OverlapDisposition;
  readonly observedPermitDeadline: string | null;
  readonly observedAt: string;
  readonly lastContactAt: string | null;
  readonly lastSynchronizedAt: string | null;
  readonly observedPermitCount: number;
  readonly disconnectedQueuesUnknown: boolean;
  readonly disconnectedQueuesNotice: string;
  readonly billingOwnerUserId: string;
  readonly billingOwnerRevision: number;
  readonly billingOwnerCurrent: boolean;
  readonly billingOwnerAcknowledged: boolean;
  readonly outgoingConsent: CoverageExceptionConsent;
  readonly incomingConsent: CoverageExceptionConsent;
  readonly requestReason: string;
  readonly state: string;
  readonly stateReason: string | null;
  readonly canApprove: boolean;
  readonly blockingReasons: readonly string[];
  readonly courtesyGrantWillBeCreated: boolean;
  readonly courtesyStartsAt: string | null;
  readonly courtesyEndsAt: string | null;
  readonly approval: CoverageExceptionApproval | null;
}

export interface ApproveCoverageExceptionCommand {
  readonly operationKey: string;
  readonly expectedProposalRevision: number;
  readonly expectedSourceHash: string;
  readonly expectedTermsDigest: string;
  readonly expectedGrantVersion: number;
  readonly disposition: OverlapDisposition;
  readonly reason: string;
  readonly acknowledgedPermitsAndUnknownQueues: true;
  readonly confirmed: true;
}

const uuid = /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i;
const hex64 = /^[0-9a-f]{64}$/;
const str = (v: unknown): v is string => typeof v === 'string' && v.length > 0;
const optStr = (v: unknown) => v === null || typeof v === 'string';
const optUuid = (v: unknown) => v === null || (typeof v === 'string' && uuid.test(v));
const isDisp = (v: unknown): v is OverlapDisposition => v === 'Wait' || v === 'CourtesyBridge';

export function normalizeReviewReason(value: string): string | null {
  const trimmed = value.trim();
  if (!trimmed || trimmed.length > 500 || /[\x00-\x1f\x7f]/.test(trimmed)) return null;
  return trimmed;
}

function isConsent(v: unknown, leg: 'Outgoing' | 'Incoming'): v is CoverageExceptionConsent {
  if (!v || typeof v !== 'object') return false;
  const r = v as Record<string, unknown>;
  return r['leg'] === leg && str(r['shopId']) && uuid.test(r['shopId']) && str(r['shopName'])
    && str(r['status']) && optUuid(r['eventId']) && optUuid(r['actorId']);
}

function isApproval(v: unknown, account: string, exceptionId: string): v is CoverageExceptionApproval {
  if (!v || typeof v !== 'object') return false;
  const r = v as Record<string, unknown>;
  return r['accountId'] === account && r['exceptionId'] === exceptionId
    && str(r['approvalId']) && uuid.test(r['approvalId'])
    && str(r['operationKey']) && uuid.test(r['operationKey'])
    && str(r['adminId']) && uuid.test(r['adminId'])
    && isDisp(r['disposition']) && str(r['reason']) && str(r['state'])
    && optUuid(r['courtesyGrantId']) && str(r['outgoingActivation'])
    && str(r['incomingActivation']) && str(r['courtesyActivation']);
}

function isPreview(v: unknown, account: string, exceptionId: string): v is CoverageExceptionPreview {
  if (!v || typeof v !== 'object') return false;
  const r = v as Record<string, unknown>;
  return r['accountId'] === account && r['exceptionId'] === exceptionId
    && typeof r['proposalRevision'] === 'number' && r['proposalRevision'] > 0
    && str(r['sourceHash']) && hex64.test(r['sourceHash'])
    && str(r['termsDigest']) && hex64.test(r['termsDigest'])
    && typeof r['grantScopeVersion'] === 'number' && r['grantScopeVersion'] >= 0
    && str(r['outgoingShopName']) && str(r['incomingShopName'])
    && isDisp(r['overlapDisposition']) && typeof r['disconnectedQueuesUnknown'] === 'boolean'
    && str(r['disconnectedQueuesNotice']) && typeof r['canApprove'] === 'boolean'
    && Array.isArray(r['blockingReasons']) && optStr(r['observedPermitDeadline'])
    && isConsent(r['outgoingConsent'], 'Outgoing') && isConsent(r['incomingConsent'], 'Incoming')
    && (r['approval'] === null || isApproval(r['approval'], account, exceptionId));
}

function isList(v: unknown, account: string): v is CoverageExceptionList {
  if (!v || typeof v !== 'object') return false;
  const r = v as Record<string, unknown>;
  return r['accountId'] === account && str(r['evaluatedAt']) && Array.isArray(r['items'])
    && r['items'].every((item: unknown) => {
      if (!item || typeof item !== 'object') return false;
      const x = item as Record<string, unknown>;
      return x['accountId'] === account && str(x['exceptionId']) && uuid.test(x['exceptionId'])
        && str(x['outgoingShopName']) && str(x['incomingShopName']) && isDisp(x['overlapDisposition']) && str(x['state']);
    });
}

@Injectable({ providedIn: 'root' })
export class CoverageExceptionApi {
  private readonly http = inject(HttpClient);

  async list(account: string, session: string): Promise<CoverageExceptionList> {
    const raw = await firstValueFrom(this.http.get<unknown>(
      `/api/admin/accounts/${encodeURIComponent(account)}/coverage-replacement-exceptions`,
      { headers: { 'X-Admin-Session': session } },
    ));
    if (!isList(raw, account)) throw new Error('Invalid coverage exception list');
    return raw;
  }

  async preview(account: string, exceptionId: string, session: string): Promise<CoverageExceptionPreview> {
    const raw = await this.post(account, exceptionId, '/preview', {}, session);
    if (!isPreview(raw, account, exceptionId)) throw new Error('Invalid coverage exception preview');
    return raw;
  }

  async approve(account: string, exceptionId: string, session: string,
    command: ApproveCoverageExceptionCommand): Promise<CoverageExceptionApproval> {
    const raw = await this.post(account, exceptionId, '/approve', command, session);
    if (!isApproval(raw, account, exceptionId)) throw new Error('Invalid coverage exception approval');
    return raw;
  }

  async recover(account: string, exceptionId: string, operationKey: string, session: string): Promise<CoverageExceptionApproval> {
    const raw = await firstValueFrom(this.http.get<unknown>(
      `${this.path(account, exceptionId)}/operations/${encodeURIComponent(operationKey)}`,
      { headers: { 'X-Admin-Session': session } },
    ));
    if (!isApproval(raw, account, exceptionId)) throw new Error('Invalid coverage exception approval');
    return raw;
  }

  private post(account: string, exceptionId: string, suffix: string, body: unknown, session: string) {
    return firstValueFrom(this.http.get<{ requestToken: string }>('/api/admin/auth/sign-in/context').pipe(
      switchMap(({ requestToken }) => this.http.post<unknown>(this.path(account, exceptionId) + suffix, body,
        { headers: { 'X-Admin-Session': session, 'X-Admin-CSRF': requestToken } })),
    ));
  }

  private path(account: string, exceptionId: string) {
    return `/api/admin/accounts/${encodeURIComponent(account)}/coverage-replacement-exceptions/${encodeURIComponent(exceptionId)}`;
  }
}
