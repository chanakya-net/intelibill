import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, switchMap } from 'rxjs';

export type RefundTreatment = 'Preserve' | 'EndAt' | 'TermAdjustment';
export const REFUND_TREATMENTS: readonly RefundTreatment[] = ['Preserve', 'EndAt', 'TermAdjustment'];

export interface RefundApprovalScope {
  readonly account: string;
  readonly request: string;
  readonly session: string;
}

export interface RefundPreviewInput {
  readonly amountPaise: number;
  readonly treatment: RefundTreatment;
  readonly effectiveAt: string | null;
}

/** The frozen approval command. It is persisted before it is sent and never rebuilt for a retry. */
export interface ApproveRefundCommand extends RefundPreviewInput {
  readonly operationKey: string;
  readonly reason: string;
  readonly expectedVersion: number;
  readonly impactDigest: string;
  readonly confirmed: true;
}

export interface RefundAccessImpact {
  readonly treatment: RefundTreatment;
  readonly termId: string | null;
  readonly shops: readonly { readonly shopId: string; readonly shopName: string; readonly restricted: boolean }[];
  readonly effectiveAt: string | null;
  readonly previousPaidThrough: string | null;
  readonly previousAccessEndsAt: string | null;
  readonly newPaidThrough: string | null;
  readonly newAccessEndsAt: string | null;
}

export interface RefundApprovalPreview {
  readonly refundRequestId: string;
  readonly confirmationId: string;
  readonly verifiedAmountPaise: number;
  readonly requestedAmountPaise: number;
  readonly occupiedAmountPaise: number;
  readonly remainingAmountPaise: number;
  readonly proposedAmountPaise: number;
  readonly proposedRemainingAmountPaise: number;
  readonly paymentSource: string;
  readonly providerId: string;
  readonly providerAccountId: string;
  readonly environment: string;
  readonly configurationVersion: string;
  readonly refundPolicyVersion: string;
  readonly receiptVersion: number;
  readonly impactDigest: string;
  readonly access: RefundAccessImpact;
}

export interface RefundApprovalReceipt {
  readonly approvalId: string;
  readonly reservationId: string;
  readonly accessDecisionId: string;
  readonly refundRequestId: string;
  readonly operationKey: string;
  readonly status: 'Pending';
  readonly approvedAmountPaise: number;
  readonly requestedAmountPaise: number;
  readonly verifiedAmountPaise: number;
  readonly remainingAfterPaise: number;
  readonly receiptVersion: number;
  readonly adminId: string;
  readonly decidedAt: string;
  readonly reason: string;
  readonly access: RefundAccessImpact;
}

const uuid = /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i;
const digest = /^[a-f0-9]{64}$/;
const paise = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
const instant = (value: unknown) => value === null || (typeof value === 'string' && Number.isFinite(Date.parse(value)));

function isAccess(value: unknown): value is RefundAccessImpact {
  if (!value || typeof value !== 'object') return false;
  const r = value as Record<string, unknown>;
  return (REFUND_TREATMENTS as readonly unknown[]).includes(r['treatment']) &&
    (r['termId'] === null || (typeof r['termId'] === 'string' && uuid.test(r['termId']))) &&
    Array.isArray(r['shops']) && r['shops'].every(shop => shop && typeof shop.shopId === 'string' && uuid.test(shop.shopId)
      && typeof shop.shopName === 'string' && typeof shop.restricted === 'boolean') &&
    ['effectiveAt', 'previousPaidThrough', 'previousAccessEndsAt', 'newPaidThrough', 'newAccessEndsAt'].every(key => instant(r[key])) &&
    (r['treatment'] === 'Preserve') === (r['effectiveAt'] === null);
}

