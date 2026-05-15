import 'package:freezed_annotation/freezed_annotation.dart';

part 'add_inventory_batch_row_dto.freezed.dart';
part 'add_inventory_batch_row_dto.g.dart';

@freezed
sealed class AddInventoryBatchRowDto with _$AddInventoryBatchRowDto {
  const factory AddInventoryBatchRowDto({
    @JsonKey(name: 'clientRowId') required String clientRowId,
    @JsonKey(name: 'itemName') required String itemName,
    @JsonKey(name: 'barcode') required String barcode,
    @JsonKey(name: 'itemDescription') String? itemDescription,
    @JsonKey(name: 'uom') required String uom,
    @JsonKey(name: 'batchNumber') required String batchNumber,
    @JsonKey(name: 'quantity') required double quantity,
    @JsonKey(name: 'costPrice') required double costPrice,
    @JsonKey(name: 'mrp') required double mrp,
    @JsonKey(name: 'salesPrice') required double salesPrice,
    @JsonKey(name: 'taxRatePercent') required double taxRatePercent,
    @JsonKey(name: 'taxIncluded') required bool taxIncluded,
    @JsonKey(name: 'expiryDate') String? expiryDate,
    @JsonKey(name: 'manufacturingDate') String? manufacturingDate,
    @JsonKey(name: 'supplierId') String? supplierId,
    @JsonKey(name: 'referenceNumber') String? referenceNumber,
    @JsonKey(name: 'notes') String? notes,
    @JsonKey(name: 'performedAt') String? performedAt,
  }) = _AddInventoryBatchRowDto;

  factory AddInventoryBatchRowDto.fromJson(Map<String, dynamic> json) =>
      _$AddInventoryBatchRowDtoFromJson(json);
}
