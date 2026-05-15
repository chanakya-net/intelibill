import 'package:freezed_annotation/freezed_annotation.dart';

part 'item_dto.freezed.dart';
part 'item_dto.g.dart';

@freezed
sealed class ItemDto with _$ItemDto {
  const factory ItemDto({
    @JsonKey(name: 'id') required String id,
    @JsonKey(name: 'name') required String name,
    @JsonKey(name: 'barcode') required String barcode,
    @JsonKey(name: 'description') String? description,
    @JsonKey(name: 'uom') required String uom,
    @JsonKey(name: 'isActive') required bool isActive,
    @JsonKey(name: 'currentStock') @Default(0.0) double currentStock,
  }) = _ItemDto;

  factory ItemDto.fromJson(Map<String, dynamic> json) =>
      _$ItemDtoFromJson(json);
}
