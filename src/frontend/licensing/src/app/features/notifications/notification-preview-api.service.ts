import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';

export const notificationReaders = ['support', 'billing', 'licensing-manager'] as const;

export interface ProviderNotificationCommercial {
  readonly evidencedTermPricePaise: number | null;
  readonly evidencedTermCurrency: string | null;
  readonly renewalQuoteRequired: boolean;
  readonly destinationHint: string | null;
  readonly recipientReference: string | null;
  readonly coveredShopNames: readonly string[];
  readonly coveredShopNamesTruncated?: boolean;
}

export const suppressionReasons = [
  'ScopeClosed', 'SourceNoLongerCurrent', 'WindowElapsed', 'RestrictionChanged', 'RecipientIneligible',
] as const;

export interface ProviderEmailDelivery {
  readonly state: string;
  readonly attempts: number;
  readonly lastAttemptAt: string | null;
  readonly nextAttemptAt: string | null;
  readonly outcomeAt: string | null;
  readonly suppressionReasonCode?: string | null;
  readonly captureUrl: string | null;
}

export interface ProviderNotificationItem {
  readonly emailDelivery?: ProviderEmailDelivery | null;
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
  readonly suspendedShopCount?: number;
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

function formatZoneExact(
  value: string | null,
  timeZone: 'UTC' | 'Asia/Kolkata',
  zoneLabel: 'UTC' | 'IST',
): string | null {
  if (!value) {
    return null;
  }

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return null;
  }

  const parts = new Intl.DateTimeFormat('en-GB', {
    timeZone,
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hourCycle: 'h23',
  }).formatToParts(parsed);
  const part = (type: Intl.DateTimeFormatPartTypes) => parts.find((item) => item.type === type)?.value ?? '';
  // Preserve sub-second microsecond precision from the server ISO string (neither UTC nor IST has DST).
  const fraction = /T\d{2}:\d{2}:\d{2}\.(\d+)/.exec(value)?.[1]?.replace(/0+$/, '') ?? '';
  const suffix = fraction ? `.${fraction}` : '';
  return `${part('day')} ${part('month')} ${part('year')}, ${part('hour')}:${part('minute')}:${part('second')}${suffix} ${zoneLabel}`;
}

export function formatUtcExact(value: string | null): string | null {
  return formatZoneExact(value, 'UTC', 'UTC');
}

export function formatIstNoticeInstant(value: string | null): string | null {
  return formatZoneExact(value, 'Asia/Kolkata', 'IST');
}
