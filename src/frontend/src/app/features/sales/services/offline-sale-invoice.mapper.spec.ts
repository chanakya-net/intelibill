import { describe, expect, it } from 'vitest';

import { mapOfflineQueuedSaleToSaleDto } from './offline-sale-invoice.mapper';
import type { InvoiceDocument } from './sale.models';

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
            lineType: 'goods',
            serviceId: null,
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
      creditNoteAppliedAmount: 0,
      creditNoteRedemptions: [],
      // Queued before devices froze documents: nothing to print it from but the priced bill.
      document: null,
      isLegacyDocument: true,
      returns: [],
      warnings: [],
      items: [
        {
          saleItemId: 'line-1',
          lineType: 'Goods',
          itemId: 'item-1',
          serviceId: null,
          itemName: 'Test Item',
          lineCode: '12345',
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
          uom: null,
          quantityScale: 0,
        },
      ],
    });
  });

  it('carries the printed unit and the precision a queued line was sold in', () => {
    const dto = mapOfflineQueuedSaleToSaleDto({
      clientSaleId: 'client-sale-3',
      idempotencyKey: 'offline-sale-client-sale-3',
      shopId: 'shop-1',
      deviceId: 'device-1',
      invoiceNumber: 'A/2627/000003',
      soldAt: '2026-11-05T10:00:00Z',
      paymentMethod: 1,
      customerId: null,
      customerName: null,
      customerPhone: null,
      pricing: {
        lines: [
          {
            clientLineId: 'line-kg',
            inventoryBatchId: 'batch-kg',
            itemId: 'item-kg',
            barcode: 'RICE',
            itemName: 'Rice',
            batchNumber: 'B-1',
            quantity: 0.25,
            salesPrice: 400,
            mrp: 400,
            costPrice: 300,
            taxRatePercent: 0,
            taxIncluded: false,
            hsnCode: null,
            documentUnit: 'KGS',
            preTaxAmount: 100,
            itemDiscountAmount: 0,
            saleDiscountAmount: 0,
            taxableAmount: 100,
            taxAmount: 0,
            lineTotal: 100,
            configuredRuleId: null,
            lineType: 'goods',
            serviceId: null,
          },
        ],
        totals: { totalBeforeDiscount: 100, totalDiscount: 0, totalTax: 0, grandTotal: 100, paidAmount: 100, dueAmount: 0 },
      },
    });

    expect(dto.items[0]).toMatchObject({ quantity: 0.25, uom: 'KGS', quantityScale: 2 });
  });

  it('prints a queued sale from the document frozen when it was issued', () => {
    const document = { version: 2, number: 'A/2627/000001' } as unknown as InvoiceDocument;
    const dto = mapOfflineQueuedSaleToSaleDto({
      clientSaleId: 'client-sale-2',
      idempotencyKey: 'offline-sale-client-sale-2',
      shopId: 'shop-1',
      deviceId: 'device-1',
      invoiceNumber: 'A/2627/000001',
      soldAt: '2026-11-03T10:00:00Z',
      paymentMethod: 1,
      customerId: null,
      customerName: null,
      customerPhone: null,
      pricing: {
        lines: [],
        totals: { totalBeforeDiscount: 0, totalDiscount: 0, totalTax: 0, grandTotal: 0, paidAmount: 0, dueAmount: 0 },
      },
      document,
    });

    expect(dto.document).toBe(document);
    expect(dto.isLegacyDocument).toBe(false);
  });
});
