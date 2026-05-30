// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'create_item_request_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_CreateItemRequestDto _$CreateItemRequestDtoFromJson(
  Map<String, dynamic> json,
) => _CreateItemRequestDto(
  name: json['name'] as String,
  barcode: json['barcode'] as String,
  description: json['description'] as String?,
  uom: json['uom'] as String,
  isActive: json['isActive'] as bool,
);

Map<String, dynamic> _$CreateItemRequestDtoToJson(
  _CreateItemRequestDto instance,
) => <String, dynamic>{
  'name': instance.name,
  'barcode': instance.barcode,
  'description': instance.description,
  'uom': instance.uom,
  'isActive': instance.isActive,
};
