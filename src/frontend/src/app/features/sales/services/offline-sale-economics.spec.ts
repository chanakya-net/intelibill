import { describe, expect, it } from 'vitest';
import { evaluateOfflineSaleEconomics } from './offline-sale-economics';
import { calculateOfflineFrozenSale } from './offline-sale-pricing-calculator';

describe('offline issuance economics', () => {
  it.each([['goods', 99.99, 33.3, 0], ['goods', 100.02, 33.31, -0.01], ['service', 100.02, 0, 33.3]] as const)(
    'rounds quantity cost once and excludes service cost (%s %s)', (lineType, costPrice, cost, profit) => {
      const pricing = calculateOfflineFrozenSale({ soldAt: '2026-06-01T10:00:00Z', paymentMethod: 0, paidAmount: 100,
        customerId: null, customerName: null, customerPhone: null, rules: [], saleDiscount: { type: 0, value: 0 },
        lines: [{ clientLineId: 'one', inventoryBatchId: 'b', itemId: 'i', barcode: 'i', itemName: 'Item', batchNumber: 'b',
          quantity: .333, salesPrice: 100, mrp: 100, costPrice, taxRatePercent: 0, taxIncluded: false,
          itemDiscount: { type: 0, value: 0 }, hsnCode: null, lineType }] });
      const result = evaluateOfflineSaleEconomics(pricing);
      expect(result.lines[0].economicCost).toBe(cost);
      expect(result.lines[0].grossProfit).toBe(profit);
      expect(result.belowCostLineIds).toEqual(profit < 0 ? ['one'] : []);
    },
  );
});
