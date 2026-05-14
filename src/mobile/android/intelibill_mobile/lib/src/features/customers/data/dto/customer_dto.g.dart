// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'customer_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_CustomerDto _$CustomerDtoFromJson(Map<String, dynamic> json) => _CustomerDto(
  customerId: json['customerId'] as String,
  name: json['name'] as String,
  phoneNumber: json['phoneNumber'] as String,
  address: json['address'] as String?,
  isActive: json['isActive'] as bool,
  outstandingDue: (json['outstandingDue'] as num?)?.toDouble() ?? 0.0,
);

Map<String, dynamic> _$CustomerDtoToJson(_CustomerDto instance) =>
    <String, dynamic>{
      'customerId': instance.customerId,
      'name': instance.name,
      'phoneNumber': instance.phoneNumber,
      'address': instance.address,
      'isActive': instance.isActive,
      'outstandingDue': instance.outstandingDue,
    };
