// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'supplier_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_SupplierDto _$SupplierDtoFromJson(Map<String, dynamic> json) => _SupplierDto(
  supplierId: json['supplierId'] as String,
  name: json['name'] as String,
  contactPersonName: json['contactPersonName'] as String?,
  contactPersonPhone: json['contactPersonPhone'] as String?,
  address: json['address'] as String?,
  city: json['city'] as String?,
  state: json['state'] as String?,
  pin: json['pin'] as String?,
  isSystem: json['isSystem'] as bool,
  isActive: json['isActive'] as bool,
  isPreferred: json['isPreferred'] as bool,
  balanceDue: (json['balanceDue'] as num?)?.toDouble() ?? 0.0,
);

Map<String, dynamic> _$SupplierDtoToJson(_SupplierDto instance) =>
    <String, dynamic>{
      'supplierId': instance.supplierId,
      'name': instance.name,
      'contactPersonName': instance.contactPersonName,
      'contactPersonPhone': instance.contactPersonPhone,
      'address': instance.address,
      'city': instance.city,
      'state': instance.state,
      'pin': instance.pin,
      'isSystem': instance.isSystem,
      'isActive': instance.isActive,
      'isPreferred': instance.isPreferred,
      'balanceDue': instance.balanceDue,
    };
