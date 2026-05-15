// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'item_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_ItemDto _$ItemDtoFromJson(Map<String, dynamic> json) => _ItemDto(
  id: json['id'] as String,
  name: json['name'] as String,
  barcode: json['barcode'] as String,
  description: json['description'] as String?,
  uom: json['uom'] as String,
  isActive: json['isActive'] as bool,
  currentStock: (json['currentStock'] as num?)?.toDouble() ?? 0.0,
);

Map<String, dynamic> _$ItemDtoToJson(_ItemDto instance) => <String, dynamic>{
  'id': instance.id,
  'name': instance.name,
  'barcode': instance.barcode,
  'description': instance.description,
  'uom': instance.uom,
  'isActive': instance.isActive,
  'currentStock': instance.currentStock,
};
