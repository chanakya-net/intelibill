// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'purchase_order_list_item_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_PurchaseOrderListItemDto _$PurchaseOrderListItemDtoFromJson(
  Map<String, dynamic> json,
) => _PurchaseOrderListItemDto(
  purchaseOrderId: json['purchaseOrderId'] as String,
  purchaseOrderNumber: json['purchaseOrderNumber'] as String,
  status: json['status'] as String,
  supplierName: json['supplierName'] as String?,
  supplierReference: json['supplierReference'] as String?,
  lineCount: (json['lineCount'] as num).toInt(),
  expectedQuantity: (json['expectedQuantity'] as num).toInt(),
  receivedQuantity: (json['receivedQuantity'] as num).toInt(),
  expectedTotal: (json['expectedTotal'] as num).toDouble(),
  createdAt: DateTime.parse(json['createdAt'] as String),
);

Map<String, dynamic> _$PurchaseOrderListItemDtoToJson(
  _PurchaseOrderListItemDto instance,
) => <String, dynamic>{
  'purchaseOrderId': instance.purchaseOrderId,
  'purchaseOrderNumber': instance.purchaseOrderNumber,
  'status': instance.status,
  'supplierName': instance.supplierName,
  'supplierReference': instance.supplierReference,
  'lineCount': instance.lineCount,
  'expectedQuantity': instance.expectedQuantity,
  'receivedQuantity': instance.receivedQuantity,
  'expectedTotal': instance.expectedTotal,
  'createdAt': instance.createdAt.toIso8601String(),
};
