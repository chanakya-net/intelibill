import 'package:freezed_annotation/freezed_annotation.dart';

part 'update_item_request_dto.freezed.dart';
part 'update_item_request_dto.g.dart';

@freezed
sealed class UpdateItemRequestDto with _$UpdateItemRequestDto {
  const factory UpdateItemRequestDto({
    @JsonKey(name: 'name') required String name,
    @JsonKey(name: 'barcode') required String barcode,
    @JsonKey(name: 'description') String? description,
    @JsonKey(name: 'uom') required String uom,
    @JsonKey(name: 'isActive') required bool isActive,
  }) = _UpdateItemRequestDto;

  factory UpdateItemRequestDto.fromJson(Map<String, dynamic> json) =>
      _$UpdateItemRequestDtoFromJson(json);
}
