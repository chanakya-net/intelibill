import 'package:freezed_annotation/freezed_annotation.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/inventory_adjustment_dto.dart';

part 'inventory_adjustment_history_response_dto.freezed.dart';
part 'inventory_adjustment_history_response_dto.g.dart';

@freezed
sealed class InventoryAdjustmentHistoryResponseDto
    with _$InventoryAdjustmentHistoryResponseDto {
  const factory InventoryAdjustmentHistoryResponseDto({
    @JsonKey(name: 'items') @Default([]) List<InventoryAdjustmentDto> items,
    @JsonKey(name: 'totalCount') required int totalCount,
    @JsonKey(name: 'pageNumber') required int pageNumber,
    @JsonKey(name: 'pageSize') required int pageSize,
  }) = _InventoryAdjustmentHistoryResponseDto;

  factory InventoryAdjustmentHistoryResponseDto.fromJson(
    Map<String, dynamic> json,
  ) => _$InventoryAdjustmentHistoryResponseDtoFromJson(json);
}
