import 'package:freezed_annotation/freezed_annotation.dart';
import 'package:intelibill_mobile/src/core/utils/payment_method_wire.dart';

part 'sale_detail_dto.freezed.dart';
part 'sale_detail_dto.g.dart';

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
    @JsonKey(name: 'items') @Default([]) List<SaleDetailItemDto> items,
    @JsonKey(name: 'settlements')
    @Default([])
    List<SaleDetailSettlementDto> settlements,
    @JsonKey(name: 'discounts') @Default([]) List<SaleDetailDiscountDto> discounts,
    @JsonKey(name: 'returns') @Default([]) List<SaleDetailReturnDto> returns,
    @JsonKey(name: 'redemptions')
    @Default([])
    List<SaleDetailRedemptionDto> redemptions,
    @JsonKey(name: 'warnings') @Default([]) List<SaleDetailWarningDto> warnings,
    @JsonKey(name: 'paidAmount') required double paidAmount,
    @JsonKey(name: 'dueAmount') required double dueAmount,
    @JsonKey(name: 'totalBeforeDiscount') required double totalBeforeDiscount,
    @JsonKey(name: 'totalDiscountAmount') required double totalDiscountAmount,
    @JsonKey(name: 'totalAmount') required double totalAmount,
    @JsonKey(name: 'totalTaxAmount') required double totalTaxAmount,
    @JsonKey(name: 'status') required String status,
    @JsonKey(name: 'refundAmount') @Default(0.0) double refundAmount,
    @JsonKey(name: 'dueReductionAmount')
    @Default(0.0)
    double dueReductionAmount,
  }) = _SaleDetailDto;

  factory SaleDetailDto.fromJson(Map<String, dynamic> json) =>
      _$SaleDetailDtoFromJson(json);
}

@freezed
sealed class SaleDetailItemDto with _$SaleDetailItemDto {
  const factory SaleDetailItemDto({
    @JsonKey(name: 'itemId') required String itemId,
    @JsonKey(name: 'name') required String name,
    @JsonKey(name: 'quantity') required double quantity,
    @JsonKey(name: 'rate') required double rate,
    @JsonKey(name: 'tax') required double tax,
    @JsonKey(name: 'total') required double total,
  }) = _SaleDetailItemDto;

  factory SaleDetailItemDto.fromJson(Map<String, dynamic> json) =>
      _$SaleDetailItemDtoFromJson(json);
}

@freezed
sealed class SaleDetailSettlementDto with _$SaleDetailSettlementDto {
  const factory SaleDetailSettlementDto({
    @JsonKey(name: 'settlementId') required String settlementId,
    @JsonKey(name: 'method') required String method,
    @JsonKey(name: 'amount') required double amount,
    @JsonKey(name: 'settledAt') required DateTime settledAt,
  }) = _SaleDetailSettlementDto;

  factory SaleDetailSettlementDto.fromJson(Map<String, dynamic> json) =>
      _$SaleDetailSettlementDtoFromJson(json);
}

@freezed
sealed class SaleDetailDiscountDto with _$SaleDetailDiscountDto {
  const factory SaleDetailDiscountDto({
    @JsonKey(name: 'discountId') required String discountId,
    @JsonKey(name: 'type') required String type,
    @JsonKey(name: 'value') required String value,
    @JsonKey(name: 'amount') required double amount,
  }) = _SaleDetailDiscountDto;

  factory SaleDetailDiscountDto.fromJson(Map<String, dynamic> json) =>
      _$SaleDetailDiscountDtoFromJson(json);
}

@freezed
sealed class SaleDetailReturnDto with _$SaleDetailReturnDto {
  const factory SaleDetailReturnDto({
    @JsonKey(name: 'returnId') required String returnId,
    @JsonKey(name: 'returnNumber') required String returnNumber,
    @JsonKey(name: 'items') @Default([]) List<SaleDetailReturnItemDto> items,
    @JsonKey(name: 'amount') required double amount,
    @JsonKey(name: 'returnedAt') required DateTime returnedAt,
  }) = _SaleDetailReturnDto;

  factory SaleDetailReturnDto.fromJson(Map<String, dynamic> json) =>
      _$SaleDetailReturnDtoFromJson(json);
}

@freezed
sealed class SaleDetailReturnItemDto with _$SaleDetailReturnItemDto {
  const factory SaleDetailReturnItemDto({
    @JsonKey(name: 'itemId') required String itemId,
    @JsonKey(name: 'quantity') required double quantity,
    @JsonKey(name: 'amount') required double amount,
  }) = _SaleDetailReturnItemDto;

  factory SaleDetailReturnItemDto.fromJson(Map<String, dynamic> json) =>
      _$SaleDetailReturnItemDtoFromJson(json);
}

@freezed
sealed class SaleDetailRedemptionDto with _$SaleDetailRedemptionDto {
  const factory SaleDetailRedemptionDto({
    @JsonKey(name: 'redemptionId') required String redemptionId,
    @JsonKey(name: 'type') required String type,
    @JsonKey(name: 'amount') required double amount,
    @JsonKey(name: 'redeemedAt') required DateTime redeemedAt,
  }) = _SaleDetailRedemptionDto;

  factory SaleDetailRedemptionDto.fromJson(Map<String, dynamic> json) =>
      _$SaleDetailRedemptionDtoFromJson(json);
}

@freezed
sealed class SaleDetailWarningDto with _$SaleDetailWarningDto {
  const factory SaleDetailWarningDto({
    @JsonKey(name: 'warningId') required String warningId,
    @JsonKey(name: 'type') required String type,
    @JsonKey(name: 'message') required String message,
  }) = _SaleDetailWarningDto;

  factory SaleDetailWarningDto.fromJson(Map<String, dynamic> json) =>
      _$SaleDetailWarningDtoFromJson(json);
}
