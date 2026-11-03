import {
  type OfflineFrozenSaleLine,
  type OfflineFrozenSalePricing,
  type OfflineSalePricingInput,
  type OfflineSalePricingLineInput,
  type OfflineSalePricingRuleInput,
} from './offline-sale-core.types';
import {
  MONEY_SCALE,
  OFFLINE_CALCULATION_VERSION,
  PERCENT_SCALE,
  QUANTITY_SCALE,
  allocateLargestRemainder,
  clampBigInt,
  fromScaled,
  minBigInt,
  normalizeScale,
  percentOf,
  roundDiv,
  toScaled,
} from './offline-money.math';

const money = (cents: bigint): number => fromScaled(cents, MONEY_SCALE);

function applyDiscount(basis: bigint, type: number, value: number): bigint {
  if (basis <= 0n || value <= 0) return 0n;
  if (type === 1) return percentOf(basis, toScaled(value, PERCENT_SCALE));
  if (type === 2) return toScaled(value, MONEY_SCALE);
  return 0n;
}

function isRuleActive(rule: OfflineSalePricingRuleInput, soldAt: string): boolean {
  const soldAtMs = Date.parse(soldAt);
  if (Number.isNaN(soldAtMs)) return true;
  if (rule.startsAt) {
    const startMs = Date.parse(rule.startsAt);
    if (!Number.isNaN(startMs) && soldAtMs < startMs) return false;
  }
  if (rule.endsAt) {
    const endMs = Date.parse(rule.endsAt);
    if (!Number.isNaN(endMs) && soldAtMs > endMs) return false;
  }
  return true;
}

function pickBatchRule(rules: readonly OfflineSalePricingRuleInput[], line: OfflineSalePricingLineInput): OfflineSalePricingRuleInput | null {
  const candidates = rules.filter((rule) => {
    return rule.ruleType === 'BatchPercentage'
      && rule.inventoryBatchId === line.inventoryBatchId
      && rule.percentage > 0;
  });

  if (candidates.length === 0) return null;
  return candidates.reduce((best, current) => (current.percentage > best.percentage ? current : best));
}

function pickSaleRule(rules: readonly OfflineSalePricingRuleInput[], eligibleSubtotal: bigint): OfflineSalePricingRuleInput | null {
  const candidates = rules.filter((rule) => {
    if (rule.inventoryBatchId) return false;
    if (rule.percentage <= 0) return false;
    if (rule.ruleType === 'SalePercentage') return true;
    return rule.ruleType === 'SaleThresholdPercentage'
      && typeof rule.thresholdAmount === 'number'
      && eligibleSubtotal >= toScaled(rule.thresholdAmount, MONEY_SCALE);
  });

  if (candidates.length === 0) return null;

  return candidates.reduce((best, current) => {
    const bestAmount = percentOf(eligibleSubtotal, toScaled(best.percentage, PERCENT_SCALE));
    const currentAmount = percentOf(eligibleSubtotal, toScaled(current.percentage, PERCENT_SCALE));
    if (currentAmount !== bestAmount) return currentAmount > bestAmount ? current : best;

    const bestIsThreshold = best.ruleType === 'SaleThresholdPercentage';
    const currentIsThreshold = current.ruleType === 'SaleThresholdPercentage';
    if (currentIsThreshold !== bestIsThreshold) return currentIsThreshold ? current : best;

    return (current.thresholdAmount ?? 0) > (best.thresholdAmount ?? 0) ? current : best;
  });
}

function calculateEffectiveItemDiscount(type: number, value: number, preTax: bigint, rulePercentage: number | null): bigint {
  const overrideAmount = applyDiscount(preTax, type, value);
  if (rulePercentage === null) return overrideAmount;

  const configuredAmount = percentOf(preTax, toScaled(rulePercentage, PERCENT_SCALE));
  return type === 0 ? configuredAmount : minBigInt(configuredAmount, overrideAmount);
}

function calculateEffectiveSaleDiscount(type: number, value: number, eligibleSubtotal: bigint, rulePercentage: number | null): bigint {
  const configuredAmount = rulePercentage === null ? 0n : percentOf(eligibleSubtotal, toScaled(rulePercentage, PERCENT_SCALE));
  if (type === 0) return configuredAmount;

  const overrideAmount = applyDiscount(eligibleSubtotal, type, value);
  return rulePercentage === null ? overrideAmount : minBigInt(configuredAmount, overrideAmount);
}

