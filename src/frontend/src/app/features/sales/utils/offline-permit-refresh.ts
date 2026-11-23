import type { InvoiceLeaseSnapshot } from '../../../core/storage/invoice-lease-indexeddb.service';
import { isShopTradingAuthority } from '../../billing/status/subscription-status.models';
import { parseInstantNanos, type OfflineLicensePermitPayload } from './offline-licence-permit';

type HeldWindow = Pick<OfflineLicensePermitPayload, 'authorityVerifiedAt' | 'validUntil'>;

/** The unsigned read can justify requesting a grant. Only the signed candidate can authorize a sale. */
export function shouldReserveForRefresh(held: HeldWindow, preview: unknown, shopId: string, now: number, needsRange = false): boolean {
  if (!isShopTradingAuthority(preview, shopId)) return false;
  const verified = parseInstantNanos(preview.authorityVerifiedAt);
  const end = parseInstantNanos(preview.validUntil);
  // The preview has no capacity revision. An equal horizon can still justify new signed authority.
  // A shorter advertised window alone cannot improve an otherwise usable range.
  return verified !== null && end !== null && (needsRange || end >= parseInstantNanos(held.validUntil)!) &&
    verified > parseInstantNanos(held.authorityVerifiedAt)! &&
    verified <= BigInt(now) * 1_000_000n && end > BigInt(now) * 1_000_000n;
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
  const freshness = 48n * 60n * 60n * 1_000_000_000n;
  const end = parseInstantNanos(next.validUntil)!;
  return [next.authorityVerifiedAt, next.deviceVerifiedAt, next.snapshotCompletedAt].every(
    observation => end <= parseInstantNanos(observation)! + freshness) &&
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
