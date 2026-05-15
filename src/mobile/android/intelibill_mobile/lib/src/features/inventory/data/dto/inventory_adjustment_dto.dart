import 'package:freezed_annotation/freezed_annotation.dart';

part 'inventory_adjustment_dto.freezed.dart';
part 'inventory_adjustment_dto.g.dart';

@freezed
sealed class InventoryAdjustmentDto with _$InventoryAdjustmentDto {
  const factory InventoryAdjustmentDto({
    @JsonKey(name: 'adjustmentId') required String adjustmentId,
    @JsonKey(name: 'batchId') required String batchId,
    @JsonKey(name: 'itemId') required String itemId,
    @JsonKey(name: 'itemName') required String itemName,
    @JsonKey(name: 'batchNumber') required String batchNumber,
    @JsonKey(name: 'direction') required String direction,
    @JsonKey(name: 'reason') required String reason,
    @JsonKey(name: 'quantity') required double quantity,
    @JsonKey(name: 'costImpact') required double costImpact,
    @JsonKey(name: 'notes') String? notes,
    @JsonKey(name: 'performedAt') required DateTime performedAt,
    @JsonKey(name: 'performedByDisplayName')
    required String performedByDisplayName,
    @JsonKey(name: 'isVoided') @Default(false) bool isVoided,
  }) = _InventoryAdjustmentDto;

  factory InventoryAdjustmentDto.fromJson(Map<String, dynamic> json) =>
      _$InventoryAdjustmentDtoFromJson(json);
}
