import type { InvoiceLeaseSnapshot } from '../../../core/storage/invoice-lease-indexeddb.service';
import { isShopTradingAuthority } from '../../billing/status/subscription-status.models';
import { parseInstantNanos, type OfflineLicensePermitPayload } from './offline-licence-permit';

const DAY = 86_400_000n * 1_000_000n;
const FRESHNESS = 2n * DAY;

type HeldWindow = Pick<OfflineLicensePermitPayload, 'authorityVerifiedAt' | 'validUntil' | 'limitingReason' | 'tillSessionId'>;

interface RefreshBounds {
  needsLease: boolean;
  tillSessionId: string | null;
  authDeadline?: string;
}

function fiscalEnd(now: number): bigint {
  // The merchant fiscal year ends at midnight on April 1 in India.
  const year = new Date(now).getUTCFullYear();
  const boundary = (y: number) => Date.UTC(y, 2, 31, 18, 30);
  return BigInt(boundary(now < boundary(year) ? year : year + 1)) * 1_000_000n;
}

/** The preview schedules work. Only the completed, signed candidate changes issuing authority. */
export function decidePermitRefresh(held: HeldWindow, preview: unknown, shopId: string, now: number,
  bounds: RefreshBounds): 'reserve' | 'healthy' | 'unverified' {
  if (!isShopTradingAuthority(preview, shopId)) return 'unverified';
  const verified = parseInstantNanos(preview.authorityVerifiedAt);
  const end = parseInstantNanos(preview.validUntil);
  if (verified === null || end === null) return 'unverified';
  const heldEnd = parseInstantNanos(held.validUntil)!;
  const heldVerified = parseInstantNanos(held.authorityVerifiedAt)!;
  const instant = BigInt(now) * 1_000_000n;
  const caps = [end, instant + FRESHNESS, fiscalEnd(now)];
  if (bounds.authDeadline !== undefined) caps.push(parseInstantNanos(bounds.authDeadline) ?? instant);
  if (held.limitingReason === 'TillDayEnd' && held.tillSessionId === bounds.tillSessionId) caps.push(heldEnd);
  const achievable = caps.reduce((a, b) => a < b ? a : b);
  if (verified > instant || verified < heldVerified || achievable <= instant) return 'unverified';
  if (bounds.needsLease) return verified > heldVerified ? 'reserve' : 'unverified';
  if (end < heldEnd) return 'unverified';
  if (verified === heldVerified || achievable <= heldEnd) return 'healthy';
  const commercial = held.limitingReason === 'LicenceEnd' || held.limitingReason === 'CurrentLicenceEnd' ||
    end < verified + FRESHNESS;
  return commercial || achievable - heldEnd >= DAY ? 'reserve' : 'healthy';
}

export function isNewReservation(previous: InvoiceLeaseSnapshot, next: InvoiceLeaseSnapshot): boolean {
  const reserved = parseInstantNanos(next.reservedAt);
  const earlier = parseInstantNanos(previous.reservedAt);
  return next.shopId === previous.shopId && next.deviceId === previous.deviceId && !!next.leaseId &&
    !!next.fiscalYear && !!next.prefix && !!next.sellerProfile?.seller && Number.isSafeInteger(next.numberPadding) &&
    next.numberPadding > 0 && next.leaseId !== previous.leaseId && reserved !== null && earlier !== null &&
    reserved > earlier && Number.isSafeInteger(next.rangeStart) && next.rangeStart > 0 &&
    Number.isSafeInteger(next.rangeEnd) && next.nextNumber === next.rangeStart &&
    next.remainingCount === next.rangeEnd - next.rangeStart + 1 && next.remainingCount > 0 && next.remainingCount <= 1000 &&
    (next.prefix !== previous.prefix || next.rangeStart > previous.rangeEnd);
}

export function isFreshCandidate(held: OfflineLicensePermitPayload, next: OfflineLicensePermitPayload, previewAt: string): boolean {
  const end = parseInstantNanos(next.validUntil)!;
  return [next.authorityVerifiedAt, next.deviceVerifiedAt, next.snapshotCompletedAt].every(
    observation => end <= parseInstantNanos(observation)! + FRESHNESS) &&
    next.currentShopRevision >= next.shopRevision && next.currentAccountRevision >= next.accountRevision &&
    next.authorityVerifiedAt > held.authorityVerifiedAt &&
    parseInstantNanos(next.authorityVerifiedAt)! >= parseInstantNanos(previewAt)! &&
    next.issuedAt > held.issuedAt && next.deviceVerifiedAt >= held.deviceVerifiedAt &&
    next.authorityVerifiedAt <= next.issuedAt && next.deviceVerifiedAt <= next.issuedAt &&
    next.snapshotCompletedAt >= held.snapshotCompletedAt &&
    (next.accountId !== held.accountId || next.accountRevision >= held.currentAccountRevision) &&
    next.shopRevision >= held.currentShopRevision && next.permitId !== held.permitId &&
    next.grantId !== held.grantId && next.snapshotId !== held.snapshotId;
}
