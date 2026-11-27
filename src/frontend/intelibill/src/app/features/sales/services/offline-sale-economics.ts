import type { OfflineFrozenSalePricing } from './offline-sale-core.types';
import { MONEY_SCALE, QUANTITY_SCALE, fromScaled, roundDiv, toScaled } from './offline-money.math';

export interface OfflineLineEconomics {
  readonly clientLineId: string;
  readonly netRevenue: number;
  readonly economicCost: number;
  readonly grossProfit: number;
  readonly grossMarginPercent: number | null;
  readonly markupPercent: number | null;
}

/** Same quantity/unit-cost money boundary as the invoice guard and decimal server. */
export function recordedCostPaise(quantity: number, unitCost: number): bigint {
  return roundDiv(toScaled(quantity, QUANTITY_SCALE) * toScaled(unitCost, MONEY_SCALE), 1000n);
}

export function discountCapacityPaise(netBeforeDiscount: number, quantity: number, unitCost: number, itemDiscount = 0): bigint {
  const capacity = toScaled(netBeforeDiscount, MONEY_SCALE) - toScaled(itemDiscount, MONEY_SCALE) - recordedCostPaise(quantity, unitCost);
  return capacity > 0n ? capacity : 0n;
}

export function discountCeilingFromPaise(capacity: bigint, basis: bigint): { maxFlat: number; maxPercent: number } {
  return { maxFlat: fromScaled(capacity, MONEY_SCALE), maxPercent: basis <= 0n ? 0 : Number(capacity * 10000n / basis) / 100 };
}

/** Issuance policy only; the frozen v1 invoice and its allocation remain unchanged. */
export function evaluateOfflineSaleEconomics(pricing: OfflineFrozenSalePricing): {
  lines: readonly OfflineLineEconomics[]; belowCostLineIds: readonly string[];
} {
  const belowCostLineIds: string[] = [];
  const lines = pricing.lines.map((line): OfflineLineEconomics => {
    const revenue = toScaled(line.taxableAmount, MONEY_SCALE);
    const cost = line.lineType === 'service' ? 0n : recordedCostPaise(line.quantity, line.costPrice);
    const profit = revenue - cost;
    if (line.lineType !== 'service' && profit < 0n) belowCostLineIds.push(line.clientLineId);
    return { clientLineId: line.clientLineId, netRevenue: fromScaled(revenue, MONEY_SCALE), economicCost: fromScaled(cost, MONEY_SCALE),
      grossProfit: fromScaled(profit, MONEY_SCALE),
      grossMarginPercent: revenue === 0n ? null : fromScaled(roundDiv(profit * 10000n, revenue), 2),
      markupPercent: cost === 0n ? null : fromScaled(roundDiv(profit * 10000n, cost), 2) };
  });
  return { lines, belowCostLineIds };
}
