// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'user_shop_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_UserShopDto _$UserShopDtoFromJson(Map<String, dynamic> json) => _UserShopDto(
  shopId: json['shopId'] as String,
  shopName: json['shopName'] as String,
  role: json['role'] as String,
  isDefault: json['isDefault'] as bool,
  lastUsedAt: json['lastUsedAt'] == null
      ? null
      : DateTime.parse(json['lastUsedAt'] as String),
);

Map<String, dynamic> _$UserShopDtoToJson(_UserShopDto instance) =>
    <String, dynamic>{
      'shopId': instance.shopId,
      'shopName': instance.shopName,
      'role': instance.role,
      'isDefault': instance.isDefault,
      'lastUsedAt': instance.lastUsedAt?.toIso8601String(),
    };
