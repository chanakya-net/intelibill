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

export interface RefundIntakeItem {
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
    r['currency'] === 'INR' &&
    typeof r['reason'] === 'string' && r['reason'].trim().length > 0 &&
    typeof r['requestedAt'] === 'string' && Number.isFinite(Date.parse(r['requestedAt'])) &&
    typeof r['providerId'] === 'string' && r['providerId'].length > 0 &&
    typeof r['providerAccountId'] === 'string' && r['providerAccountId'].length > 0 &&
    typeof r['environment'] === 'string' && r['environment'].length > 0 &&
    typeof r['configurationVersion'] === 'string' && r['configurationVersion'].length > 0;
}

@Injectable({ providedIn: 'root' })
export class RefundIntakeApi {
  private readonly http = inject(HttpClient);

  async list(account: string, session: string, after?: string): Promise<RefundIntakePage> {
    const raw = await firstValueFrom(this.http.get<unknown>(
      `/api/admin/accounts/${encodeURIComponent(account)}/refund-requests`,
      { headers: { 'X-Admin-Session': session }, params: after ? { after } : {} },
    ));
    if (!raw || typeof raw !== 'object') throw new Error('Invalid refund intake response');
    const page = raw as Record<string, unknown>;
    const items = page['items'];
    const cursor = page['nextCursor'];
    if (page['schemaVersion'] !== 1 || page['accountId'] !== account || !Array.isArray(items) ||
      items.length > 25 || !items.every(item => isValidItem(item, account)) ||
      (cursor !== null && (typeof cursor !== 'string' || !uuid.test(cursor))))
      throw new Error('Invalid refund intake page');
    return {
      schemaVersion: 1,
      accountId: account,
      items: items as RefundIntakeItem[],
      nextCursor: cursor as string | null,
    };
  }
}
