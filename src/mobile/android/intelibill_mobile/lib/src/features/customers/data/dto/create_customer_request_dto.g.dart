// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'create_customer_request_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_CreateCustomerRequestDto _$CreateCustomerRequestDtoFromJson(
  Map<String, dynamic> json,
) => _CreateCustomerRequestDto(
  name: json['name'] as String,
  phoneNumber: json['phoneNumber'] as String,
  address: json['address'] as String?,
  isActive: json['isActive'] as bool,
);

Map<String, dynamic> _$CreateCustomerRequestDtoToJson(
  _CreateCustomerRequestDto instance,
) => <String, dynamic>{
  'name': instance.name,
  'phoneNumber': instance.phoneNumber,
  'address': instance.address,
  'isActive': instance.isActive,
};