function isPreview(value: unknown, scope: RefundApprovalScope, input: RefundPreviewInput): value is RefundApprovalPreview {
  if (!value || typeof value !== 'object') return false;
  const r = value as Record<string, unknown>;
  return r['schemaVersion'] === 1 && r['accountId'] === scope.account && r['refundRequestId'] === scope.request &&
    ['verifiedAmountPaise', 'requestedAmountPaise', 'occupiedAmountPaise', 'remainingAmountPaise', 'proposedAmountPaise',
      'proposedRemainingAmountPaise', 'receiptVersion'].every(key => paise(r[key])) &&
    r['proposedAmountPaise'] === input.amountPaise &&
    r['remainingAmountPaise'] === (r['verifiedAmountPaise'] as number) - (r['occupiedAmountPaise'] as number) &&
    r['proposedRemainingAmountPaise'] === (r['remainingAmountPaise'] as number) - input.amountPaise &&
    typeof r['impactDigest'] === 'string' && digest.test(r['impactDigest']) && r['currency'] === 'INR' &&
    ['providerId', 'providerAccountId', 'environment', 'configurationVersion', 'refundPolicyVersion', 'paymentSource']
      .every(key => typeof r[key] === 'string' && (r[key] as string).length > 0) &&
    isAccess(r['access']) && r['access'].treatment === input.treatment;
}

function isReceipt(value: unknown, scope: RefundApprovalScope): value is RefundApprovalReceipt {
  if (!value || typeof value !== 'object') return false;
  const r = value as Record<string, unknown>;
  return r['schemaVersion'] === 1 && r['accountId'] === scope.account && r['refundRequestId'] === scope.request &&
    r['status'] === 'Pending' && r['currency'] === 'INR' &&
    ['approvalId', 'reservationId', 'accessDecisionId', 'operationKey', 'adminId']
      .every(key => typeof r[key] === 'string' && uuid.test(r[key] as string)) &&
    ['approvedAmountPaise', 'requestedAmountPaise', 'verifiedAmountPaise', 'remainingAfterPaise', 'receiptVersion']
      .every(key => paise(r[key])) &&
    typeof r['decidedAt'] === 'string' && instant(r['decidedAt']) && typeof r['reason'] === 'string' && isAccess(r['access']);
}

@Injectable({ providedIn: 'root' })
export class RefundApprovalApi {
  private readonly http = inject(HttpClient);

  async preview(scope: RefundApprovalScope, input: RefundPreviewInput): Promise<RefundApprovalPreview> {
    const raw = await this.post(scope, '/approval-preview', input);
    if (!isPreview(raw, scope, input)) throw new Error('Invalid refund approval preview');
    return raw;
  }

  async approve(scope: RefundApprovalScope, command: ApproveRefundCommand): Promise<RefundApprovalReceipt> {
    return this.receipt(scope, await this.post(scope, '/approvals', command));
  }

  async recover(scope: RefundApprovalScope, operationKey: string): Promise<RefundApprovalReceipt> {
    return this.receipt(scope, await firstValueFrom(this.http.get<unknown>(
      `${this.path(scope)}/approvals/operations/${encodeURIComponent(operationKey)}`,
      { headers: { 'X-Admin-Session': scope.session } })));
  }

  private receipt(scope: RefundApprovalScope, raw: unknown): RefundApprovalReceipt {
    if (!isReceipt(raw, scope)) throw new Error('Invalid refund approval receipt');
    return raw;
  }

  private post(scope: RefundApprovalScope, suffix: string, body: unknown) {
    return firstValueFrom(this.http.get<{ requestToken: string }>('/api/admin/auth/sign-in/context').pipe(
      switchMap(({ requestToken }) => this.http.post<unknown>(this.path(scope) + suffix, body,
        { headers: { 'X-Admin-Session': scope.session, 'X-Admin-CSRF': requestToken } })),
    ));
  }

  private path(scope: RefundApprovalScope) {
    return `/api/admin/accounts/${encodeURIComponent(scope.account)}/refund-requests/${encodeURIComponent(scope.request)}`;
  }
}
