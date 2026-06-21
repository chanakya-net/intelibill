// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'update_service_request_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_UpdateServiceRequestDto _$UpdateServiceRequestDtoFromJson(
  Map<String, dynamic> json,
) => _UpdateServiceRequestDto(
  name: json['name'] as String,
  description: json['description'] as String?,
  price: (json['price'] as num).toDouble(),
  hsnCode: json['hsnCode'] as String?,
  taxRatePercent: (json['taxRatePercent'] as num).toDouble(),
  taxIncluded: json['taxIncluded'] as bool,
);

Map<String, dynamic> _$UpdateServiceRequestDtoToJson(
  _UpdateServiceRequestDto instance,
) => <String, dynamic>{
  'name': instance.name,
  'description': instance.description,
  'price': instance.price,
  'hsnCode': instance.hsnCode,
  'taxRatePercent': instance.taxRatePercent,
  'taxIncluded': instance.taxIncluded,
};
