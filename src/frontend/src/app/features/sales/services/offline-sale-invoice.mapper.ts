import { quantityScaleOf } from '../../../shared/quantity/quantity-input';
import type { SaleDto } from './sale.models';
import { OfflineQueuedSalePayload } from './offline-sale-core.types';

export function mapOfflineQueuedSaleToSaleDto(payload: OfflineQueuedSalePayload): SaleDto {
  return {
    saleId: payload.clientSaleId,
    invoiceNumber: payload.invoiceNumber,
    customerId: payload.customerId,
    customerName: payload.customerName,
    customerPhone: payload.customerPhone,
    paymentMethod: payload.paymentMethod,
    soldAt: payload.soldAt,
    paidAmount: payload.pricing.totals.paidAmount,
    dueAmount: payload.pricing.totals.dueAmount,
    totalBeforeDiscount: payload.pricing.totals.totalBeforeDiscount,
    totalDiscountAmount: payload.pricing.totals.totalDiscount,
    totalAmount: payload.pricing.totals.grandTotal,
    totalTaxAmount: payload.pricing.totals.totalTax,
    creditNoteAppliedAmount: 0,
    creditNoteRedemptions: [],
    // The document frozen at finalization; payloads queued before devices issued documents have none.
    document: payload.document ?? null,
    isLegacyDocument: !payload.document,
    items: payload.pricing.lines.map((line) => ({
      saleItemId: line.clientLineId,
      lineType: 'Goods',
      itemId: line.itemId,
      serviceId: null,
      itemName: line.itemName,
      lineCode: line.barcode,
      inventoryBatchId: line.inventoryBatchId,
      quantity: line.quantity,
      salesPrice: line.salesPrice,
      originalSalesPrice: line.salesPrice,
      finalSalesPrice: line.salesPrice,
      preTaxAmountBeforeDiscount: line.preTaxAmount,
      itemDiscountAmount: line.itemDiscountAmount,
      saleDiscountAmount: line.saleDiscountAmount,
      taxableAmount: line.taxableAmount,
      taxAmount: line.taxAmount,
      totalAmount: line.lineTotal,
      savingsAmount: line.itemDiscountAmount + line.saleDiscountAmount,
      taxRatePercent: line.taxRatePercent,
      isPriceIncludingTax: line.taxIncluded,
      hasPriceMismatch: false,
      returnedQuantity: 0,
      returnableQuantity: line.quantity,
      // A queued line knows only the unit its document prints, and the precision its quantity was sold in.
      uom: line.documentUnit ?? null,
      quantityScale: quantityScaleOf(line.quantity),
      returnStatus: 'None',
      hsnCode: line.hsnCode,
    })),
    returns: [],
    warnings: [],
  };
}
