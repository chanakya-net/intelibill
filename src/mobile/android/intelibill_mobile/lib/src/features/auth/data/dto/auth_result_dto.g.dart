// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'auth_result_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_AuthResultDto _$AuthResultDtoFromJson(Map<String, dynamic> json) =>
    _AuthResultDto(
      accessToken: json['accessToken'] as String,
      refreshToken: json['refreshToken'] as String,
      accessTokenExpiresAt: DateTime.parse(
        json['accessTokenExpiresAt'] as String,
      ),
      refreshTokenExpiresAt: DateTime.parse(
        json['refreshTokenExpiresAt'] as String,
      ),
      user: AuthUserDto.fromJson(json['user'] as Map<String, dynamic>),
      activeShopId: json['activeShopId'] as String?,
      shops: (json['shops'] as List<dynamic>?)
          ?.map((e) => UserShopDto.fromJson(e as Map<String, dynamic>))
          .toList(),
    );

Map<String, dynamic> _$AuthResultDtoToJson(_AuthResultDto instance) =>
    <String, dynamic>{
      'accessToken': instance.accessToken,
      'refreshToken': instance.refreshToken,
      'accessTokenExpiresAt': instance.accessTokenExpiresAt.toIso8601String(),
      'refreshTokenExpiresAt': instance.refreshTokenExpiresAt.toIso8601String(),
      'user': instance.user,
      'activeShopId': instance.activeShopId,
      'shops': instance.shops,
    };
