import {
  type OfflineFrozenSaleLine,
  type OfflineFrozenSalePricing,
  type OfflineSalePricingInput,
  type OfflineSalePricingLineInput,
  type OfflineSalePricingRuleInput,
} from './offline-sale-core.types';

const MONEY_PRECISION = 2;

function roundMoney(value: number): number {
  return Number(value.toFixed(MONEY_PRECISION));
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

function applyDiscount(base: number, type: number, value: number): number {
  if (base <= 0 || value <= 0) return 0;
  if (type === 1) return roundMoney(base * (value / 100));
  if (type === 2) return roundMoney(value);
  return 0;
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

function pickSaleRule(rules: readonly OfflineSalePricingRuleInput[], eligibleSubtotal: number): OfflineSalePricingRuleInput | null {
  const candidates = rules.filter((rule) => {
    if (rule.inventoryBatchId) return false;
    if (rule.percentage <= 0) return false;
    if (rule.ruleType === 'SalePercentage') return true;
    return rule.ruleType === 'SaleThresholdPercentage'
      && typeof rule.thresholdAmount === 'number'
      && eligibleSubtotal >= rule.thresholdAmount;
  });

  if (candidates.length === 0) return null;

  return candidates.reduce((best, current) => {
    const bestAmount = roundMoney(eligibleSubtotal * (best.percentage / 100));
    const currentAmount = roundMoney(eligibleSubtotal * (current.percentage / 100));
    if (currentAmount !== bestAmount) return currentAmount > bestAmount ? current : best;

    const bestIsThreshold = best.ruleType === 'SaleThresholdPercentage';
    const currentIsThreshold = current.ruleType === 'SaleThresholdPercentage';
    if (currentIsThreshold !== bestIsThreshold) return currentIsThreshold ? current : best;

    return (current.thresholdAmount ?? 0) > (best.thresholdAmount ?? 0) ? current : best;
  });
}

function calculateEffectiveItemDiscount(
  discount: OfflineSalePricingLineInput['itemDiscount'],
  preTaxAmount: number,
  batchRule: OfflineSalePricingRuleInput | null,
): number {
  if (!batchRule) {
    return applyDiscount(preTaxAmount, discount.type, discount.value);
  }

  const configuredAmount = roundMoney(preTaxAmount * (batchRule.percentage / 100));
  if (discount.type === 0) return configuredAmount;

  const overrideAmount = applyDiscount(preTaxAmount, discount.type, discount.value);
  return roundMoney(Math.min(configuredAmount, overrideAmount));
}

function calculateEffectiveSaleDiscount(
  discount: OfflineSalePricingInput['saleDiscount'],
  eligibleSubtotal: number,
  saleRule: OfflineSalePricingRuleInput | null,
): number {
  const configuredAmount = saleRule
    ? roundMoney(eligibleSubtotal * (saleRule.percentage / 100))
    : 0;

  if (discount.type === 0) return configuredAmount;

  const overrideAmount = applyDiscount(eligibleSubtotal, discount.type, discount.value);
  return saleRule ? roundMoney(Math.min(configuredAmount, overrideAmount)) : overrideAmount;
}

export function calculateOfflineFrozenSale(input: OfflineSalePricingInput): OfflineFrozenSalePricing {
  const eligibleRules = input.rules.filter((rule) => isRuleActive(rule, input.soldAt));
  const lines = input.lines.map((line, index) => {
    const quantity = Math.max(0, line.quantity);
    const grossAmount = roundMoney(line.salesPrice * quantity);
    const preTaxAmount = line.taxIncluded
      ? roundMoney(grossAmount / (1 + line.taxRatePercent / 100))
      : grossAmount;

    const rule = pickBatchRule(eligibleRules, line);
    const itemDiscountAmount = clamp(
      calculateEffectiveItemDiscount(line.itemDiscount, preTaxAmount, rule),
      0,
      preTaxAmount,
    );

    const taxableBeforeSaleDiscount = roundMoney(preTaxAmount - itemDiscountAmount);
    const taxAmountBeforeSaleDiscount = roundMoney(taxableBeforeSaleDiscount * (line.taxRatePercent / 100));
    const lineTotalBeforeSaleDiscount = line.taxIncluded
      ? roundMoney(taxableBeforeSaleDiscount + taxAmountBeforeSaleDiscount)
      : roundMoney(taxableBeforeSaleDiscount + taxAmountBeforeSaleDiscount);

    return {
      clientLineId: line.clientLineId ?? `${line.inventoryBatchId}-${index + 1}`,
      inventoryBatchId: line.inventoryBatchId,
      itemId: line.itemId,
      barcode: line.barcode,
      itemName: line.itemName,
      batchNumber: line.batchNumber,
      quantity,
      salesPrice: line.salesPrice,
      mrp: line.mrp,
      costPrice: line.costPrice,
      taxRatePercent: line.taxRatePercent,
      taxIncluded: line.taxIncluded,
      hsnCode: line.hsnCode,
      preTaxAmount,
      itemDiscountAmount,
      saleDiscountAmount: 0,
      taxableAmount: taxableBeforeSaleDiscount,
      taxAmount: taxAmountBeforeSaleDiscount,
      lineTotal: lineTotalBeforeSaleDiscount,
      configuredRuleId: rule?.ruleId ?? null,
      configuredRulePercentage: rule?.percentage ?? null,
      itemDiscountOverrideType: line.itemDiscount.type,
      itemDiscountOverrideValue: line.itemDiscount.value,
    };
  });

  const eligibleSubtotal = roundMoney(lines.reduce((sum, line) => sum + line.taxableAmount, 0));
  const saleRule = pickSaleRule(eligibleRules, eligibleSubtotal);
  const saleDiscountRaw = calculateEffectiveSaleDiscount(input.saleDiscount, eligibleSubtotal, saleRule);
  const saleDiscountTotal = clamp(saleDiscountRaw, 0, eligibleSubtotal);

  let allocated = 0;
  const pricedLines = lines.map((line, index) => {
    const weight = eligibleSubtotal > 0 ? line.taxableAmount / eligibleSubtotal : 0;
    const amount = index === lines.length - 1
      ? roundMoney(saleDiscountTotal - allocated)
      : roundMoney(saleDiscountTotal * weight);
    allocated = roundMoney(allocated + amount);

    const taxableAmount = roundMoney(line.taxableAmount - amount);
    const taxAmount = roundMoney(taxableAmount * (line.taxRatePercent / 100));
    const lineTotal = roundMoney(taxableAmount + taxAmount);

    return {
      ...line,
      saleDiscountAmount: amount,
      taxableAmount,
      taxAmount,
      lineTotal,
    } as OfflineFrozenSaleLine;
  });

  const totalBeforeDiscount = roundMoney(pricedLines.reduce((sum, line) => sum + line.preTaxAmount, 0));
  const totalItemDiscount = roundMoney(pricedLines.reduce((sum, line) => sum + line.itemDiscountAmount, 0));
  const totalSaleDiscount = roundMoney(pricedLines.reduce((sum, line) => sum + line.saleDiscountAmount, 0));
  const totalDiscount = roundMoney(totalItemDiscount + totalSaleDiscount);
  const totalTax = roundMoney(pricedLines.reduce((sum, line) => sum + line.taxAmount, 0));
  const grandTotal = roundMoney(pricedLines.reduce((sum, line) => sum + line.lineTotal, 0));
  const paidAmount = roundMoney(Math.max(0, input.paidAmount));
  const dueAmount = roundMoney(Math.max(grandTotal - paidAmount, 0));

  return {
    lines: pricedLines,
    totals: {
      totalBeforeDiscount,
      totalDiscount,
      totalTax,
      grandTotal,
      paidAmount,
      dueAmount,
    },
    saleDiscountOverrideType: input.saleDiscount.type,
    saleDiscountOverrideValue: input.saleDiscount.value,
    configuredSaleRuleId: saleRule?.ruleId ?? null,
    configuredSaleRuleType: saleRule?.ruleType ?? null,
    configuredSaleRulePercentage: saleRule?.percentage ?? null,
    configuredSaleRuleThresholdAmount: saleRule?.thresholdAmount ?? null,
  };
}
