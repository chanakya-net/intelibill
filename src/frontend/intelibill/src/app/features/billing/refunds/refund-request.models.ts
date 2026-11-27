import { validRefundOutcome, type RefundOutcome } from '@intelibill/api';
import { isUuid } from '../checkout/checkout.models';

export type RefundRequestStatus = 'Requested' | 'Pending' | 'Confirmed' | 'Failed' | 'Unknown';

export const REFUND_STATUSES: readonly RefundRequestStatus[] = [
  'Requested', 'Pending', 'Confirmed', 'Failed', 'Unknown',
];

export const REFUND_MAX_REASON_LENGTH = 500;
export const REFUND_PAGE_SIZE = 25;
export const REFUND_MAX_PAISE = 1_000_000_000_000;

export interface CreateRefundRequest {
  readonly operationKey: string;
  readonly orderId: string;
  readonly confirmationId: string;
  readonly amountPaise: number;
  readonly reason: string;
}

export interface RefundRequestItem extends RefundOutcome {
  readonly schemaVersion: 1;
  readonly refundRequestId: string;
  readonly accountId: string;
  readonly orderId: string;
  readonly confirmationId: string;
  readonly status: RefundRequestStatus;
  readonly amountPaise: number;
  readonly paymentAmountPaise: number;
  readonly currency: 'INR';
  readonly reason: string;
  readonly requestedAt: string;
}

export interface RefundRequestPage {
  readonly schemaVersion: 1;
  readonly accountId: string;
  readonly items: readonly RefundRequestItem[];
  readonly nextCursor: string | null;
}

export const REFUND_STATUS_COPY: Readonly<Record<RefundRequestStatus, { readonly label: string; readonly detail: string }>> = {
  Requested: {
    label: 'billing.refunds.statuses.Requested',
    detail: 'billing.refunds.statusDetails.Requested',
  },
  Pending: {
    label: 'billing.refunds.statuses.Pending',
    detail: 'billing.refunds.statusDetails.Pending',
  },
  Confirmed: {
    label: 'billing.refunds.statuses.Confirmed',
    detail: 'billing.refunds.statusDetails.Confirmed',
  },
  Failed: {
    label: 'billing.refunds.statuses.Failed',
    detail: 'billing.refunds.statusDetails.Failed',
  },
  Unknown: {
    label: 'billing.refunds.statuses.Unknown',
    detail: 'billing.refunds.statusDetails.Unknown',
  },
};

export function refundStatusCopy(
  item: Pick<RefundRequestItem, 'status' | 'providerState'>,
): { readonly label: string; readonly detail: string } {
  switch (item.providerState) {
    case 'Reserved':
      return { label: 'billing.refunds.providerStatuses.Reserved', detail: 'billing.refunds.Reserved' };
    case 'Manual':
      return { label: 'billing.refunds.providerStatuses.Manual', detail: 'billing.refunds.Manual' };
    case 'Historical':
      return { label: 'billing.refunds.providerStatuses.Historical', detail: 'billing.refunds.Historical' };
    case 'Pending':
      return REFUND_STATUS_COPY.Pending;
    default:
      return REFUND_STATUS_COPY[item.status];
  }
}

// Refund request V1 trims with .NET string.Trim() (char.IsWhiteSpace). String.prototype.trim() differs: it also
// removes U+FEFF, which V1 records, and keeps U+0085, which V1 removes.
const V1_EDGE_SPACE = '\\t-\\r \\u0085\\u00a0\\u1680\\u2000-\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000';
const v1EdgeSpace = new RegExp(`^[${V1_EDGE_SPACE}]+|[${V1_EDGE_SPACE}]+$`, 'g');

/** Normalizes a reason exactly as the V1 server contract does; null when the server would refuse it. */
export function normalizeRefundReason(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const trimmed = value.replace(v1EdgeSpace, '');
  if (trimmed.length === 0 || trimmed.length > REFUND_MAX_REASON_LENGTH) return null;
  for (let i = 0; i < trimmed.length; i++) {
    const code = trimmed.charCodeAt(i);
    if (code < 0x20 || (code >= 0x7f && code <= 0x9f)) return null;
  }
  return trimmed;
}

/** Parses a whole-rupee or up-to-2-decimal INR string into exact integer paise without floating-point rounding. */
export function parseInrToPaise(input: string): number | null {
  const trimmed = input.trim();
  const match = /^(\d+)(?:\.(\d{1,2}))?$/.exec(trimmed);
  if (!match) return null;
  const rupees = Number(match[1]);
  const paisePart = match[2] ? Number(match[2].padEnd(2, '0')) : 0;
  const total = rupees * 100 + paisePart;
  return Number.isSafeInteger(total) && total > 0 && total <= REFUND_MAX_PAISE ? total : null;
}

