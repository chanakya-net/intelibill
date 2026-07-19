// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'create_purchase_order_draft_request_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_CreatePurchaseOrderDraftLineRequestDto
_$CreatePurchaseOrderDraftLineRequestDtoFromJson(Map<String, dynamic> json) =>
    _CreatePurchaseOrderDraftLineRequestDto(
      itemId: json['itemId'] as String,
      description: json['description'] as String,
      expectedQuantity: (json['expectedQuantity'] as num).toInt(),
      unitCost: (json['unitCost'] as num).toDouble(),
    );

Map<String, dynamic> _$CreatePurchaseOrderDraftLineRequestDtoToJson(
  _CreatePurchaseOrderDraftLineRequestDto instance,
) => <String, dynamic>{
  'itemId': instance.itemId,
  'description': instance.description,
  'expectedQuantity': instance.expectedQuantity,
  'unitCost': instance.unitCost,
};

_CreatePurchaseOrderDraftRequestDto
_$CreatePurchaseOrderDraftRequestDtoFromJson(Map<String, dynamic> json) =>
    _CreatePurchaseOrderDraftRequestDto(
      supplierId: json['supplierId'] as String?,
      orderDate: json['orderDate'] as String?,
      expectedDeliveryDate: json['expectedDeliveryDate'] as String?,
      supplierReferenceNumber: json['supplierReferenceNumber'] as String?,
      notes: json['notes'] as String?,
      supplierName: json['supplierName'] as String?,
      supplierReference: json['supplierReference'] as String?,
      lines:
          (json['lines'] as List<dynamic>?)
              ?.map(
                (e) => CreatePurchaseOrderDraftLineRequestDto.fromJson(
                  e as Map<String, dynamic>,
                ),
              )
              .toList() ??
          const [],
    );

Map<String, dynamic> _$CreatePurchaseOrderDraftRequestDtoToJson(
  _CreatePurchaseOrderDraftRequestDto instance,
) => <String, dynamic>{
  'supplierId': instance.supplierId,
  'orderDate': instance.orderDate,
  'expectedDeliveryDate': instance.expectedDeliveryDate,
  'supplierReferenceNumber': instance.supplierReferenceNumber,
  'notes': instance.notes,
  'supplierName': instance.supplierName,
  'supplierReference': instance.supplierReference,
  'lines': instance.lines,
};
