// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'inventory_adjustment_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_InventoryAdjustmentDto _$InventoryAdjustmentDtoFromJson(
  Map<String, dynamic> json,
) => _InventoryAdjustmentDto(
  adjustmentId: json['adjustmentId'] as String,
  batchId: json['batchId'] as String,
  itemId: json['itemId'] as String,
  itemName: json['itemName'] as String,
  batchNumber: json['batchNumber'] as String,
  direction: json['direction'] as String,
  reason: json['reason'] as String,
  quantity: (json['quantity'] as num).toDouble(),
  costImpact: (json['costImpact'] as num).toDouble(),
  notes: json['notes'] as String?,
  performedAt: DateTime.parse(json['performedAt'] as String),
  performedByDisplayName: json['performedByDisplayName'] as String,
  isVoided: json['isVoided'] as bool? ?? false,
);

Map<String, dynamic> _$InventoryAdjustmentDtoToJson(
  _InventoryAdjustmentDto instance,
) => <String, dynamic>{
  'adjustmentId': instance.adjustmentId,
  'batchId': instance.batchId,
  'itemId': instance.itemId,
  'itemName': instance.itemName,
  'batchNumber': instance.batchNumber,
  'direction': instance.direction,
  'reason': instance.reason,
  'quantity': instance.quantity,
  'costImpact': instance.costImpact,
  'notes': instance.notes,
  'performedAt': instance.performedAt.toIso8601String(),
  'performedByDisplayName': instance.performedByDisplayName,
  'isVoided': instance.isVoided,
};
