import {
  isExpiryPolicy,
  type ExpiryBatchFacts,
  type ExpiryPolicy,
  type SellabilityDecision,
} from './expiry.models';

/** A date-only value in the server-provided shop zone. No browser-zone or UTC-day fallback. */
export function shopBusinessDate(instant: string | number | Date, timeZone: string): string {
  if (!timeZone) throw new RangeError('Shop business time zone is required.');
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(new Date(instant));
  const part = (type: string) => parts.find((p) => p.type === type)!.value;
  return `${part('year')}-${part('month')}-${part('day')}`;
}
export function evaluateExpiry(
  batch: ExpiryBatchFacts,
  policy: ExpiryPolicy,
  catalogVersion: number,
  businessDate: string,
): SellabilityDecision {
  const available = Math.round((batch.physicalQuantity - batch.quarantinedQuantity) * 1000) / 1000;
  const result = (
    allowed: boolean,
    reasonCode: string | null = null,
    warning = false,
    requiresApproval = false,
  ): SellabilityDecision => ({
    allowed,
    reasonCode,
    warning,
    requiresApproval,
    policyVersion: policy.version,
    catalogVersion,
    businessDate,
    physicalQuantity: batch.physicalQuantity,
    quarantinedQuantity: batch.quarantinedQuantity,
    availableQuantity: allowed ? available : 0,
  });
  if (
    !isExpiryPolicy(policy) ||
    !Number.isSafeInteger(catalogVersion) ||
    catalogVersion < policy.version
  )
    return result(false, 'Expiry.PolicyInvalid');
  if (!batch.itemActive) return result(false, 'Item.Inactive');
  if (batch.isVoided) return result(false, 'InventoryBatch.Voided');
  if (
    !Number.isFinite(available) ||
    batch.quarantinedQuantity < 0 ||
    batch.physicalQuantity < 0 ||
    available <= 0
  )
    return result(false, 'InventoryBatch.InsufficientStock');
  if (!batch.expiryDate)
    return policy.expiryRequired ? result(false, 'Expiry.RequiredDateMissing') : result(true);
  if (policy.mode === 'Ignore') return result(true);
  const expired = policy.sellThroughExpiryDate
    ? businessDate > batch.expiryDate
    : businessDate >= batch.expiryDate;
  if (expired) {
    if (policy.mode === 'Block') return result(false, 'Expiry.Expired');
    return policy.overrideAllowed
      ? result(true, 'Expiry.Expired', true, true)
      : result(false, 'Expiry.OverrideDisabled');
  }
  const days =
    (Date.parse(batch.expiryDate + 'T00:00:00Z') - Date.parse(businessDate + 'T00:00:00Z')) /
    86400000;
  return days <= policy.warningDays ? result(true, 'Expiry.ExpiringSoon', true) : result(true);
}
export function expiryStatusKey(decision?: SellabilityDecision | null): string {
  if (!decision) return 'expiry.status.refresh';
  if (decision.reasonCode === 'Expiry.OfflineRefreshRequired') return 'expiry.offline.refresh';
  if (decision.requiresApproval) return 'expiry.status.approval';
  const keys: Record<string, string> = {
    'Expiry.RequiredDateMissing': 'required',
    'Expiry.Expired': 'expired',
    'Expiry.OverrideDisabled': 'expired',
    'Expiry.ExpiringSoon': 'soon',
    'InventoryBatch.InsufficientStock': 'unavailable',
    'InventoryBatch.Voided': 'voided',
    'Item.Inactive': 'inactive',
  };
  return `expiry.status.${decision.reasonCode ? (keys[decision.reasonCode] ?? 'refresh') : 'available'}`;
}
/** Finds the next actual shop date boundary, including DST days, without assuming a UTC offset. */
export function millisecondsUntilShopDateChange(now: number, timeZone: string): number {
  const today = shopBusinessDate(now, timeZone);
  let low = now,
    high = now + 48 * 60 * 60 * 1000;
  while (high - low > 1) {
    const middle = Math.floor((low + high) / 2);
    if (shopBusinessDate(middle, timeZone) === today) low = middle;
    else high = middle;
  }
  return high - now;
}
