import 'package:freezed_annotation/freezed_annotation.dart';

part 'add_inventory_batch_response_dto.freezed.dart';
part 'add_inventory_batch_response_dto.g.dart';

@freezed
sealed class AddInventoryBatchResponseDto with _$AddInventoryBatchResponseDto {
  const factory AddInventoryBatchResponseDto({
    @JsonKey(name: 'requestedCount') required int requestedCount,
    @JsonKey(name: 'successCount') required int successCount,
    @JsonKey(name: 'failedCount') required int failedCount,
    @JsonKey(name: 'succeeded')
    @Default([])
    List<AddInventoryBatchSucceededRowDto> succeeded,
    @JsonKey(name: 'failed')
    @Default([])
    List<AddInventoryBatchFailedRowDto> failed,
  }) = _AddInventoryBatchResponseDto;

  factory AddInventoryBatchResponseDto.fromJson(Map<String, dynamic> json) =>
      _$AddInventoryBatchResponseDtoFromJson(json);
}

@freezed
sealed class AddInventoryBatchSucceededRowDto
    with _$AddInventoryBatchSucceededRowDto {
  const factory AddInventoryBatchSucceededRowDto({
    @JsonKey(name: 'clientRowId') required String clientRowId,
    @JsonKey(name: 'result') required AddInventoryResultDto result,
  }) = _AddInventoryBatchSucceededRowDto;

  factory AddInventoryBatchSucceededRowDto.fromJson(
    Map<String, dynamic> json,
  ) => _$AddInventoryBatchSucceededRowDtoFromJson(json);
}

@freezed
sealed class AddInventoryBatchFailedRowDto
    with _$AddInventoryBatchFailedRowDto {
  const factory AddInventoryBatchFailedRowDto({
    @JsonKey(name: 'clientRowId') required String clientRowId,
    @JsonKey(name: 'itemName') required String itemName,
    @JsonKey(name: 'barcode') required String barcode,
    @JsonKey(name: 'errors')
    @Default([])
    List<AddInventoryBatchRowErrorDto> errors,
  }) = _AddInventoryBatchFailedRowDto;

  factory AddInventoryBatchFailedRowDto.fromJson(Map<String, dynamic> json) =>
      _$AddInventoryBatchFailedRowDtoFromJson(json);
}

@freezed
sealed class AddInventoryResultDto with _$AddInventoryResultDto {
  const factory AddInventoryResultDto({
    @JsonKey(name: 'itemId') required String itemId,
    @JsonKey(name: 'itemName') required String itemName,
    @JsonKey(name: 'barcode') required String barcode,
    @JsonKey(name: 'inventoryBatchId') required String inventoryBatchId,
    @JsonKey(name: 'batchNumber') required String batchNumber,
    @JsonKey(name: 'batchQuantity') required double batchQuantity,
    @JsonKey(name: 'totalQuantity') required double totalQuantity,
    @JsonKey(name: 'supplierId') String? supplierId,
    @JsonKey(name: 'stockTransactionId') required String stockTransactionId,
    @JsonKey(name: 'performedAt') required DateTime performedAt,
  }) = _AddInventoryResultDto;

  factory AddInventoryResultDto.fromJson(Map<String, dynamic> json) =>
      _$AddInventoryResultDtoFromJson(json);
}

@freezed
sealed class AddInventoryBatchRowErrorDto with _$AddInventoryBatchRowErrorDto {
  const factory AddInventoryBatchRowErrorDto({
    @JsonKey(name: 'code') required String code,
    @JsonKey(name: 'description') required String description,
  }) = _AddInventoryBatchRowErrorDto;

  factory AddInventoryBatchRowErrorDto.fromJson(Map<String, dynamic> json) =>
      _$AddInventoryBatchRowErrorDtoFromJson(json);
}
