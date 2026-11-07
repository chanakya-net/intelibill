import { describe, expect, it } from 'vitest';
import { NewSalePageDiscountValidationService } from './new-sale-page.discount-validation.service';
import type { SalePreviewDto } from '../services/sale.models';

describe('discount ceilings', () => {
  it.each([false, true])('uses exact midpoint cost for the sale ceiling (economics supplied=%s)', (supplied) => {
    const preview: SalePreviewDto = { totalAmount: 2, totalTaxableAmount: 2, totalTaxAmount: 0, totalDiscountAmount: 0, infos: [], warnings: [], saleLevelEligibleSubtotal: 2, configuredSaleRule: null, lines: [{ itemId: 'i', barcode: 'i', itemName: 'Item', inventoryBatchId: 'b', batchNumber: 'B', salesPrice: 4, mrp: 4, taxRatePercent: 0, isPriceIncludingTax: false, saleDiscountAmount: 0, taxableAmount: 2, taxAmount: 0, lineTotalAmount: 2, maxAllowedItemDiscountFlat: .99, maxAllowedItemDiscountPercent: 49.5, configuredBatchRuleId: null, configuredBatchRulePercentage: null, hasClientPriceMismatch: false, clientLineKey: 'l', lineType: 'Goods', preTaxAmountBeforeDiscount: 2,
      itemDiscountAmount: 0, costPrice: 2.01, quantity: .5, ...(supplied ? { economics: { netRevenue: 2, economicCost: 1.01, grossProfit: .99, grossMarginPercent: 49.5, markupPercent: 98.02 } } : {}) }] };
    const limits = NewSalePageDiscountValidationService.prototype.getSaleDiscountLimits.call({ checkoutPreview: () => preview,
      roundAmount: (value: number) => Number(value.toFixed(2)) } as never);
    expect(limits).toEqual({ isEligible: true, maxFlat: .99, maxPercent: 49.5 });
  });
});
