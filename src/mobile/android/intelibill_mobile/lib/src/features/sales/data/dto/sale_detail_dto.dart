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
    @JsonKey(name: 'discounts')
    @Default([])
    List<SaleDetailDiscountDto> discounts,
    @JsonKey(name: 'returns') @Default([]) List<SaleDetailReturnDto> returns,
    @JsonKey(name: 'creditNoteRedemptions')
    @Default([])
    List<SaleDetailRedemptionDto> redemptions,
    @JsonKey(name: 'warnings') @Default([]) List<String> warnings,
    @JsonKey(name: 'paidAmount') required double paidAmount,
    @JsonKey(name: 'dueAmount') required double dueAmount,
    @JsonKey(name: 'totalBeforeDiscount') required double totalBeforeDiscount,
    @JsonKey(name: 'totalDiscountAmount') required double totalDiscountAmount,
    @JsonKey(name: 'totalAmount') required double totalAmount,
    @JsonKey(name: 'totalTaxAmount') required double totalTaxAmount,
    @JsonKey(name: 'creditNoteAppliedAmount')
    @Default(0.0)
    double creditNoteAppliedAmount,
    @JsonKey(name: 'status') String? status,
    @JsonKey(name: 'refundAmount') @Default(0.0) double refundAmount,
    @JsonKey(name: 'dueReductionAmount')
    @Default(0.0)
    double dueReductionAmount,
  }) = _SaleDetailDto;

  factory SaleDetailDto.fromJson(Map<String, dynamic> json) {
    final normalized = Map<String, dynamic>.of(json);
    // `refundAmount` is a first-class totals field and intentionally remains
    // independent from credit-note redemption entries shown in the dedicated
    // redemptions section.
    normalized['refundAmount'] = _toDouble(normalized['refundAmount']) ?? 0.0;
    normalized['warnings'] ??= const [];
    return _$SaleDetailDtoFromJson(normalized);
  }

  static double? _toDouble(Object? value) {
    if (value == null) return null;
    if (value is num) return value.toDouble();
    return double.tryParse(value.toString());
  }
}

@freezed
sealed class SaleDetailItemDto with _$SaleDetailItemDto {
  const factory SaleDetailItemDto({
    @JsonKey(name: 'saleItemId') required String itemId,
    @JsonKey(name: 'itemName') required String name,
    @JsonKey(name: 'quantity') required double quantity,
    @JsonKey(name: 'salesPrice') required double rate,
    @JsonKey(name: 'taxRatePercent') required double tax,
    @JsonKey(name: 'totalAmount') required double total,
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
    @JsonKey(name: 'saleReturnId') required String returnId,
    @JsonKey(name: 'returnNumber') required String returnNumber,
    @JsonKey(name: 'items') @Default([]) List<SaleDetailReturnItemDto> items,
    @JsonKey(name: 'totalRefundAmount') required double amount,
    @JsonKey(name: 'processedAt') required DateTime returnedAt,
  }) = _SaleDetailReturnDto;

  factory SaleDetailReturnDto.fromJson(Map<String, dynamic> json) =>
      _$SaleDetailReturnDtoFromJson(json);
}

@freezed
sealed class SaleDetailReturnItemDto with _$SaleDetailReturnItemDto {
  const factory SaleDetailReturnItemDto({
    @JsonKey(name: 'saleItemId') required String itemId,
    @JsonKey(name: 'itemName') String? itemName,
    @JsonKey(name: 'quantity') required double quantity,
    @JsonKey(name: 'approvedRefundAmount') required double amount,
  }) = _SaleDetailReturnItemDto;

  factory SaleDetailReturnItemDto.fromJson(Map<String, dynamic> json) =>
      _$SaleDetailReturnItemDtoFromJson(json);
}

@freezed
sealed class SaleDetailRedemptionDto with _$SaleDetailRedemptionDto {
  const factory SaleDetailRedemptionDto({
    @JsonKey(name: 'creditNoteId') required String redemptionId,
    @JsonKey(name: 'code') required String code,
    @JsonKey(name: 'appliedAmount') required double amount,
  }) = _SaleDetailRedemptionDto;

  factory SaleDetailRedemptionDto.fromJson(Map<String, dynamic> json) =>
      _$SaleDetailRedemptionDtoFromJson(json);
}
