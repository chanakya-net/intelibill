// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'item_catalog_response_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_ItemCatalogResponseDto _$ItemCatalogResponseDtoFromJson(
  Map<String, dynamic> json,
) => _ItemCatalogResponseDto(
  items:
      (json['items'] as List<dynamic>?)
          ?.map((e) => ItemDto.fromJson(e as Map<String, dynamic>))
          .toList() ??
      const [],
  totalCount: (json['totalCount'] as num).toInt(),
  pageNumber: (json['pageNumber'] as num).toInt(),
  pageSize: (json['pageSize'] as num).toInt(),
);

Map<String, dynamic> _$ItemCatalogResponseDtoToJson(
  _ItemCatalogResponseDto instance,
) => <String, dynamic>{
  'items': instance.items,
  'totalCount': instance.totalCount,
  'pageNumber': instance.pageNumber,
  'pageSize': instance.pageSize,
};
