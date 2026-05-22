import { describe, expect, it } from 'vitest';

import { mapOfflineQueuedSaleToSaleDto } from './offline-sale-invoice.mapper';

describe('mapOfflineQueuedSaleToSaleDto', () => {
  it('maps offline queued payload into sale invoice dto', () => {
    const dto = mapOfflineQueuedSaleToSaleDto({
      clientSaleId: 'client-sale-1',
      idempotencyKey: 'offline-sale-client-sale-1',
      shopId: 'shop-1',
      deviceId: 'device-1',
      invoiceNumber: 'INV-2026-001',
      soldAt: '2026-05-01T10:00:00Z',
      paymentMethod: 1,
      customerId: 'customer-1',
      customerName: 'Jane Doe',
      customerPhone: '+919999111222',
      pricing: {
        lines: [
          {
            clientLineId: 'line-1',
            inventoryBatchId: 'batch-1',
            itemId: 'item-1',
            barcode: '12345',
            itemName: 'Test Item',
            batchNumber: 'B-1',
            quantity: 3,
            salesPrice: 100,
            mrp: 120,
            costPrice: 60,
            taxRatePercent: 18,
            taxIncluded: true,
            hsnCode: '1001',
            preTaxAmount: 254.24,
            itemDiscountAmount: 10,
            saleDiscountAmount: 5,
            taxableAmount: 239.24,
            taxAmount: 43.06,
            lineTotal: 282.3,
            configuredRuleId: null,
          },
        ],
        totals: {
          totalBeforeDiscount: 280,
          totalDiscount: 15,
          totalTax: 43.06,
          grandTotal: 282.3,
          paidAmount: 0,
          dueAmount: 282.3,
        },
      },
    });

    expect(dto).toEqual({
      saleId: 'client-sale-1',
      invoiceNumber: 'INV-2026-001',
      customerId: 'customer-1',
      customerName: 'Jane Doe',
      customerPhone: '+919999111222',
      paymentMethod: 1,
      soldAt: '2026-05-01T10:00:00Z',
      paidAmount: 0,
      dueAmount: 282.3,
      totalBeforeDiscount: 280,
      totalDiscountAmount: 15,
      totalAmount: 282.3,
      totalTaxAmount: 43.06,
      returns: [],
      warnings: [],
      items: [
        {
          saleItemId: 'line-1',
          itemId: 'item-1',
          itemName: 'Test Item',
          inventoryBatchId: 'batch-1',
          quantity: 3,
          salesPrice: 100,
          originalSalesPrice: 100,
          finalSalesPrice: 100,
          preTaxAmountBeforeDiscount: 254.24,
          itemDiscountAmount: 10,
          saleDiscountAmount: 5,
          taxableAmount: 239.24,
          taxAmount: 43.06,
          totalAmount: 282.3,
          savingsAmount: 15,
          taxRatePercent: 18,
          isPriceIncludingTax: true,
          hasPriceMismatch: false,
          returnedQuantity: 0,
          returnableQuantity: 3,
          returnStatus: 'None',
          hsnCode: '1001',
        },
      ],
    });
  });
});
