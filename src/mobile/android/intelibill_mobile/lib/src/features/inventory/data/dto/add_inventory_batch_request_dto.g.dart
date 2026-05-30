// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'add_inventory_batch_request_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_AddInventoryBatchRequestDto _$AddInventoryBatchRequestDtoFromJson(
  Map<String, dynamic> json,
) => _AddInventoryBatchRequestDto(
  items: (json['items'] as List<dynamic>)
      .map((e) => AddInventoryBatchRowDto.fromJson(e as Map<String, dynamic>))
      .toList(),
);

Map<String, dynamic> _$AddInventoryBatchRequestDtoToJson(
  _AddInventoryBatchRequestDto instance,
) => <String, dynamic>{'items': instance.items};
