import 'package:freezed_annotation/freezed_annotation.dart';

part 'close_purchase_order_request_dto.freezed.dart';
part 'close_purchase_order_request_dto.g.dart';

@freezed
sealed class ClosePurchaseOrderRequestDto with _$ClosePurchaseOrderRequestDto {
  const factory ClosePurchaseOrderRequestDto({required String reason}) =
      _ClosePurchaseOrderRequestDto;

  factory ClosePurchaseOrderRequestDto.fromJson(Map<String, dynamic> json) =>
      _$ClosePurchaseOrderRequestDtoFromJson(json);
}
