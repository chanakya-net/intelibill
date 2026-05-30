import 'package:freezed_annotation/freezed_annotation.dart';

part 'create_item_request_dto.freezed.dart';
part 'create_item_request_dto.g.dart';

@freezed
sealed class CreateItemRequestDto with _$CreateItemRequestDto {
  const factory CreateItemRequestDto({
    @JsonKey(name: 'name') required String name,
    @JsonKey(name: 'barcode') required String barcode,
    @JsonKey(name: 'description') String? description,
    @JsonKey(name: 'uom') required String uom,
    @JsonKey(name: 'isActive') required bool isActive,
  }) = _CreateItemRequestDto;

  factory CreateItemRequestDto.fromJson(Map<String, dynamic> json) =>
      _$CreateItemRequestDtoFromJson(json);
}
