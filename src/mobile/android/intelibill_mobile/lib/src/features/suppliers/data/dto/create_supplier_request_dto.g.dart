// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'create_supplier_request_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_CreateSupplierRequestDto _$CreateSupplierRequestDtoFromJson(
  Map<String, dynamic> json,
) => _CreateSupplierRequestDto(
  name: json['name'] as String,
  contactPersonName: json['contactPersonName'] as String?,
  contactPersonPhone: json['contactPersonPhone'] as String?,
  address: json['address'] as String,
  city: json['city'] as String,
  state: json['state'] as String,
  pin: json['pin'] as String,
  isActive: json['isActive'] as bool,
  isPreferred: json['isPreferred'] as bool,
);

Map<String, dynamic> _$CreateSupplierRequestDtoToJson(
  _CreateSupplierRequestDto instance,
) => <String, dynamic>{
  'name': instance.name,
  'contactPersonName': instance.contactPersonName,
  'contactPersonPhone': instance.contactPersonPhone,
  'address': instance.address,
  'city': instance.city,
  'state': instance.state,
  'pin': instance.pin,
  'isActive': instance.isActive,
  'isPreferred': instance.isPreferred,
};
