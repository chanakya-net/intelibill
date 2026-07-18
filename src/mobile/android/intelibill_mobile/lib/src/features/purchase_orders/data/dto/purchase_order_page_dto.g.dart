// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'purchase_order_page_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_PurchaseOrderPageDto _$PurchaseOrderPageDtoFromJson(
  Map<String, dynamic> json,
) => _PurchaseOrderPageDto(
  items:
      (json['items'] as List<dynamic>?)
          ?.map(
            (e) => PurchaseOrderListItemDto.fromJson(e as Map<String, dynamic>),
          )
          .toList() ??
      const [],
  totalCount: (json['totalCount'] as num).toInt(),
  pageNumber: (json['pageNumber'] as num).toInt(),
  pageSize: (json['pageSize'] as num).toInt(),
);

Map<String, dynamic> _$PurchaseOrderPageDtoToJson(
  _PurchaseOrderPageDto instance,
) => <String, dynamic>{
  'items': instance.items,
  'totalCount': instance.totalCount,
  'pageNumber': instance.pageNumber,
  'pageSize': instance.pageSize,
};
