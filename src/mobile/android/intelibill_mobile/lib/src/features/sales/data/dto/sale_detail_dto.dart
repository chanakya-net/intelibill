import 'package:freezed_annotation/freezed_annotation.dart';
import 'package:intelibill_mobile/src/core/utils/payment_method_wire.dart';

part 'sale_detail_dto.freezed.dart';
part 'sale_detail_dto.g.dart';

@freezed
sealed class SaleDetailCreditNoteRedemptionDto
    with _$SaleDetailCreditNoteRedemptionDto {
  const factory SaleDetailCreditNoteRedemptionDto({
    @JsonKey(name: 'creditNoteId') required String creditNoteId,
    @JsonKey(name: 'code') required String code,
    @JsonKey(name: 'appliedAmount') required double appliedAmount,
  }) = _SaleDetailCreditNoteRedemptionDto;

  factory SaleDetailCreditNoteRedemptionDto.fromJson(
    Map<String, dynamic> json,
  ) => _$SaleDetailCreditNoteRedemptionDtoFromJson(json);
}

@freezed
sealed class SaleDetailReturnCreditNoteDto
    with _$SaleDetailReturnCreditNoteDto {
  const factory SaleDetailReturnCreditNoteDto({
    @JsonKey(name: 'creditNoteId') required String creditNoteId,
    @JsonKey(name: 'code') required String code,
    @JsonKey(name: 'originalAmount') required double originalAmount,
    @JsonKey(name: 'availableBalance') required double availableBalance,
    @JsonKey(name: 'expiresAt') DateTime? expiresAt,
    @JsonKey(name: 'reason') required String reason,
  }) = _SaleDetailReturnCreditNoteDto;

  factory SaleDetailReturnCreditNoteDto.fromJson(Map<String, dynamic> json) =>
      _$SaleDetailReturnCreditNoteDtoFromJson(json);
}

@freezed
sealed class SaleDetailReturnItemDto with _$SaleDetailReturnItemDto {
  const factory SaleDetailReturnItemDto({
    @JsonKey(name: 'saleReturnItemId') required String saleReturnItemId,
    @JsonKey(name: 'saleItemId') required String saleItemId,
    @JsonKey(name: 'quantity') required double quantity,
    @JsonKey(name: 'condition') String? condition,
    @JsonKey(name: 'approvedRefundAmount') required double approvedRefundAmount,
    @JsonKey(name: 'taxableAmount') required double taxableAmount,
    @JsonKey(name: 'taxAmount') required double taxAmount,
    @JsonKey(name: 'notes') String? notes,
  }) = _SaleDetailReturnItemDto;

  factory SaleDetailReturnItemDto.fromJson(Map<String, dynamic> json) =>
      _$SaleDetailReturnItemDtoFromJson(json);
}

@freezed
sealed class SaleDetailReturnDto with _$SaleDetailReturnDto {
  const factory SaleDetailReturnDto({
    @JsonKey(name: 'saleReturnId') required String saleReturnId,
    @JsonKey(name: 'returnNumber') required String returnNumber,
    @JsonKey(name: 'processedAt') required DateTime processedAt,
    @JsonKey(name: 'processedBy') required String processedBy,
    @JsonKey(name: 'notes') String? notes,
    @JsonKey(name: 'totalRefundAmount') required double totalRefundAmount,
    @JsonKey(name: 'dueReductionAmount') required double dueReductionAmount,
    @JsonKey(name: 'payoutAmount') required double payoutAmount,
    @JsonKey(name: 'payoutDestination') String? payoutDestination,
    @JsonKey(name: 'totalTaxableAmount') required double totalTaxableAmount,
    @JsonKey(name: 'totalTaxAmount') required double totalTaxAmount,
    @JsonKey(name: 'creditNote') SaleDetailReturnCreditNoteDto? creditNote,
    @JsonKey(name: 'items') @Default([]) List<SaleDetailReturnItemDto> items,
  }) = _SaleDetailReturnDto;

  factory SaleDetailReturnDto.fromJson(Map<String, dynamic> json) =>
      _$SaleDetailReturnDtoFromJson(json);
}

