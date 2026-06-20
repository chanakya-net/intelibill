import 'package:intelibill_mobile/src/features/sales/data/dto/sale_detail_dto.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';

extension SaleDetailDtoX on SaleDetailDto {
  SaleDetail toDomain() => SaleDetail(
    saleId: saleId,
    invoiceNumber: invoiceNumber,
    customerId: customerId,
    customerName: customerName,
    customerPhone: customerPhone,
    paymentMethod: paymentMethod,
    soldAt: soldAt,
    items: items.map((e) => e.toDomain()).toList(),
    settlements: settlements.map((e) => e.toDomain()).toList(),
    discounts: discounts.map((e) => e.toDomain()).toList(),
    returns: returns.map((e) => e.toDomain()).toList(),
    redemptions: redemptions.map((e) => e.toDomain()).toList(),
    warnings: warnings.map((e) => e.toDomain()).toList(),
    paidAmount: paidAmount,
    dueAmount: dueAmount,
    totalBeforeDiscount: totalBeforeDiscount,
    totalDiscountAmount: totalDiscountAmount,
    totalAmount: totalAmount,
    totalTaxAmount: totalTaxAmount,
    status: status,
    refundAmount: refundAmount,
    dueReductionAmount: dueReductionAmount,
  );
}

extension SaleDetailItemDtoX on SaleDetailItemDto {
  SaleDetailItem toDomain() => SaleDetailItem(
    itemId: itemId,
    name: name,
    quantity: quantity,
    rate: rate,
    tax: tax,
    total: total,
  );
}

extension SaleDetailSettlementDtoX on SaleDetailSettlementDto {
  SaleDetailSettlement toDomain() => SaleDetailSettlement(
    settlementId: settlementId,
    method: method,
    amount: amount,
    settledAt: settledAt,
  );
}

extension SaleDetailDiscountDtoX on SaleDetailDiscountDto {
  SaleDetailDiscount toDomain() => SaleDetailDiscount(
    discountId: discountId,
    type: type,
    value: value,
    amount: amount,
  );
}

extension SaleDetailReturnDtoX on SaleDetailReturnDto {
  SaleDetailReturn toDomain() => SaleDetailReturn(
    returnId: returnId,
    returnNumber: returnNumber,
    items: items.map((e) => e.toDomain()).toList(),
    amount: amount,
    returnedAt: returnedAt,
  );
}

extension SaleDetailReturnItemDtoX on SaleDetailReturnItemDto {
  SaleDetailReturnItem toDomain() => SaleDetailReturnItem(
    itemId: itemId,
    quantity: quantity,
    amount: amount,
  );
}

extension SaleDetailRedemptionDtoX on SaleDetailRedemptionDto {
  SaleDetailRedemption toDomain() => SaleDetailRedemption(
    redemptionId: redemptionId,
    type: type,
    amount: amount,
    redeemedAt: redeemedAt,
  );
}

extension SaleDetailWarningDtoX on SaleDetailWarningDto {
  SaleDetailWarning toDomain() => SaleDetailWarning(
    warningId: warningId,
    type: type,
    message: message,
  );
}
