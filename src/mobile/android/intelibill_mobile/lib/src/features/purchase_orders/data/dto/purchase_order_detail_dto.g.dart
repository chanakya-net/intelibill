// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'purchase_order_detail_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_PurchaseOrderLineDto _$PurchaseOrderLineDtoFromJson(
  Map<String, dynamic> json,
) => _PurchaseOrderLineDto(
  lineId: json['lineId'] as String,
  itemId: json['itemId'] as String,
  description: json['description'] as String,
  expectedQuantity: (json['expectedQuantity'] as num).toInt(),
  receivedQuantity: (json['receivedQuantity'] as num).toInt(),
  remainingQuantity: (json['remainingQuantity'] as num).toInt(),
  unitCost: (json['unitCost'] as num).toDouble(),
  lineTotal: (json['lineTotal'] as num).toDouble(),
);

Map<String, dynamic> _$PurchaseOrderLineDtoToJson(
  _PurchaseOrderLineDto instance,
) => <String, dynamic>{
  'lineId': instance.lineId,
  'itemId': instance.itemId,
  'description': instance.description,
  'expectedQuantity': instance.expectedQuantity,
  'receivedQuantity': instance.receivedQuantity,
  'remainingQuantity': instance.remainingQuantity,
  'unitCost': instance.unitCost,
  'lineTotal': instance.lineTotal,
};

_PurchaseOrderDetailDto _$PurchaseOrderDetailDtoFromJson(
  Map<String, dynamic> json,
) => _PurchaseOrderDetailDto(
  purchaseOrderId: json['purchaseOrderId'] as String,
  purchaseOrderNumber: json['purchaseOrderNumber'] as String,
  status: json['status'] as String,
  supplierId: json['supplierId'] as String?,
  orderDate: json['orderDate'] as String?,
  expectedDeliveryDate: json['expectedDeliveryDate'] as String?,
  supplierReferenceNumber: json['supplierReferenceNumber'] as String?,
  notes: json['notes'] as String?,
  lines: (json['lines'] as List<dynamic>)
      .map((e) => PurchaseOrderLineDto.fromJson(e as Map<String, dynamic>))
      .toList(),
  expectedTotal: (json['expectedTotal'] as num).toDouble(),
  createdAt: DateTime.parse(json['createdAt'] as String),
  supplierName: json['supplierName'] as String?,
  supplierReference: json['supplierReference'] as String?,
  receivedQuantity: (json['receivedQuantity'] as num?)?.toInt() ?? 0,
);

Map<String, dynamic> _$PurchaseOrderDetailDtoToJson(
  _PurchaseOrderDetailDto instance,
) => <String, dynamic>{
  'purchaseOrderId': instance.purchaseOrderId,
  'purchaseOrderNumber': instance.purchaseOrderNumber,
  'status': instance.status,
  'supplierId': instance.supplierId,
  'orderDate': instance.orderDate,
  'expectedDeliveryDate': instance.expectedDeliveryDate,
  'supplierReferenceNumber': instance.supplierReferenceNumber,
  'notes': instance.notes,
  'lines': instance.lines,
  'expectedTotal': instance.expectedTotal,
  'createdAt': instance.createdAt.toIso8601String(),
  'supplierName': instance.supplierName,
  'supplierReference': instance.supplierReference,
  'receivedQuantity': instance.receivedQuantity,
};
