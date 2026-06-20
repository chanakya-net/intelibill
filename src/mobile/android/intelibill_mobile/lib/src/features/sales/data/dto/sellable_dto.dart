import 'package:freezed_annotation/freezed_annotation.dart';

part 'sellable_dto.freezed.dart';
part 'sellable_dto.g.dart';

@freezed
sealed class SellableDto with _$SellableDto {
  const factory SellableDto({
    @JsonKey(name: 'kind') required String kind,
    @JsonKey(name: 'inventoryBatchId') String? inventoryBatchId,
    @JsonKey(name: 'barcode') String? barcode,
    @JsonKey(name: 'itemName') String? itemName,
    @JsonKey(name: 'batchNumber') String? batchNumber,
    @JsonKey(name: 'quantity') @Default(0.0) double quantity,
    @JsonKey(name: 'salesPrice') @Default(0.0) double salesPrice,
    @JsonKey(name: 'mrp') @Default(0.0) double mrp,
    @JsonKey(name: 'taxRatePercent') @Default(0.0) double taxRatePercent,
    @JsonKey(name: 'taxIncluded') @Default(false) bool taxIncluded,
    @JsonKey(name: 'purchaseTaxIncluded')
    @Default(false)
    bool purchaseTaxIncluded,
    @JsonKey(name: 'expiryDate') DateTime? expiryDate,
    @JsonKey(name: 'serviceId') String? serviceId,
    @JsonKey(name: 'code') String? code,
    @JsonKey(name: 'name') String? name,
    @JsonKey(name: 'description') String? description,
    @JsonKey(name: 'price') @Default(0.0) double price,
    @JsonKey(name: 'hsnCode') String? hsnCode,
  }) = _SellableDto;

  factory SellableDto.fromJson(Map<String, dynamic> json) =>
      _$SellableDtoFromJson(json);
}
