import 'package:freezed_annotation/freezed_annotation.dart';

part 'user_shop_dto.freezed.dart';
part 'user_shop_dto.g.dart';

@freezed
sealed class UserShopDto with _$UserShopDto {
  const factory UserShopDto({
    @JsonKey(name: 'shopId') required String shopId,
    @JsonKey(name: 'shopName') required String shopName,
    @JsonKey(name: 'role') required String role,
    @JsonKey(name: 'isDefault') required bool isDefault,
    @JsonKey(name: 'lastUsedAt') DateTime? lastUsedAt,
  }) = _UserShopDto;

  factory UserShopDto.fromJson(Map<String, dynamic> json) =>
      _$UserShopDtoFromJson(json);
}
