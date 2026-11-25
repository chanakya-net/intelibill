import { validRefundOutcome, type RefundOutcome } from '@intelibill/api';
import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type RefundIntakeStatus = 'Requested' | 'Pending' | 'Confirmed' | 'Failed' | 'Unknown';

export const REFUND_INTAKE_STATUSES: readonly RefundIntakeStatus[] = [
  'Requested', 'Pending', 'Confirmed', 'Failed', 'Unknown',
];

export const REFUND_INTAKE_STATUS_META: Readonly<Record<RefundIntakeStatus, { readonly label: string; readonly detail: string }>> = {
  Requested: {
    label: 'Requested',
    detail: 'Customer requested a refund. Not approved; no funds moved and no access changed.',
  },
  Pending: {
    label: 'Pending provider',
    detail: 'Approved refund awaiting confirmation from the original payment provider.',
  },
  Confirmed: {
    label: 'Refund confirmed',
    detail: 'Refund confirmed by the original payment provider.',
  },
  Failed: {
    label: 'Refund failed',
    detail: 'Refund attempt failed at the original payment provider. Review before any further action.',
  },
  Unknown: {
    label: 'Outcome unknown',
    detail: 'Provider outcome is uncertain and requires reconciliation through the original provider.',
  },
};

const RESERVED_REASON_META: Readonly<Record<string, { readonly label: string; readonly detail: string }>> = {
  BelowProviderMinimum: {
    label: 'Reserved — below provider minimum',
    detail: 'Approved amount is below the original payment provider minimum and cannot be sent automatically. Amount stays reserved for finance review.',
  },
  ProviderRefundUnsupported: {
    label: 'Reserved — provider unsupported',
    detail: 'Original payment provider does not support this refund. Amount stays reserved for finance review.',
  },
  LiveDispatchDisabled: {
    label: 'Reserved — live dispatch disabled',
    detail: 'Live provider refund dispatch is disabled in this environment. Amount stays reserved.',
  },
  OriginalAdapterUnavailable: {
    label: 'Reserved — credentials unavailable',
    detail: 'Original payment provider credentials are unavailable. Amount stays reserved until restored.',
  },
};

export function refundOutcomeMeta(
  item: Pick<RefundIntakeItem, 'status' | 'providerState' | 'observationReason'>,
): { readonly label: string; readonly detail: string } {
  switch (item.providerState) {
    case 'Reserved':
      return (item.observationReason ? RESERVED_REASON_META[item.observationReason] : undefined) ?? {
        label: 'Reserved',
        detail: 'Refund approved and amount reserved; not yet dispatched to the original payment provider.',
      };
    case 'Pending':
      return { label: 'Pending provider', detail: 'Refund sent to the original payment provider and awaiting confirmation.' };
    case 'Manual':
      return { label: 'Manual follow-up', detail: 'Manual refund approved and reserved; finance handles settlement outside online providers.' };
    case 'Historical':
      return { label: 'Historical approval', detail: 'Recorded before online provider reconciliation; money settlement requires finance verification.' };
    case 'Confirmed':
      return { label: 'Refund confirmed', detail: 'Approved refund amount confirmed by the original payment provider.' };
    case 'Failed':
      return REFUND_INTAKE_STATUS_META.Failed;
    case 'Unknown':
      return REFUND_INTAKE_STATUS_META.Unknown;
    default:
      return REFUND_INTAKE_STATUS_META[item.status];
  }
}

export function providerOutcomeLabel(state: RefundOutcome['providerState']): string {
  switch (state) {
    case 'Manual': return 'Manual follow-up';
    case 'Historical': return 'Historical approval';
    case 'Pending': return 'Pending provider';
    default: return state ?? '';
  }
}

export function approvedAmountLabel(item: Pick<RefundIntakeItem, 'status' | 'providerState'>): string {
  return item.providerState === 'Confirmed' || item.status === 'Confirmed' ? 'Confirmed amount' : 'Approved amount';
}

export function accessDispositionLabel(disposition: RefundOutcome['accessDisposition']): string {
  if (disposition === 'AwaitingConfirmation') return 'Awaiting confirmation';
  if (disposition === 'NeedsReview') return 'Finance review required';
  return 'Applied';
}

export interface RefundIntakeItem extends RefundOutcome {
  readonly refundReference?: string | null;
  readonly observationReason?: string | null;
  readonly lastCheckedAt?: string | null;
  readonly refundRequestId: string;
  readonly accountId: string;
  readonly orderId: string;
  readonly confirmationId: string;
  readonly actorId: string;
  readonly paymentSource: 'Online' | 'Manual';
  readonly status: RefundIntakeStatus;
  readonly amountPaise: number;
  readonly paymentAmountPaise: number;
  readonly currency: 'INR';
  readonly reason: string;
  readonly requestedAt: string;
  readonly providerId: string;
  readonly providerAccountId: string;
  readonly environment: string;
  readonly configurationVersion: string;
}

