// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'service_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_ServiceDto _$ServiceDtoFromJson(Map<String, dynamic> json) => _ServiceDto(
  serviceId: json['serviceId'] as String,
  code: json['code'] as String,
  name: json['name'] as String,
  description: json['description'] as String?,
  price: (json['price'] as num?)?.toDouble() ?? 0.0,
  hsnCode: json['hsnCode'] as String?,
  taxRatePercent: (json['taxRatePercent'] as num?)?.toDouble() ?? 0.0,
  taxIncluded: json['taxIncluded'] as bool,
  isActive: json['isActive'] as bool,
);

Map<String, dynamic> _$ServiceDtoToJson(_ServiceDto instance) =>
    <String, dynamic>{
      'serviceId': instance.serviceId,
      'code': instance.code,
      'name': instance.name,
      'description': instance.description,
      'price': instance.price,
      'hsnCode': instance.hsnCode,
      'taxRatePercent': instance.taxRatePercent,
      'taxIncluded': instance.taxIncluded,
      'isActive': instance.isActive,
    };
