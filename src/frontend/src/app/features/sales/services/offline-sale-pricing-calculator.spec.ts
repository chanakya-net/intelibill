import { describe, expect, it } from 'vitest';

import contract from '../../../../../../../tests/contracts/offline-invoice-arithmetic-v1.json';
import corpus from '../../../../../../../tests/contracts/offline-invoice-arithmetic-v1-corpus.json';
import type { OfflineSalePricingInput, OfflineSalePricingRuleInput } from './offline-sale-core.types';
import { calculateOfflineFrozenSale } from './offline-sale-pricing-calculator';

interface ContractLine {
  readonly lineType: 'goods' | 'service';
  readonly quantity: number;
  readonly salesPrice: number;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
  readonly itemDiscount: { readonly type: number; readonly value: number };
  readonly batchRulePercentage: number | null;
}

interface ContractCase {
  readonly name: string;
  readonly input: {
    readonly lines: readonly ContractLine[];
    readonly saleDiscount: { readonly type: number; readonly value: number };
    readonly saleRule: { readonly ruleType: string; readonly percentage: number; readonly thresholdAmount: number | null } | null;
    readonly paidAmount: number;
  };
  readonly expected: {
    readonly lines: readonly Record<string, number>[];
    readonly totals: Record<string, number>;
  };
}

function toPricingInput(input: ContractCase['input']): OfflineSalePricingInput {
  const rules: OfflineSalePricingRuleInput[] = input.lines.flatMap((line, index) =>
    line.batchRulePercentage === null
      ? []
      : [{ ruleId: `rule-${index}`, ruleType: 'BatchPercentage', inventoryBatchId: `batch-${index}`, percentage: line.batchRulePercentage }],
  );
  if (input.saleRule) {
    rules.push({ ruleId: 'sale-rule', ruleType: input.saleRule.ruleType, inventoryBatchId: null, percentage: input.saleRule.percentage, thresholdAmount: input.saleRule.thresholdAmount });
  }

  return {
    soldAt: '2026-11-03T10:00:00.000Z',
    paymentMethod: 1,
    paidAmount: input.paidAmount,
    customerId: null,
    customerName: null,
    customerPhone: null,
    saleDiscount: { type: input.saleDiscount.type as 0 | 1 | 2, value: input.saleDiscount.value },
    rules,
    lines: input.lines.map((line, index) => ({
      inventoryBatchId: `batch-${index}`,
      itemId: `item-${index}`,
      barcode: `BC-${index}`,
      itemName: `Line ${index}`,
      batchNumber: line.lineType === 'service' ? '' : `B-${index}`,
      quantity: line.quantity,
      salesPrice: line.salesPrice,
      mrp: 0,
      costPrice: 0,
      taxRatePercent: line.taxRatePercent,
      taxIncluded: line.taxIncluded,
      itemDiscount: { type: line.itemDiscount.type as 0 | 1 | 2, value: line.itemDiscount.value },
      hsnCode: null,
      lineType: line.lineType,
      serviceId: line.lineType === 'service' ? `service-${index}` : null,
    })),
  };
}

