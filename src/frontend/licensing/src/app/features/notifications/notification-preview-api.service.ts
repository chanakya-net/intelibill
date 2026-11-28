import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { formatIstExact } from '../accounts/account-status-api.service';

export const notificationReaders = ['support', 'billing', 'licensing-manager'] as const;

export interface ProviderNotificationCommercial {
  readonly evidencedTermPricePaise: number | null;
  readonly evidencedTermCurrency: string | null;
  readonly renewalQuoteRequired: boolean;
  readonly destinationHint: string | null;
  readonly recipientReference: string | null;
  readonly coveredShopNames: readonly string[];
}

export interface ProviderNotificationItem {
  readonly jobId: string;
  readonly scopeKind: string;
  readonly scopeId: string;
  readonly audience: string;
  readonly purpose: string;
  readonly sourceKind: string;
  readonly sourceId: string;
  readonly sourceRevision: string;
  readonly threshold: string;
  readonly status: string;
  readonly isDue: boolean;
  readonly dueAt: string;
  readonly effectiveDeadlineAt: string;
  readonly timeZone: string;
  readonly locale: string;
  readonly allowedChannels: readonly string[];
  readonly templateKey: string;
  readonly guidanceCode: string;
  readonly coveredShopCount: number;
  readonly paymentDoesNotLiftSuspension: boolean;
  readonly plannedAt: string;
  readonly commercial: ProviderNotificationCommercial | null;
}

export interface ProviderNotificationsPage {
  readonly schemaVersion: 1;
  readonly accountId: string;
  readonly evaluatedAt: string;
  readonly commercialIncluded: boolean;
  readonly items: readonly ProviderNotificationItem[];
  readonly nextCursor: string | null;
}

/**
 * Read-only provider notification preview API. Never writes or claims delivery; the server
 * enforces live MFA-enrolled identity, active session, and persisted capability redaction.
 */
@Injectable({ providedIn: 'root' })
export class NotificationPreviewApi {
  private readonly http = inject(HttpClient);

  read(accountId: string, after?: string | null): Observable<ProviderNotificationsPage> {
    const params: Record<string, string> = {};
    if (after) {
      params['after'] = after;
    }

    return this.http.get<ProviderNotificationsPage>(
      `/api/admin/accounts/${encodeURIComponent(accountId)}/notifications`,
      { params },
    );
  }
}

export function formatUtcExact(value: string | null): string | null {
  if (!value) {
    return null;
  }

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return null;
  }

  const parts = new Intl.DateTimeFormat('en-GB', {
    timeZone: 'UTC',
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hourCycle: 'h23',
  }).formatToParts(parsed);
  const part = (type: Intl.DateTimeFormatPartTypes) => parts.find((item) => item.type === type)?.value ?? '';
  return `${part('day')} ${part('month')} ${part('year')}, ${part('hour')}:${part('minute')}:${part('second')} UTC`;
}

export function formatIstNoticeInstant(value: string | null): string | null {
  return formatIstExact(value);
}
