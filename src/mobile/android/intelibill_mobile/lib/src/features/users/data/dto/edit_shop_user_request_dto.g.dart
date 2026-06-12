// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'edit_shop_user_request_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_EditShopUserRequestDto _$EditShopUserRequestDtoFromJson(
  Map<String, dynamic> json,
) => _EditShopUserRequestDto(
  email: json['email'] as String,
  firstName: json['firstName'] as String,
  lastName: json['lastName'] as String,
  phoneNumber: json['phoneNumber'] as String,
  role: json['role'] as String,
  isLoginEnabled: json['isLoginEnabled'] as bool,
  shopIds: (json['shopIds'] as List<dynamic>).map((e) => e as String).toList(),
);

Map<String, dynamic> _$EditShopUserRequestDtoToJson(
  _EditShopUserRequestDto instance,
) => <String, dynamic>{
  'email': instance.email,
  'firstName': instance.firstName,
  'lastName': instance.lastName,
  'phoneNumber': instance.phoneNumber,
  'role': instance.role,
  'isLoginEnabled': instance.isLoginEnabled,
  'shopIds': instance.shopIds,
};
