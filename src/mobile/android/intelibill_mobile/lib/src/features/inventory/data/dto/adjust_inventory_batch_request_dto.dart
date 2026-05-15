import 'package:freezed_annotation/freezed_annotation.dart';

part 'adjust_inventory_batch_request_dto.freezed.dart';
part 'adjust_inventory_batch_request_dto.g.dart';

@freezed
sealed class AdjustInventoryBatchRequestDto
    with _$AdjustInventoryBatchRequestDto {
  const factory AdjustInventoryBatchRequestDto({
    @JsonKey(name: 'direction') required String direction,
    @JsonKey(name: 'reason') required String reason,
    @JsonKey(name: 'quantity') required double quantity,
    @JsonKey(name: 'performedAt') String? performedAt,
    @JsonKey(name: 'notes') String? notes,
  }) = _AdjustInventoryBatchRequestDto;

  factory AdjustInventoryBatchRequestDto.fromJson(Map<String, dynamic> json) =>
      _$AdjustInventoryBatchRequestDtoFromJson(json);
}
