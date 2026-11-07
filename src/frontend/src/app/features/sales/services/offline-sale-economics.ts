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

/** Issuance policy only; the frozen v1 invoice and its allocation remain unchanged. */
export function evaluateOfflineSaleEconomics(pricing: OfflineFrozenSalePricing): {
  lines: readonly OfflineLineEconomics[]; belowCostLineIds: readonly string[];
} {
  const belowCostLineIds: string[] = [];
  const lines = pricing.lines.map((line): OfflineLineEconomics => {
    const revenue = toScaled(line.taxableAmount, MONEY_SCALE);
    const cost = line.lineType === 'service' ? 0n : roundDiv(toScaled(line.quantity, QUANTITY_SCALE) * toScaled(line.costPrice, MONEY_SCALE), 1000n);
    const profit = revenue - cost;
    if (line.lineType !== 'service' && profit < 0n) belowCostLineIds.push(line.clientLineId);
    return { clientLineId: line.clientLineId, netRevenue: fromScaled(revenue, MONEY_SCALE), economicCost: fromScaled(cost, MONEY_SCALE),
      grossProfit: fromScaled(profit, MONEY_SCALE),
      grossMarginPercent: revenue === 0n ? null : fromScaled(roundDiv(profit * 10000n, revenue), 2),
      markupPercent: cost === 0n ? null : fromScaled(roundDiv(profit * 10000n, cost), 2) };
  });
  return { lines, belowCostLineIds };
}
