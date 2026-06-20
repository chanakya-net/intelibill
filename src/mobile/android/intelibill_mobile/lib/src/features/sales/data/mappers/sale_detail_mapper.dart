import 'package:intelibill_mobile/src/features/sales/data/dto/sale_detail_dto.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';

class SaleDetailMapper {
  const SaleDetailMapper._();

  static SaleDetail toDomain(SaleDetailDto dto) {
    return SaleDetail(
      saleId: dto.saleId,
      invoiceNumber: dto.invoiceNumber,
      customerId: dto.customerId,
      customerName: dto.customerName,
      customerPhone: dto.customerPhone,
      paymentMethod: dto.paymentMethod,
      soldAt: dto.soldAt.toLocal(),
      paidAmount: dto.paidAmount,
      dueAmount: dto.dueAmount,
      totalBeforeDiscount: dto.totalBeforeDiscount,
      totalDiscountAmount: dto.totalDiscountAmount,
      totalAmount: dto.totalAmount,
      totalTaxAmount: dto.totalTaxAmount,
      creditNoteAppliedAmount: dto.creditNoteAppliedAmount,
      items: dto.items.map(_itemToDomain).toList(),
      warnings: dto.warnings,
      returns: dto.returns.map(_returnToDomain).toList(),
      creditNoteRedemptions: dto.creditNoteRedemptions
          .map(_redemptionToDomain)
          .toList(),
      settlements: dto.settlements.map(_settlementToDomain).toList(),
      discounts: dto.discounts.map(_discountToDomain).toList(),
      status: dto.status,
      refundAmount: dto.refundAmount,
      dueReductionAmount: dto.dueReductionAmount,
    );
  }

  static SaleDetailSettlement _settlementToDomain(
    SaleDetailSettlementDto dto,
  ) {
    return SaleDetailSettlement(
      settlementId: dto.settlementId,
      method: dto.method,
      amount: dto.amount,
      settledAt: dto.settledAt.toLocal(),
    );
  }

  static SaleDetailDiscount _discountToDomain(SaleDetailDiscountDto dto) {
    return SaleDetailDiscount(
      discountId: dto.discountId,
      type: dto.type,
      value: dto.value,
      amount: dto.amount,
    );
  }

  static SaleDetailItem _itemToDomain(SaleDetailItemDto dto) {
    return SaleDetailItem(
      saleItemId: dto.saleItemId,
      lineType: dto.lineType,
      itemId: dto.itemId,
      inventoryBatchId: dto.inventoryBatchId,
      serviceId: dto.serviceId,
      lineCode: dto.lineCode,
      itemName: dto.itemName,
      quantity: dto.quantity,
      salesPrice: dto.salesPrice,
      originalSalesPrice: dto.originalSalesPrice,
      finalSalesPrice: dto.finalSalesPrice,
      preTaxAmountBeforeDiscount: dto.preTaxAmountBeforeDiscount,
      itemDiscountAmount: dto.itemDiscountAmount,
      saleDiscountAmount: dto.saleDiscountAmount,
      taxableAmount: dto.taxableAmount,
      taxAmount: dto.taxAmount,
      totalAmount: dto.totalAmount,
      savingsAmount: dto.savingsAmount,
      taxRatePercent: dto.taxRatePercent,
      isPriceIncludingTax: dto.isPriceIncludingTax,
      hasPriceMismatch: dto.hasPriceMismatch,
      hsnCode: dto.hsnCode,
      returnedQuantity: dto.returnedQuantity,
      returnableQuantity: dto.returnableQuantity,
      returnStatus: dto.returnStatus,
    );
  }

  static SaleDetailReturn _returnToDomain(SaleDetailReturnDto dto) {
    return SaleDetailReturn(
      saleReturnId: dto.saleReturnId,
      returnNumber: dto.returnNumber,
      processedAt: dto.processedAt.toLocal(),
      processedBy: dto.processedBy,
      notes: dto.notes,
      totalRefundAmount: dto.totalRefundAmount,
      dueReductionAmount: dto.dueReductionAmount,
      payoutAmount: dto.payoutAmount,
      payoutDestination: dto.payoutDestination,
      totalTaxableAmount: dto.totalTaxableAmount,
      totalTaxAmount: dto.totalTaxAmount,
      creditNote: dto.creditNote != null
          ? _returnCreditNoteToDomain(dto.creditNote!)
          : null,
      items: dto.items.map(_returnItemToDomain).toList(),
    );
  }

  static SaleDetailReturnItem _returnItemToDomain(SaleDetailReturnItemDto dto) {
    return SaleDetailReturnItem(
      saleReturnItemId: dto.saleReturnItemId,
      saleItemId: dto.saleItemId,
      quantity: dto.quantity,
      condition: dto.condition,
      approvedRefundAmount: dto.approvedRefundAmount,
      taxableAmount: dto.taxableAmount,
      taxAmount: dto.taxAmount,
      notes: dto.notes,
    );
  }

  static SaleDetailReturnCreditNote _returnCreditNoteToDomain(
    SaleDetailReturnCreditNoteDto dto,
  ) {
    return SaleDetailReturnCreditNote(
      creditNoteId: dto.creditNoteId,
      code: dto.code,
      originalAmount: dto.originalAmount,
      availableBalance: dto.availableBalance,
      expiresAt: dto.expiresAt?.toLocal(),
      reason: dto.reason,
    );
  }

  static SaleDetailCreditNoteRedemption _redemptionToDomain(
    SaleDetailCreditNoteRedemptionDto dto,
  ) {
    return SaleDetailCreditNoteRedemption(
      creditNoteId: dto.creditNoteId,
      code: dto.code,
      appliedAmount: dto.appliedAmount,
    );
  }
}
