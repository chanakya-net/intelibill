import 'package:freezed_annotation/freezed_annotation.dart';

part 'generate_item_barcode_response_dto.freezed.dart';
part 'generate_item_barcode_response_dto.g.dart';

@freezed
sealed class GenerateItemBarcodeResponseDto
    with _$GenerateItemBarcodeResponseDto {
  const factory GenerateItemBarcodeResponseDto({
    @JsonKey(name: 'barcode') required String barcode,
  }) = _GenerateItemBarcodeResponseDto;

  factory GenerateItemBarcodeResponseDto.fromJson(Map<String, dynamic> json) =>
      _$GenerateItemBarcodeResponseDtoFromJson(json);
}
