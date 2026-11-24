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

export interface RefundRequestItem {
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

export function normalizeRefundReason(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const trimmed = value.trim();
  if (trimmed.length === 0 || trimmed.length > REFUND_MAX_REASON_LENGTH) return null;
  for (let i = 0; i < trimmed.length; i++) {
    const code = trimmed.charCodeAt(i);
    if (code < 0x20 || code === 0x7f) return null;
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
    r['currency'] === 'INR' &&
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