describe('offline invoice arithmetic contract v1', () => {
  const cases = contract.cases as readonly ContractCase[];
  const corpusCases = corpus.cases as readonly ContractCase[];

  it.each([...cases, ...corpusCases].map((testCase) => [testCase.name, testCase] as const))('%s matches the shared fixture exactly', (_name, testCase) => {
    const result = calculateOfflineFrozenSale(toPricingInput(testCase.input));

    expect(result.calculationVersion).toBe(contract.calculationVersion);
    expect(result.lines.map((line) => ({
      preTaxAmount: line.preTaxAmount,
      itemDiscountAmount: line.itemDiscountAmount,
      saleDiscountAmount: line.saleDiscountAmount,
      taxableAmount: line.taxableAmount,
      taxAmount: line.taxAmount,
      lineTotal: line.lineTotal,
    }))).toEqual(testCase.expected.lines);
    expect(result.totals).toEqual(testCase.expected.totals);
  });

  it('gives the same invoice however a quantity was reached', () => {
    const base = cases.find((testCase) => testCase.name === 'exclusive_fractional_quantity')!;
    const edited = { ...base.input, lines: [{ ...base.input.lines[0], quantity: 0.1 + 0.15 }] };

    expect(calculateOfflineFrozenSale(toPricingInput(edited))).toEqual(calculateOfflineFrozenSale(toPricingInput(base.input)));
  });

  it('freezes inputs at the contract scales so the server can replay them', () => {
    const base = cases.find((testCase) => testCase.name === 'exclusive_single_line')!;
    const result = calculateOfflineFrozenSale(toPricingInput({
      ...base.input,
      saleDiscount: { type: 2, value: 1.005 },
      lines: [{ ...base.input.lines[0], quantity: 1.0004, itemDiscount: { type: 1, value: 2.555 } }],
    }));

    expect(result.lines[0].quantity).toBe(1);
    expect(result.lines[0].itemDiscountOverrideValue).toBe(2.56);
    expect(result.saleDiscountOverrideValue).toBe(1.01);
  });
});

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
    expect(result.calculationVersion).toBe(1);
    expect(result.totals.subtotalBeforeDiscount).toBe(250);
    expect(result.totals.totalBeforeDiscount).toBe(283.03);
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

  it('service lines receive no discounts, cost=0, and are excluded from eligibleSubtotal', () => {
    const result = calculateOfflineFrozenSale({
      soldAt: '2026-05-22T10:00:00.000Z',
      paymentMethod: 1,
      paidAmount: 0,
      customerId: null,
      customerName: null,
      customerPhone: null,
      saleDiscount: { type: 1, value: 20 },
      rules: [{ ruleId: 'sale-rule', ruleType: 'SalePercentage', inventoryBatchId: null, percentage: 10 }],
      lines: [
        {
          inventoryBatchId: '',
          serviceId: 'svc-1',
          itemId: 'svc-1',
          barcode: 'SVC001',
          itemName: 'Consulting',
          batchNumber: '',
          quantity: 1,
          salesPrice: 200,
          mrp: 0,
          costPrice: 0,
          taxRatePercent: 18,
          taxIncluded: false,
          itemDiscount: { type: 2, value: 10 },
          hsnCode: null,
          lineType: 'service',
        },
        {
          inventoryBatchId: 'batch-1',
          itemId: 'item-1',
          barcode: '111',
          itemName: 'Goods A',
          batchNumber: 'B1',
          quantity: 1,
          salesPrice: 100,
          mrp: 100,
          costPrice: 80,
          taxRatePercent: 0,
          taxIncluded: false,
          itemDiscount: { type: 0, value: 0 },
          hsnCode: null,
        },
      ],
    });

    const serviceLine = result.lines.find((l) => l.lineType === 'service')!;
    const goodsLine = result.lines.find((l) => l.lineType !== 'service')!;

    // Service line: no item discount, no sale discount, cost=0
    expect(serviceLine.itemDiscountAmount).toBe(0);
    expect(serviceLine.saleDiscountAmount).toBe(0);
    expect(serviceLine.costPrice).toBe(0);
    expect(serviceLine.mrp).toBe(0);
    expect(serviceLine.serviceId).toBe('svc-1');
    expect(serviceLine.lineType).toBe('service');

    // Goods line should receive full sale discount (service excluded from eligibleSubtotal)
    expect(goodsLine.saleDiscountAmount).toBeGreaterThan(0);
    expect(goodsLine.lineType).toBe('goods');
    expect(goodsLine.serviceId).toBeNull();
  });

  it('service-only sale has zero total discounts', () => {
    const result = calculateOfflineFrozenSale({
      soldAt: '2026-05-22T10:00:00.000Z',
      paymentMethod: 1,
      paidAmount: 0,
      customerId: null,
      customerName: null,
      customerPhone: null,
      saleDiscount: { type: 1, value: 50 },
      rules: [{ ruleId: 'sale-rule', ruleType: 'SalePercentage', inventoryBatchId: null, percentage: 20 }],
      lines: [{
        inventoryBatchId: '',
        serviceId: 'svc-1',
        itemId: 'svc-1',
        barcode: 'SVC001',
        itemName: 'Repair',
        batchNumber: '',
        quantity: 2,
        salesPrice: 500,
        mrp: 0,
        costPrice: 0,
        taxRatePercent: 18,
        taxIncluded: false,
        itemDiscount: { type: 2, value: 30 },
        hsnCode: null,
        lineType: 'service',
      }],
    });

    expect(result.totals.totalDiscount).toBe(0);
    expect(result.lines[0].itemDiscountAmount).toBe(0);
    expect(result.lines[0].saleDiscountAmount).toBe(0);
    expect(result.lines[0].lineType).toBe('service');
  });
});
