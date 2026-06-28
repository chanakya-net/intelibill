// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'create_service_request_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_CreateServiceRequestDto _$CreateServiceRequestDtoFromJson(
  Map<String, dynamic> json,
) => _CreateServiceRequestDto(
  name: json['name'] as String,
  description: json['description'] as String?,
  price: (json['price'] as num).toDouble(),
  hsnCode: json['hsnCode'] as String?,
  taxRatePercent: (json['taxRatePercent'] as num).toDouble(),
  taxIncluded: json['taxIncluded'] as bool,
  isActive: json['isActive'] as bool,
);

Map<String, dynamic> _$CreateServiceRequestDtoToJson(
  _CreateServiceRequestDto instance,
) => <String, dynamic>{
  'name': instance.name,
  'description': instance.description,
  'price': instance.price,
  'hsnCode': instance.hsnCode,
  'taxRatePercent': instance.taxRatePercent,
  'taxIncluded': instance.taxIncluded,
  'isActive': instance.isActive,
};
