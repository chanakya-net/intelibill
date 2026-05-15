import 'package:freezed_annotation/freezed_annotation.dart';

part 'set_default_shop_request_dto.freezed.dart';
part 'set_default_shop_request_dto.g.dart';

@freezed
sealed class SetDefaultShopRequestDto with _$SetDefaultShopRequestDto {
  const factory SetDefaultShopRequestDto({
    @JsonKey(name: 'shopId') required String shopId,
  }) = _SetDefaultShopRequestDto;

  factory SetDefaultShopRequestDto.fromJson(Map<String, dynamic> json) =>
      _$SetDefaultShopRequestDtoFromJson(json);
}
