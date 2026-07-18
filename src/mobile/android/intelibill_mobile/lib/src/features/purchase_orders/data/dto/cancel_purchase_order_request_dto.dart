import 'package:freezed_annotation/freezed_annotation.dart';

part 'cancel_purchase_order_request_dto.freezed.dart';
part 'cancel_purchase_order_request_dto.g.dart';

@freezed
sealed class CancelPurchaseOrderRequestDto
    with _$CancelPurchaseOrderRequestDto {
  const factory CancelPurchaseOrderRequestDto({
    required String reason,
  }) = _CancelPurchaseOrderRequestDto;

  factory CancelPurchaseOrderRequestDto.fromJson(
    Map<String, dynamic> json,
  ) => _$CancelPurchaseOrderRequestDtoFromJson(json);
}