// Prices a cart under calculation version 1. Inputs are normalised to the contract scales and frozen as normalised,
// so the queued invoice carries exactly the numbers the server replays. A tax-inclusive line never totals more than
// its shelf price.
export function calculateOfflineFrozenSale(input: OfflineSalePricingInput): OfflineFrozenSalePricing {
  const eligibleRules = input.rules.filter((rule) => isRuleActive(rule, input.soldAt));
  const drafts = input.lines.map((line, index) => {
    const isService = line.lineType === 'service';
    const quantity = toScaled(Math.max(0, line.quantity), QUANTITY_SCALE);
    const salesPrice = toScaled(line.salesPrice, MONEY_SCALE);
    const taxRate = toScaled(line.taxRatePercent, PERCENT_SCALE);
    const taxInclusive = line.taxIncluded && taxRate > 0n;
    const exactGross = salesPrice * quantity;
    const gross = roundDiv(exactGross, 1_000n);
    const preTax = taxInclusive ? roundDiv(exactGross * 10_000n, 1_000n * (10_000n + taxRate)) : gross;

    const rule = isService ? null : pickBatchRule(eligibleRules, line);
    const rulePercentage = rule ? normalizeScale(rule.percentage, PERCENT_SCALE) : null;
    const discountType = isService ? 0 : line.itemDiscount.type;
    const discountValue = isService ? 0 : normalizeScale(line.itemDiscount.value, discountType === 2 ? MONEY_SCALE : PERCENT_SCALE);
    const itemDiscount = isService ? 0n : clampBigInt(calculateEffectiveItemDiscount(discountType, discountValue, preTax, rulePercentage), preTax);

    return {
      line,
      index,
      isService,
      quantity,
      salesPrice,
      taxRate,
      taxInclusive,
      gross,
      preTax,
      itemDiscount,
      taxableBeforeSaleDiscount: preTax - itemDiscount,
      ruleId: rule?.ruleId ?? null,
      rulePercentage,
      discountType,
      discountValue,
    };
  });

  const eligibleSubtotal = drafts.reduce((sum, draft) => (draft.isService ? sum : sum + draft.taxableBeforeSaleDiscount), 0n);
  const saleRule = pickSaleRule(eligibleRules, eligibleSubtotal);
  const saleRulePercentage = saleRule ? normalizeScale(saleRule.percentage, PERCENT_SCALE) : null;
  const saleDiscountType = input.saleDiscount.type;
  const saleDiscountValue = normalizeScale(input.saleDiscount.value, saleDiscountType === 2 ? MONEY_SCALE : PERCENT_SCALE);
  const saleDiscountTotal = clampBigInt(
    calculateEffectiveSaleDiscount(saleDiscountType, saleDiscountValue, eligibleSubtotal, saleRulePercentage),
    eligibleSubtotal,
  );
  const allocation = allocateLargestRemainder(
    saleDiscountTotal,
    drafts.map((draft) => (draft.isService || draft.taxableBeforeSaleDiscount <= 0n ? 0n : draft.taxableBeforeSaleDiscount)),
  );

  let subtotal = 0n;
  let discounts = 0n;
  let taxes = 0n;
  let grandTotal = 0n;
  const lines = drafts.map((draft): OfflineFrozenSaleLine => {
    const saleDiscount = allocation[draft.index];
    const taxable = draft.taxableBeforeSaleDiscount - saleDiscount;
    let tax: bigint;
    let lineTotal: bigint;
    if (draft.taxInclusive) {
      // The shelf price less the tax-inclusive equivalent of the discounts, as online sales total it.
      const grossDiscount = roundDiv((draft.itemDiscount + saleDiscount) * (10_000n + draft.taxRate), 10_000n);
      lineTotal = draft.gross > grossDiscount ? draft.gross - grossDiscount : 0n;
      tax = lineTotal > taxable ? lineTotal - taxable : 0n;
    } else {
      tax = roundDiv(taxable * draft.taxRate, 10_000n);
      lineTotal = taxable + tax;
    }
    subtotal += draft.preTax;
    discounts += draft.itemDiscount + saleDiscount;
    taxes += tax;
    grandTotal += lineTotal;

    const { line, isService } = draft;
    return {
      clientLineId: line.clientLineId ?? `${line.inventoryBatchId || line.serviceId}-${draft.index + 1}`,
      inventoryBatchId: line.inventoryBatchId,
      itemId: line.itemId,
      barcode: line.barcode,
      itemName: line.itemName,
      batchNumber: line.batchNumber,
      quantity: fromScaled(draft.quantity, QUANTITY_SCALE),
      salesPrice: fromScaled(draft.salesPrice, MONEY_SCALE),
      mrp: isService ? 0 : line.mrp,
      costPrice: isService ? 0 : line.costPrice,
      taxRatePercent: fromScaled(draft.taxRate, PERCENT_SCALE),
      taxIncluded: line.taxIncluded,
      hsnCode: line.hsnCode,
      preTaxAmount: money(draft.preTax),
      itemDiscountAmount: money(draft.itemDiscount),
      saleDiscountAmount: money(saleDiscount),
      taxableAmount: money(taxable),
      taxAmount: money(tax),
      lineTotal: money(lineTotal),
      configuredRuleId: draft.ruleId,
      configuredRulePercentage: draft.rulePercentage,
      itemDiscountOverrideType: draft.discountType,
      itemDiscountOverrideValue: draft.discountValue,
      lineType: isService ? 'service' : 'goods',
      serviceId: line.serviceId ?? null,
    };
  });

  const paid = toScaled(Math.max(0, input.paidAmount), MONEY_SCALE);
  const due = grandTotal > paid ? grandTotal - paid : 0n;

  return {
    calculationVersion: OFFLINE_CALCULATION_VERSION,
    lines,
    totals: {
      subtotalBeforeDiscount: money(subtotal),
      // Matches the domain definition (line totals plus discounts) that online sales store.
      totalBeforeDiscount: money(grandTotal + discounts),
      totalDiscount: money(discounts),
      totalTax: money(taxes),
      grandTotal: money(grandTotal),
      paidAmount: money(paid),
      dueAmount: money(due),
    },
    saleDiscountOverrideType: saleDiscountType,
    saleDiscountOverrideValue: saleDiscountValue,
    configuredSaleRuleId: saleRule?.ruleId ?? null,
    configuredSaleRuleType: saleRule?.ruleType ?? null,
    configuredSaleRulePercentage: saleRulePercentage,
    configuredSaleRuleThresholdAmount: saleRule?.thresholdAmount == null ? null : normalizeScale(saleRule.thresholdAmount, MONEY_SCALE),
  };
}