@freezed
sealed class SaleDetailItemDto with _$SaleDetailItemDto {
  const factory SaleDetailItemDto({
    @JsonKey(name: 'saleItemId') required String saleItemId,
    @JsonKey(name: 'lineType') required String lineType,
    @JsonKey(name: 'itemId') String? itemId,
    @JsonKey(name: 'inventoryBatchId') String? inventoryBatchId,
    @JsonKey(name: 'serviceId') String? serviceId,
    @JsonKey(name: 'lineCode') required String lineCode,
    @JsonKey(name: 'itemName') required String itemName,
    @JsonKey(name: 'quantity') required double quantity,
    @JsonKey(name: 'salesPrice') required double salesPrice,
    @JsonKey(name: 'originalSalesPrice')
    @Default(0.0)
    double originalSalesPrice,
    @JsonKey(name: 'finalSalesPrice') @Default(0.0) double finalSalesPrice,
    @JsonKey(name: 'preTaxAmountBeforeDiscount')
    @Default(0.0)
    double preTaxAmountBeforeDiscount,
    @JsonKey(name: 'itemDiscountAmount')
    @Default(0.0)
    double itemDiscountAmount,
    @JsonKey(name: 'saleDiscountAmount')
    @Default(0.0)
    double saleDiscountAmount,
    @JsonKey(name: 'taxableAmount') @Default(0.0) double taxableAmount,
    @JsonKey(name: 'taxAmount') @Default(0.0) double taxAmount,
    @JsonKey(name: 'totalAmount') @Default(0.0) double totalAmount,
    @JsonKey(name: 'savingsAmount') @Default(0.0) double savingsAmount,
    @JsonKey(name: 'taxRatePercent') required double taxRatePercent,
    @JsonKey(name: 'isPriceIncludingTax') required bool isPriceIncludingTax,
    @JsonKey(name: 'hasPriceMismatch') @Default(false) bool hasPriceMismatch,
    @JsonKey(name: 'hsnCode') String? hsnCode,
    @JsonKey(name: 'returnedQuantity') @Default(0.0) double returnedQuantity,
    @JsonKey(name: 'returnableQuantity')
    @Default(0.0)
    double returnableQuantity,
    @JsonKey(name: 'returnStatus') @Default('NotReturned') String returnStatus,
  }) = _SaleDetailItemDto;

  factory SaleDetailItemDto.fromJson(Map<String, dynamic> json) =>
      _$SaleDetailItemDtoFromJson(json);
}

@freezed
sealed class SaleDetailDto with _$SaleDetailDto {
  const factory SaleDetailDto({
    @JsonKey(name: 'saleId') required String saleId,
    @JsonKey(name: 'invoiceNumber') required String invoiceNumber,
    @JsonKey(name: 'customerId') String? customerId,
    @JsonKey(name: 'customerName') String? customerName,
    @JsonKey(name: 'customerPhone') String? customerPhone,
    @JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson)
    required int paymentMethod,
    @JsonKey(name: 'soldAt') required DateTime soldAt,
    @JsonKey(name: 'paidAmount') required double paidAmount,
    @JsonKey(name: 'dueAmount') required double dueAmount,
    @JsonKey(name: 'totalBeforeDiscount') required double totalBeforeDiscount,
    @JsonKey(name: 'totalDiscountAmount') required double totalDiscountAmount,
    @JsonKey(name: 'totalAmount') required double totalAmount,
    @JsonKey(name: 'totalTaxAmount') required double totalTaxAmount,
    @JsonKey(name: 'creditNoteAppliedAmount')
    @Default(0.0)
    double creditNoteAppliedAmount,
    @JsonKey(name: 'items') @Default([]) List<SaleDetailItemDto> items,
    @JsonKey(name: 'warnings') @Default([]) List<String> warnings,
    @JsonKey(name: 'returns') @Default([]) List<SaleDetailReturnDto> returns,
    @JsonKey(name: 'creditNoteRedemptions')
    @Default([])
    List<SaleDetailCreditNoteRedemptionDto> creditNoteRedemptions,
  }) = _SaleDetailDto;

  factory SaleDetailDto.fromJson(Map<String, dynamic> json) =>
      _$SaleDetailDtoFromJson(json);
}
