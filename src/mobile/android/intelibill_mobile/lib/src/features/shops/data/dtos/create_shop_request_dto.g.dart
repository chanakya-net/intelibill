// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'create_shop_request_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_CreateShopRequestDto _$CreateShopRequestDtoFromJson(
  Map<String, dynamic> json,
) => _CreateShopRequestDto(
  name: json['name'] as String,
  address: json['address'] as String,
  city: json['city'] as String,
  state: json['state'] as String,
  pincode: json['pincode'] as String,
  contactPerson: json['contactPerson'] as String?,
  mobileNumber: json['mobileNumber'] as String?,
  gstNumber: json['gstNumber'] as String?,
);

Map<String, dynamic> _$CreateShopRequestDtoToJson(
  _CreateShopRequestDto instance,
) => <String, dynamic>{
  'name': instance.name,
  'address': instance.address,
  'city': instance.city,
  'state': instance.state,
  'pincode': instance.pincode,
  'contactPerson': instance.contactPerson,
  'mobileNumber': instance.mobileNumber,
  'gstNumber': instance.gstNumber,
};
