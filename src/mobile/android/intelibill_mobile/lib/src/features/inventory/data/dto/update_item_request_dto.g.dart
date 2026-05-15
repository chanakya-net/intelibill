// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'update_item_request_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_UpdateItemRequestDto _$UpdateItemRequestDtoFromJson(
  Map<String, dynamic> json,
) => _UpdateItemRequestDto(
  name: json['name'] as String,
  barcode: json['barcode'] as String,
  description: json['description'] as String?,
  uom: json['uom'] as String,
  isActive: json['isActive'] as bool,
);

Map<String, dynamic> _$UpdateItemRequestDtoToJson(
  _UpdateItemRequestDto instance,
) => <String, dynamic>{
  'name': instance.name,
  'barcode': instance.barcode,
  'description': instance.description,
  'uom': instance.uom,
  'isActive': instance.isActive,
};
