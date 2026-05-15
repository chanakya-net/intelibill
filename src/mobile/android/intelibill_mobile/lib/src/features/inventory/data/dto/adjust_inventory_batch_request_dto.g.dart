// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'adjust_inventory_batch_request_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_AdjustInventoryBatchRequestDto _$AdjustInventoryBatchRequestDtoFromJson(
  Map<String, dynamic> json,
) => _AdjustInventoryBatchRequestDto(
  direction: json['direction'] as String,
  reason: json['reason'] as String,
  quantity: (json['quantity'] as num).toDouble(),
  performedAt: json['performedAt'] as String?,
  notes: json['notes'] as String?,
);

Map<String, dynamic> _$AdjustInventoryBatchRequestDtoToJson(
  _AdjustInventoryBatchRequestDto instance,
) => <String, dynamic>{
  'direction': instance.direction,
  'reason': instance.reason,
  'quantity': instance.quantity,
  'performedAt': instance.performedAt,
  'notes': instance.notes,
};
