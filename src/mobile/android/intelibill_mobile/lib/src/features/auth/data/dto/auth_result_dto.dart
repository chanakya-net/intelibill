import 'package:freezed_annotation/freezed_annotation.dart';
import 'package:intelibill_mobile/src/features/auth/data/dto/auth_user_dto.dart';
import 'package:intelibill_mobile/src/features/auth/data/dto/user_shop_dto.dart';

part 'auth_result_dto.freezed.dart';
part 'auth_result_dto.g.dart';

@freezed
sealed class AuthResultDto with _$AuthResultDto {
  const factory AuthResultDto({
    @JsonKey(name: 'accessToken') required String accessToken,
    @JsonKey(name: 'refreshToken') required String refreshToken,
    @JsonKey(name: 'accessTokenExpiresAt')
    required DateTime accessTokenExpiresAt,
    @JsonKey(name: 'refreshTokenExpiresAt')
    required DateTime refreshTokenExpiresAt,
    @JsonKey(name: 'user') required AuthUserDto user,
    @JsonKey(name: 'activeShopId') String? activeShopId,
    @JsonKey(name: 'shops') List<UserShopDto>? shops,
  }) = _AuthResultDto;

  factory AuthResultDto.fromJson(Map<String, dynamic> json) =>
      _$AuthResultDtoFromJson(json);
}
