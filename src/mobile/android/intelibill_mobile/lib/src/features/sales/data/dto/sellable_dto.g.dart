// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'sellable_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_SellableDto _$SellableDtoFromJson(Map<String, dynamic> json) => _SellableDto(
  kind: json['kind'] as String,
  inventoryBatchId: json['inventoryBatchId'] as String?,
  barcode: json['barcode'] as String?,
  itemName: json['itemName'] as String?,
  batchNumber: json['batchNumber'] as String?,
  quantity: (json['quantity'] as num?)?.toDouble() ?? 0.0,
  salesPrice: (json['salesPrice'] as num?)?.toDouble() ?? 0.0,
  mrp: (json['mrp'] as num?)?.toDouble() ?? 0.0,
  taxRatePercent: (json['taxRatePercent'] as num?)?.toDouble() ?? 0.0,
  taxIncluded: json['taxIncluded'] as bool? ?? false,
  purchaseTaxIncluded: json['purchaseTaxIncluded'] as bool? ?? false,
  expiryDate: json['expiryDate'] == null
      ? null
      : DateTime.parse(json['expiryDate'] as String),
  serviceId: json['serviceId'] as String?,
  code: json['code'] as String?,
  name: json['name'] as String?,
  description: json['description'] as String?,
  price: (json['price'] as num?)?.toDouble() ?? 0.0,
  hsnCode: json['hsnCode'] as String?,
);

Map<String, dynamic> _$SellableDtoToJson(_SellableDto instance) =>
    <String, dynamic>{
      'kind': instance.kind,
      'inventoryBatchId': instance.inventoryBatchId,
      'barcode': instance.barcode,
      'itemName': instance.itemName,
      'batchNumber': instance.batchNumber,
      'quantity': instance.quantity,
      'salesPrice': instance.salesPrice,
      'mrp': instance.mrp,
      'taxRatePercent': instance.taxRatePercent,
      'taxIncluded': instance.taxIncluded,
      'purchaseTaxIncluded': instance.purchaseTaxIncluded,
      'expiryDate': instance.expiryDate?.toIso8601String(),
      'serviceId': instance.serviceId,
      'code': instance.code,
      'name': instance.name,
      'description': instance.description,
      'price': instance.price,
      'hsnCode': instance.hsnCode,
    };
