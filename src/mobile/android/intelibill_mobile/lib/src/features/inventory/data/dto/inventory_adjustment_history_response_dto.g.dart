// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'inventory_adjustment_history_response_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_InventoryAdjustmentHistoryResponseDto
_$InventoryAdjustmentHistoryResponseDtoFromJson(Map<String, dynamic> json) =>
    _InventoryAdjustmentHistoryResponseDto(
      items:
          (json['items'] as List<dynamic>?)
              ?.map(
                (e) =>
                    InventoryAdjustmentDto.fromJson(e as Map<String, dynamic>),
              )
              .toList() ??
          const [],
      totalCount: (json['totalCount'] as num).toInt(),
      pageNumber: (json['pageNumber'] as num).toInt(),
      pageSize: (json['pageSize'] as num).toInt(),
    );

Map<String, dynamic> _$InventoryAdjustmentHistoryResponseDtoToJson(
  _InventoryAdjustmentHistoryResponseDto instance,
) => <String, dynamic>{
  'items': instance.items,
  'totalCount': instance.totalCount,
  'pageNumber': instance.pageNumber,
  'pageSize': instance.pageSize,
};
