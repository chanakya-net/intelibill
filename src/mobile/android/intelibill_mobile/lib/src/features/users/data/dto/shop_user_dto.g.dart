// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'shop_user_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_ShopUserDto _$ShopUserDtoFromJson(Map<String, dynamic> json) => _ShopUserDto(
  userId: json['userId'] as String,
  firstName: json['firstName'] as String,
  lastName: json['lastName'] as String,
  email: json['email'] as String?,
  phoneNumber: json['phoneNumber'] as String?,
  role: json['role'] as String,
  isLoginEnabled: json['isLoginEnabled'] as bool,
  shopIds:
      (json['shopIds'] as List<dynamic>?)?.map((e) => e as String).toList() ??
      const [],
);

Map<String, dynamic> _$ShopUserDtoToJson(_ShopUserDto instance) =>
    <String, dynamic>{
      'userId': instance.userId,
      'firstName': instance.firstName,
      'lastName': instance.lastName,
      'email': instance.email,
      'phoneNumber': instance.phoneNumber,
      'role': instance.role,
      'isLoginEnabled': instance.isLoginEnabled,
      'shopIds': instance.shopIds,
    };
