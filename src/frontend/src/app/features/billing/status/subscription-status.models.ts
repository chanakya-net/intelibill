import type { EntitlementSynchronization } from '@intelibill/api';
import type { CancellationSummary } from '../subscriptions/subscription-cancellation.service';

/** Mirror of the versioned licensing status contract (subscription-status-v1). No entitlement arithmetic here. */
export type AccessState =
  | 'PendingActivation' | 'Scheduled' | 'Active' | 'Grace' | 'Expired' | 'Suspended' | 'EvidenceIncomplete' | 'NeedsReview';

export interface SubscriptionCommercial {
  readonly pricePaise: number | null;
  readonly currency: string | null;
  readonly planVersionId: string | null;
}

export interface SubscriptionTermStatus {
  readonly termId: string;
  readonly state: AccessState;
  readonly evidenceState: 'Complete' | 'Incomplete' | 'NeedsReview';
  readonly basis: string | null;
  readonly billingPeriod: string | null;
  readonly timeZone: string;
  readonly startsAt: string;
  readonly paidThrough: string;
  readonly graceEndsAt: string | null;
  readonly accessEndsAt: string | null;
  readonly nextDeadline: string | null;
  readonly nextPeriodEndsAt: string | null;
  readonly coverageMode: 'Independent' | 'Grouped';
  readonly purchasedCapacity: number;
  readonly memberAllowance: number | null;
  readonly sourceReference: string | null;
  readonly developmentFixture: boolean;
  readonly commercial: SubscriptionCommercial | null;
  readonly cancellation?: CancellationSummary;
}

export interface ShopSubscriptionStatus {
  readonly schemaVersion: 1;
  readonly shopId: string;
  readonly evaluatedAt: string;
  readonly state: AccessState;
  readonly term: SubscriptionTermStatus | null;
  readonly accountId: string | null;
  readonly observeOnly: true;
  readonly entitlementSynchronization: string;
  readonly paymentVerification: string;
  readonly synchronization?: EntitlementSynchronization | null;
}

export interface AccountTermStatus {
  readonly status: SubscriptionTermStatus;
  readonly coveredShops: readonly {
    readonly shopId: string;
    readonly shopName: string;
    readonly synchronization?: EntitlementSynchronization | null;
  }[];
  readonly coveredShopCount: number;
}

export interface AccountSubscriptionStatus {
  readonly schemaVersion: 1;
  readonly accountId: string;
  readonly evaluatedAt: string;
  readonly items: readonly AccountTermStatus[];
  readonly nextCursor: string | null;
  readonly observeOnly: true;
  readonly entitlementSynchronization: string;
  readonly paymentVerification: string;
}

/** Why a status could not be shown. `unavailable` is a verification outage and never means expiry. */
export type StatusFailure = 'denied' | 'scope' | 'unavailable' | 'session';

export interface ShopTradingAuthority {
  readonly schemaVersion: 1;
  readonly shopId: string;
  readonly authorityVerifiedAt: string;
  readonly validUntil: string;
  readonly enforced: boolean;
}

// The merchant projection uses PostgreSQL microseconds. Parse the zone at whole-second
// precision and retain the fraction separately so comparisons never round a boundary.
const INSTANT = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(Z|[+-]\d{2}:\d{2})$/;
const MAX_AUTHORITY_TICKS = 48n * 60n * 60n * 10_000_000n;

function instantTicks(value: unknown): bigint | null {
  const parts = typeof value === 'string' && INSTANT.exec(value);
  if (!parts) return null;
  const millis = Date.parse(parts[1] + parts[3]);
  if (!Number.isFinite(millis)) return null;
  return BigInt(millis) * 10_000n + BigInt((parts[2] ?? '').padEnd(7, '0'));
}

/** A complete v1 grant for this shop. Anything else, including an empty success, proves no authority. */
export function isShopTradingAuthority(value: unknown, shopId: string): value is ShopTradingAuthority {
  if (typeof value !== 'object' || value === null) return false;
  const body = value as { readonly [K in keyof ShopTradingAuthority]?: unknown };
  if (body.schemaVersion !== 1 || body.shopId !== shopId || typeof body.enforced !== 'boolean') return false;
  const start = instantTicks(body.authorityVerifiedAt);
  const end = instantTicks(body.validUntil);
  return start !== null && end !== null && end > start && end - start <= MAX_AUTHORITY_TICKS;
}

export type TradingAuthorityFailure = StatusFailure | 'expired' | 'restricted' | 'pending' | 'busy';

export const BILLING_TIME_ZONE = 'Asia/Kolkata';

/**
 * An absolute instant in the billing zone, independent of the browser's own time zone, on a 24-hour clock so the
 * text never depends on a runtime's day-period spelling: "28 Feb 2027, 10:00 IST".
 */
export function formatBillingInstant(value: string | null, locale: string, exact = false): string | null {
  if (!value) return null;
  const parts = new Intl.DateTimeFormat(locale, {
    timeZone: BILLING_TIME_ZONE, day: 'numeric', month: 'short', year: 'numeric',
    hour: '2-digit', minute: '2-digit', second: exact ? '2-digit' : undefined, hourCycle: 'h23',
  }).formatToParts(new Date(value));
  const part = (type: Intl.DateTimeFormatPartTypes) => parts.find(item => item.type === type)?.value ?? '';
  const fraction = exact ? value.match(/T\d{2}:\d{2}:\d{2}(\.\d+)/)?.[1] ?? '' : '';
  const seconds = exact ? `:${part('second')}${fraction}` : '';
  return `${part('day')} ${part('month')} ${part('year')}, ${part('hour')}:${part('minute')}${seconds} IST`;
}