export interface RefundIntakePage {
  readonly schemaVersion: 1;
  readonly accountId: string;
  readonly items: readonly RefundIntakeItem[];
  readonly nextCursor: string | null;
}

const uuid = /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i;

export function canonicalAccountId(value: string): string | null {
  const trimmed = value.trim();
  return uuid.test(trimmed) ? trimmed.toLowerCase() : null;
}

// Refund request V1 trims with .NET string.Trim() (char.IsWhiteSpace). String.prototype.trim() differs: it also
// removes U+FEFF, which V1 records, and keeps U+0085, which V1 removes.
const V1_EDGE_SPACE = '\\t-\\r \\u0085\\u00a0\\u1680\\u2000-\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000';
const v1EdgeSpace = new RegExp(`^[${V1_EDGE_SPACE}]+|[${V1_EDGE_SPACE}]+$`, 'g');

/** Normalizes a reason exactly as the V1 server contract does; null when the server would never record it. */
export function normalizeRefundIntakeReason(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const trimmed = value.replace(v1EdgeSpace, '');
  if (trimmed.length === 0 || trimmed.length > 500) return null;
  for (let i = 0; i < trimmed.length; i++) {
    const code = trimmed.charCodeAt(i);
    if (code < 0x20 || (code >= 0x7f && code <= 0x9f)) return null;
  }
  return trimmed;
}

function isValidItem(value: unknown, account: string): value is RefundIntakeItem {
  if (!value || typeof value !== 'object') return false;
  const r = value as Record<string, unknown>;
  return typeof r['refundRequestId'] === 'string' && uuid.test(r['refundRequestId']) &&
    r['accountId'] === account &&
    typeof r['orderId'] === 'string' && uuid.test(r['orderId']) &&
    typeof r['confirmationId'] === 'string' && uuid.test(r['confirmationId']) &&
    typeof r['actorId'] === 'string' && uuid.test(r['actorId']) &&
    (r['paymentSource'] === 'Online' || r['paymentSource'] === 'Manual') &&
    typeof r['status'] === 'string' && (REFUND_INTAKE_STATUSES as readonly string[]).includes(r['status']) &&
    typeof r['paymentAmountPaise'] === 'number' && Number.isSafeInteger(r['paymentAmountPaise']) &&
    r['paymentAmountPaise'] > 0 &&
    typeof r['amountPaise'] === 'number' && Number.isSafeInteger(r['amountPaise']) &&
    r['amountPaise'] > 0 && r['amountPaise'] <= (r['paymentAmountPaise'] as number) &&
    (r['refundReference'] == null || typeof r['refundReference'] === 'string' && r['refundReference'].length <= 120) &&
    (r['observationReason'] == null || typeof r['observationReason'] === 'string' && r['observationReason'].length <= 80) &&
    (r['lastCheckedAt'] == null || typeof r['lastCheckedAt'] === 'string' && Number.isFinite(Date.parse(r['lastCheckedAt']))) &&
    validRefundOutcome(r) && r['currency'] === 'INR' &&
    typeof r['reason'] === 'string' && normalizeRefundIntakeReason(r['reason']) === r['reason'] &&
    typeof r['requestedAt'] === 'string' && Number.isFinite(Date.parse(r['requestedAt'])) &&
    typeof r['providerId'] === 'string' && r['providerId'].length > 0 &&
    typeof r['providerAccountId'] === 'string' && r['providerAccountId'].length > 0 &&
    typeof r['environment'] === 'string' && r['environment'].length > 0 &&
    typeof r['configurationVersion'] === 'string' && r['configurationVersion'].length > 0;
}

@Injectable({ providedIn: 'root' })
export class RefundIntakeApi {
  private readonly http = inject(HttpClient);

  async list(account: string, session: string, after?: string, request?: string): Promise<RefundIntakePage> {
    const canonical = canonicalAccountId(account);
    if (!canonical) throw new Error('Invalid billing account ID');
    const raw = await firstValueFrom(this.http.get<unknown>(
      `/api/admin/accounts/${encodeURIComponent(canonical)}/refund-requests`,
      { headers: { 'X-Admin-Session': session }, params: request ? { request } : after ? { after } : {} },
    ));
    if (!raw || typeof raw !== 'object') throw new Error('Invalid refund intake response');
    const page = raw as Record<string, unknown>;
    const items = page['items'];
    const cursor = page['nextCursor'];
    if (page['schemaVersion'] !== 1 || page['accountId'] !== canonical || !Array.isArray(items) ||
      items.length > 25 || !items.every(item => isValidItem(item, canonical)) ||
      (cursor !== null && (typeof cursor !== 'string' || !uuid.test(cursor))))
      throw new Error('Invalid refund intake page');
    return {
      schemaVersion: 1,
      accountId: canonical,
      items: items as RefundIntakeItem[],
      nextCursor: cursor as string | null,
    };
  }
}
