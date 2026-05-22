import { describe, expect, it } from 'vitest';

import { calculateOfflineFrozenSale } from './offline-sale-pricing-calculator';

describe('calculateOfflineFrozenSale', () => {
  it('freezes deterministic totals and line math', () => {
    const result = calculateOfflineFrozenSale({
      soldAt: '2026-05-22T10:00:00.000Z',
      paymentMethod: 1,
      paidAmount: 150,
      customerId: null,
      customerName: null,
      customerPhone: null,
      saleDiscount: { type: 1, value: 10 },
      rules: [{ ruleId: 'rule-1', ruleType: 'BatchPercentage', inventoryBatchId: 'batch-1', percentage: 5 }],
      lines: [
        {
          inventoryBatchId: 'batch-1',
          itemId: 'item-1',
          barcode: '111',
          itemName: 'A',
          batchNumber: 'B1',
          quantity: 2,
          salesPrice: 100,
          mrp: 120,
          costPrice: 80,
          taxRatePercent: 18,
          taxIncluded: false,
          itemDiscount: { type: 2, value: 10 },
          hsnCode: null,
        },
        {
          inventoryBatchId: 'batch-2',
          itemId: 'item-2',
          barcode: '222',
          itemName: 'B',
          batchNumber: 'B2',
          quantity: 1,
          salesPrice: 50,
          mrp: 60,
          costPrice: 30,
          taxRatePercent: 5,
          taxIncluded: false,
          itemDiscount: { type: 0, value: 0 },
          hsnCode: null,
        },
      ],
    });

    expect(result.lines).toHaveLength(2);
    expect(result.lines[0].clientLineId).toBe('batch-1-1');
    expect(result.lines[0]).toMatchObject({
      configuredRuleId: 'rule-1',
      configuredRulePercentage: 5,
      itemDiscountOverrideType: 2,
      itemDiscountOverrideValue: 10,
    });
    expect(result).toMatchObject({
      saleDiscountOverrideType: 1,
      saleDiscountOverrideValue: 10,
      configuredSaleRuleId: null,
      configuredSaleRuleType: null,
      configuredSaleRulePercentage: null,
      configuredSaleRuleThresholdAmount: null,
    });
    expect(result.totals.totalBeforeDiscount).toBe(250);
    expect(result.totals.totalDiscount).toBe(34);
    expect(result.totals.totalTax).toBe(33.03);
    expect(result.totals.grandTotal).toBe(249.03);
    expect(result.totals.paidAmount).toBe(150);
    expect(result.totals.dueAmount).toBe(99.03);
  });

  it('applies sale-level percentage rules only as allocated sale discounts', () => {
    const result = calculateOfflineFrozenSale({
      soldAt: '2026-05-22T10:00:00.000Z',
      paymentMethod: 1,
      paidAmount: 0,
      customerId: null,
      customerName: null,
      customerPhone: null,
      saleDiscount: { type: 0, value: 0 },
      rules: [{ ruleId: 'sale-rule', ruleType: 'SalePercentage', inventoryBatchId: null, percentage: 10 }],
      lines: [{
        inventoryBatchId: 'batch-1',
        itemId: 'item-1',
        barcode: '111',
        itemName: 'A',
        batchNumber: 'B1',
        quantity: 1,
        salesPrice: 100,
        mrp: 100,
        costPrice: 0,
        taxRatePercent: 0,
        taxIncluded: false,
        itemDiscount: { type: 0, value: 0 },
        hsnCode: null,
      }],
    });

    expect(result.lines[0].itemDiscountAmount).toBe(0);
    expect(result.lines[0].configuredRuleId).toBeNull();
    expect(result.lines[0].saleDiscountAmount).toBe(10);
    expect(result).toMatchObject({
      configuredSaleRuleId: 'sale-rule',
      configuredSaleRuleType: 'SalePercentage',
      configuredSaleRulePercentage: 10,
      configuredSaleRuleThresholdAmount: null,
    });
    expect(result.totals.totalDiscount).toBe(10);
    expect(result.totals.grandTotal).toBe(90);
  });

  it('honors sale threshold rules after configured batch discounts', () => {
    const common = {
      soldAt: '2026-05-22T10:00:00.000Z',
      paymentMethod: 1,
      paidAmount: 0,
      customerId: null,
      customerName: null,
      customerPhone: null,
      saleDiscount: { type: 0 as const, value: 0 },
      rules: [
        { ruleId: 'batch-rule', ruleType: 'BatchPercentage', inventoryBatchId: 'batch-1', percentage: 10 },
        { ruleId: 'threshold-rule', ruleType: 'SaleThresholdPercentage', inventoryBatchId: null, percentage: 20, thresholdAmount: 95 },
      ],
      lines: [{
        inventoryBatchId: 'batch-1',
        itemId: 'item-1',
        barcode: '111',
        itemName: 'A',
        batchNumber: 'B1',
        quantity: 1,
        salesPrice: 100,
        mrp: 100,
        costPrice: 0,
        taxRatePercent: 0,
        taxIncluded: false,
        itemDiscount: { type: 0 as const, value: 0 },
        hsnCode: null,
      }],
    };

    const belowThreshold = calculateOfflineFrozenSale(common);
    expect(belowThreshold.lines[0].itemDiscountAmount).toBe(10);
    expect(belowThreshold.lines[0].saleDiscountAmount).toBe(0);
    expect(belowThreshold.totals.grandTotal).toBe(90);

    const aboveThreshold = calculateOfflineFrozenSale({
      ...common,
      rules: [
        common.rules[0],
        { ...common.rules[1], thresholdAmount: 80 },
      ],
    });

    expect(aboveThreshold.lines[0].saleDiscountAmount).toBe(18);
    expect(aboveThreshold.totals.grandTotal).toBe(72);
  });

  it('uses configured batch discount unless an instant item override lowers it', () => {
    const base = {
      soldAt: '2026-05-22T10:00:00.000Z',
      paymentMethod: 1,
      paidAmount: 0,
      customerId: null,
      customerName: null,
      customerPhone: null,
      saleDiscount: { type: 0 as const, value: 0 },
      rules: [{ ruleId: 'batch-rule', ruleType: 'BatchPercentage', inventoryBatchId: 'batch-1', percentage: 10 }],
      lines: [{
        inventoryBatchId: 'batch-1',
        itemId: 'item-1',
        barcode: '111',
        itemName: 'A',
        batchNumber: 'B1',
        quantity: 1,
        salesPrice: 100,
        mrp: 100,
        costPrice: 0,
        taxRatePercent: 0,
        taxIncluded: false,
        itemDiscount: { type: 0 as const, value: 0 },
        hsnCode: null,
      }],
    };

    const configuredOnly = calculateOfflineFrozenSale(base);
    expect(configuredOnly.lines[0].itemDiscountAmount).toBe(10);

    const increasedOverride = calculateOfflineFrozenSale({
      ...base,
      lines: [{ ...base.lines[0], itemDiscount: { type: 1, value: 20 } }],
    });
    expect(increasedOverride.lines[0].itemDiscountAmount).toBe(10);

    const reducedOverride = calculateOfflineFrozenSale({
      ...base,
      lines: [{ ...base.lines[0], itemDiscount: { type: 1, value: 5 } }],
    });
    expect(reducedOverride.lines[0].itemDiscountAmount).toBe(5);
  });

  it('keeps due at zero when paid exceeds total', () => {
    const result = calculateOfflineFrozenSale({
      soldAt: '2026-05-22T10:00:00.000Z',
      paymentMethod: 1,
      paidAmount: 1000,
      customerId: null,
      customerName: null,
      customerPhone: null,
      saleDiscount: { type: 0, value: 0 },
      rules: [],
      lines: [{
        inventoryBatchId: 'batch-1',
        itemId: 'item-1',
        barcode: '111',
        itemName: 'A',
        batchNumber: 'B1',
        quantity: 1,
        salesPrice: 100,
        mrp: 100,
        costPrice: 80,
        taxRatePercent: 0,
        taxIncluded: false,
        itemDiscount: { type: 0, value: 0 },
        hsnCode: null,
      }],
    });

    expect(result.totals.dueAmount).toBe(0);
  });

  it('returns stable output for identical inputs', () => {
    const input = {
      soldAt: '2026-05-22T10:00:00.000Z',
      paymentMethod: 1,
      paidAmount: 0,
      customerId: null,
      customerName: null,
      customerPhone: null,
      saleDiscount: { type: 0 as const, value: 0 },
      rules: [],
      lines: [{
        inventoryBatchId: 'batch-1',
        itemId: 'item-1',
        barcode: '111',
        itemName: 'A',
        batchNumber: 'B1',
        quantity: 1,
        salesPrice: 100,
        mrp: 100,
        costPrice: 80,
        taxRatePercent: 0,
        taxIncluded: false,
        itemDiscount: { type: 0 as const, value: 0 },
        hsnCode: null,
      }],
    };

    const first = calculateOfflineFrozenSale(input);
    const second = calculateOfflineFrozenSale(input);

    expect(first).toEqual(second);
  });
});
