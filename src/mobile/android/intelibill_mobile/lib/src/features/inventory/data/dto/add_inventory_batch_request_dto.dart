import 'package:freezed_annotation/freezed_annotation.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/add_inventory_batch_row_dto.dart';

part 'add_inventory_batch_request_dto.freezed.dart';
part 'add_inventory_batch_request_dto.g.dart';

@freezed
sealed class AddInventoryBatchRequestDto with _$AddInventoryBatchRequestDto {
  const factory AddInventoryBatchRequestDto({
    @JsonKey(name: 'items') required List<AddInventoryBatchRowDto> items,
  }) = _AddInventoryBatchRequestDto;

  factory AddInventoryBatchRequestDto.fromJson(Map<String, dynamic> json) =>
      _$AddInventoryBatchRequestDtoFromJson(json);
}