export function formatPaiseAsInput(paise: number): string {
  const whole = Math.floor(paise / 100);
  const fraction = String(paise % 100).padStart(2, '0');
  return `${whole}.${fraction}`;
}

export function isValidRefundRequest(value: unknown, account: string): value is RefundRequestItem {
  if (!value || typeof value !== 'object') return false;
  const r = value as Record<string, unknown>;
  return r['schemaVersion'] === 1 &&
    typeof r['refundRequestId'] === 'string' && isUuid(r['refundRequestId']) &&
    r['accountId'] === account &&
    typeof r['orderId'] === 'string' && isUuid(r['orderId']) &&
    typeof r['confirmationId'] === 'string' && isUuid(r['confirmationId']) &&
    typeof r['status'] === 'string' && (REFUND_STATUSES as readonly string[]).includes(r['status']) &&
    typeof r['paymentAmountPaise'] === 'number' && Number.isSafeInteger(r['paymentAmountPaise']) &&
    r['paymentAmountPaise'] > 0 && r['paymentAmountPaise'] <= REFUND_MAX_PAISE &&
    typeof r['amountPaise'] === 'number' && Number.isSafeInteger(r['amountPaise']) &&
    r['amountPaise'] > 0 && r['amountPaise'] <= (r['paymentAmountPaise'] as number) &&
    validRefundOutcome(r) && r['currency'] === 'INR' &&
    typeof r['reason'] === 'string' && normalizeRefundReason(r['reason']) === r['reason'] &&
    typeof r['requestedAt'] === 'string' && Number.isFinite(Date.parse(r['requestedAt']));
}

export function validateRefundPage(value: unknown, account: string): RefundRequestPage {
  if (!value || typeof value !== 'object') throw new Error('Invalid refund request page');
  const page = value as Record<string, unknown>;
  const items = page['items'];
  const cursor = page['nextCursor'];
  if (page['schemaVersion'] !== 1 || page['accountId'] !== account || !Array.isArray(items) ||
    items.length > REFUND_PAGE_SIZE || !items.every(item => isValidRefundRequest(item, account)) ||
    (cursor !== null && (typeof cursor !== 'string' || !isUuid(cursor))))
    throw new Error('Invalid refund request page');
  return {
    schemaVersion: 1,
    accountId: account,
    items: items as RefundRequestItem[],
    nextCursor: cursor as string | null,
  };
}

export function matchesRefundCommand(item: RefundRequestItem, account: string, command: CreateRefundRequest): boolean {
  return item.accountId === account && item.orderId === command.orderId &&
    item.confirmationId === command.confirmationId && item.amountPaise === command.amountPaise &&
    item.reason === normalizeRefundReason(command.reason);
}

export function refundCommandsMatch(a: CreateRefundRequest, b: CreateRefundRequest): boolean {
  return a.orderId === b.orderId && a.confirmationId === b.confirmationId &&
    a.amountPaise === b.amountPaise && a.reason === b.reason;
}

const errorCodeMap: Readonly<Record<string, string>> = {
  'Billing.RefundRequestInvalid': 'invalid',
  'Billing.RefundAmountInvalid': 'amountInvalid',
  'Billing.RefundPaymentNotFound': 'notFound',
  'Billing.OperationPayloadMismatch': 'mismatch',
};

export type RefundRefusalKind = 'definitive' | 'soleDelivery' | 'inconclusive';

/**
 * Says what a failed create proves about its operation key.
 * definitive: licensing judged this exact payload, so no delivery of the key can have committed.
 * soleDelivery: refused before replay (403, uncoded 400); proves nothing committed only when no other delivery
 * of the key was ever started, which the durable draft decides.
 * inconclusive: route-level 404s, transport and server failures keep the saved key.
 */
export function refundRefusalKind(status: number | null, code: string | null): RefundRefusalKind {
  if (status === 400) {
    return code === 'Billing.RefundRequestInvalid' || code === 'Billing.RefundAmountInvalid'
      ? 'definitive' : 'soleDelivery';
  }
  if (status === 404 && code === 'Billing.RefundPaymentNotFound') return 'definitive';
  if (status === 409 && code === 'Billing.OperationPayloadMismatch') return 'definitive';
  return status === 403 ? 'soleDelivery' : 'inconclusive';
}

export function mapRefundErrorKey(status: number | null, code: string | null, hasPending: boolean): string {
  const mapped = code ? errorCodeMap[code] : undefined;
  if (mapped) return 'billing.refunds.errors.' + mapped;
  if (status === 403) return 'billing.refunds.errors.denied';
  return hasPending ? 'billing.refunds.errors.unknown' : 'billing.refunds.errors.unavailable';
}
