// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'inventory_batch_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_InventoryBatchDto _$InventoryBatchDtoFromJson(Map<String, dynamic> json) =>
    _InventoryBatchDto(
      id: json['id'] as String,
      itemId: json['itemId'] as String,
      itemName: json['itemName'] as String,
      barcode: json['barcode'] as String,
      itemUom: json['itemUom'] as String? ?? '',
      batchNumber: json['batchNumber'] as String,
      quantity: (json['quantity'] as num).toDouble(),
      costPrice: (json['costPrice'] as num).toDouble(),
      mrp: (json['mrp'] as num).toDouble(),
      salesPrice: (json['salesPrice'] as num).toDouble(),
      taxRatePercent: (json['taxRatePercent'] as num?)?.toDouble() ?? 0.0,
      taxIncluded: json['taxIncluded'] as bool? ?? false,
      expiryDate: json['expiryDate'] == null
          ? null
          : DateTime.parse(json['expiryDate'] as String),
      manufacturingDate: json['manufacturingDate'] == null
          ? null
          : DateTime.parse(json['manufacturingDate'] as String),
      referenceNumber: json['referenceNumber'] as String?,
      notes: json['notes'] as String?,
      supplierId: json['supplierId'] as String?,
      supplierName: json['supplierName'] as String?,
      isVoided: json['isVoided'] as bool? ?? false,
      createdAt: DateTime.parse(json['createdAt'] as String),
    );

Map<String, dynamic> _$InventoryBatchDtoToJson(_InventoryBatchDto instance) =>
    <String, dynamic>{
      'id': instance.id,
      'itemId': instance.itemId,
      'itemName': instance.itemName,
      'barcode': instance.barcode,
      'itemUom': instance.itemUom,
      'batchNumber': instance.batchNumber,
      'quantity': instance.quantity,
      'costPrice': instance.costPrice,
      'mrp': instance.mrp,
      'salesPrice': instance.salesPrice,
      'taxRatePercent': instance.taxRatePercent,
      'taxIncluded': instance.taxIncluded,
      'expiryDate': instance.expiryDate?.toIso8601String(),
      'manufacturingDate': instance.manufacturingDate?.toIso8601String(),
      'referenceNumber': instance.referenceNumber,
      'notes': instance.notes,
      'supplierId': instance.supplierId,
      'supplierName': instance.supplierName,
      'isVoided': instance.isVoided,
      'createdAt': instance.createdAt.toIso8601String(),
    };
