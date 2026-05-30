// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'add_inventory_batch_row_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_AddInventoryBatchRowDto _$AddInventoryBatchRowDtoFromJson(
  Map<String, dynamic> json,
) => _AddInventoryBatchRowDto(
  clientRowId: json['clientRowId'] as String,
  itemName: json['itemName'] as String,
  barcode: json['barcode'] as String,
  itemDescription: json['itemDescription'] as String?,
  uom: json['uom'] as String,
  batchNumber: json['batchNumber'] as String,
  quantity: (json['quantity'] as num).toDouble(),
  costPrice: (json['costPrice'] as num).toDouble(),
  mrp: (json['mrp'] as num).toDouble(),
  salesPrice: (json['salesPrice'] as num).toDouble(),
  taxRatePercent: (json['taxRatePercent'] as num).toDouble(),
  taxIncluded: json['taxIncluded'] as bool,
  expiryDate: json['expiryDate'] as String?,
  manufacturingDate: json['manufacturingDate'] as String?,
  supplierId: json['supplierId'] as String?,
  referenceNumber: json['referenceNumber'] as String?,
  notes: json['notes'] as String?,
  performedAt: json['performedAt'] as String?,
);

Map<String, dynamic> _$AddInventoryBatchRowDtoToJson(
  _AddInventoryBatchRowDto instance,
) => <String, dynamic>{
  'clientRowId': instance.clientRowId,
  'itemName': instance.itemName,
  'barcode': instance.barcode,
  'itemDescription': instance.itemDescription,
  'uom': instance.uom,
  'batchNumber': instance.batchNumber,
  'quantity': instance.quantity,
  'costPrice': instance.costPrice,
  'mrp': instance.mrp,
  'salesPrice': instance.salesPrice,
  'taxRatePercent': instance.taxRatePercent,
  'taxIncluded': instance.taxIncluded,
  'expiryDate': instance.expiryDate,
  'manufacturingDate': instance.manufacturingDate,
  'supplierId': instance.supplierId,
  'referenceNumber': instance.referenceNumber,
  'notes': instance.notes,
  'performedAt': instance.performedAt,
};
