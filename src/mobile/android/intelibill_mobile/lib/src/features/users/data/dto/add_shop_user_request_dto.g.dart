// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'add_shop_user_request_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_AddShopUserRequestDto _$AddShopUserRequestDtoFromJson(
  Map<String, dynamic> json,
) => _AddShopUserRequestDto(
  shopIds: (json['shopIds'] as List<dynamic>).map((e) => e as String).toList(),
  email: json['email'] as String,
  firstName: json['firstName'] as String,
  lastName: json['lastName'] as String,
  phoneNumber: json['phoneNumber'] as String,
  password: json['password'] as String,
  confirmPassword: json['confirmPassword'] as String,
  role: json['role'] as String,
);

Map<String, dynamic> _$AddShopUserRequestDtoToJson(
  _AddShopUserRequestDto instance,
) => <String, dynamic>{
  'shopIds': instance.shopIds,
  'email': instance.email,
  'firstName': instance.firstName,
  'lastName': instance.lastName,
  'phoneNumber': instance.phoneNumber,
  'password': instance.password,
  'confirmPassword': instance.confirmPassword,
  'role': instance.role,
};
