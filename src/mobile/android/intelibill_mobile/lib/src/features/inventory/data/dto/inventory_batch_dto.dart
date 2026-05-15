import 'package:freezed_annotation/freezed_annotation.dart';

part 'inventory_batch_dto.freezed.dart';
part 'inventory_batch_dto.g.dart';

@freezed
sealed class InventoryBatchDto with _$InventoryBatchDto {
  const factory InventoryBatchDto({
    @JsonKey(name: 'id') required String id,
    @JsonKey(name: 'itemId') required String itemId,
    @JsonKey(name: 'itemName') required String itemName,
    @JsonKey(name: 'barcode') required String barcode,
    @JsonKey(name: 'itemUom') @Default('') String itemUom,
    @JsonKey(name: 'batchNumber') required String batchNumber,
    @JsonKey(name: 'quantity') required double quantity,
    @JsonKey(name: 'costPrice') required double costPrice,
    @JsonKey(name: 'mrp') required double mrp,
    @JsonKey(name: 'salesPrice') required double salesPrice,
    @JsonKey(name: 'taxRatePercent') @Default(0.0) double taxRatePercent,
    @JsonKey(name: 'taxIncluded') @Default(false) bool taxIncluded,
    @JsonKey(name: 'expiryDate') DateTime? expiryDate,
    @JsonKey(name: 'manufacturingDate') DateTime? manufacturingDate,
    @JsonKey(name: 'referenceNumber') String? referenceNumber,
    @JsonKey(name: 'notes') String? notes,
    @JsonKey(name: 'supplierId') String? supplierId,
    @JsonKey(name: 'supplierName') String? supplierName,
    @JsonKey(name: 'isVoided') @Default(false) bool isVoided,
    @JsonKey(name: 'createdAt') required DateTime createdAt,
  }) = _InventoryBatchDto;

  factory InventoryBatchDto.fromJson(Map<String, dynamic> json) =>
      _$InventoryBatchDtoFromJson(json);
}
