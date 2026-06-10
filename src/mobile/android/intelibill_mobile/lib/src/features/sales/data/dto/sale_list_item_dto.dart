import 'package:freezed_annotation/freezed_annotation.dart';
import 'package:intelibill_mobile/src/core/utils/payment_method_wire.dart';

part 'sale_list_item_dto.freezed.dart';
part 'sale_list_item_dto.g.dart';

@freezed
sealed class SaleListItemDto with _$SaleListItemDto {
  const factory SaleListItemDto({
    @JsonKey(name: 'saleId') required String saleId,
    @JsonKey(name: 'invoiceNumber') required String invoiceNumber,
    @JsonKey(name: 'customerId') String? customerId,
    @JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson)
    required int paymentMethod,
    @JsonKey(name: 'soldAt') required DateTime soldAt,
    @JsonKey(name: 'paidAmount') required double paidAmount,
    @JsonKey(name: 'dueAmount') required double dueAmount,
    @JsonKey(name: 'totalBeforeDiscount') required double totalBeforeDiscount,
    @JsonKey(name: 'totalDiscountAmount') required double totalDiscountAmount,
    @JsonKey(name: 'totalAmount') required double totalAmount,
    @JsonKey(name: 'totalTaxAmount') required double totalTaxAmount,
    @JsonKey(name: 'customerName') String? customerName,
    @JsonKey(name: 'customerPhone') String? customerPhone,
    @JsonKey(name: 'itemCount') required int itemCount,
    @JsonKey(name: 'returnNumbers') @Default([]) List<String> returnNumbers,
    @JsonKey(name: 'status') required String status,
    @JsonKey(name: 'refundAmount') @Default(0.0) double refundAmount,
    @JsonKey(name: 'dueReductionAmount')
    @Default(0.0)
    double dueReductionAmount,
  }) = _SaleListItemDto;

  factory SaleListItemDto.fromJson(Map<String, dynamic> json) =>
      _$SaleListItemDtoFromJson(json);
}
