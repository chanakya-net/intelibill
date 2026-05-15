import 'package:freezed_annotation/freezed_annotation.dart';

part 'product_details_dto.freezed.dart';
part 'product_details_dto.g.dart';

@freezed
sealed class ProductDetailsDto with _$ProductDetailsDto {
  const factory ProductDetailsDto({
    @JsonKey(name: 'name') required String name,
    @JsonKey(name: 'description') required String description,
    @JsonKey(name: 'uom') required String uom,
    @JsonKey(name: 'costPrice') required double costPrice,
    @JsonKey(name: 'mrp') required double mrp,
    @JsonKey(name: 'salesPrice') required double salesPrice,
    @JsonKey(name: 'supplierId') String? supplierId,
    @JsonKey(name: 'supplierName') String? supplierName,
    @JsonKey(name: 'taxIncluded') bool? taxIncluded,
    @JsonKey(name: 'taxRatePercent') double? taxRatePercent,
  }) = _ProductDetailsDto;

  factory ProductDetailsDto.fromJson(Map<String, dynamic> json) =>
      _$ProductDetailsDtoFromJson(json);
}
