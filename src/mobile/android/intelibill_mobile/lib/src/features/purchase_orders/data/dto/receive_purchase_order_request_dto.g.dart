// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'receive_purchase_order_request_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_ReceivePurchaseOrderLineRequestDto
_$ReceivePurchaseOrderLineRequestDtoFromJson(Map<String, dynamic> json) =>
    _ReceivePurchaseOrderLineRequestDto(
      purchaseOrderLineId: json['purchaseOrderLineId'] as String,
      barcode: json['barcode'] as String,
      batchNumber: json['batchNumber'] as String,
      quantity: (json['quantity'] as num).toDouble(),
      totalPurchaseCost: (json['totalPurchaseCost'] as num).toDouble(),
      unitCost: (json['unitCost'] as num).toDouble(),
      mrp: (json['mrp'] as num).toDouble(),
      salesPrice: (json['salesPrice'] as num).toDouble(),
      taxRatePercent: (json['taxRatePercent'] as num).toDouble(),
      taxIncluded: json['taxIncluded'] as bool,
      purchaseTaxIncluded: json['purchaseTaxIncluded'] as bool,
      expiryDate: json['expiryDate'] == null
          ? null
          : DateTime.parse(json['expiryDate'] as String),
      manufacturingDate: json['manufacturingDate'] == null
          ? null
          : DateTime.parse(json['manufacturingDate'] as String),
    );

Map<String, dynamic> _$ReceivePurchaseOrderLineRequestDtoToJson(
  _ReceivePurchaseOrderLineRequestDto instance,
) => <String, dynamic>{
  'purchaseOrderLineId': instance.purchaseOrderLineId,
  'barcode': _trim(instance.barcode),
  'batchNumber': _trim(instance.batchNumber),
  'quantity': instance.quantity,
  'totalPurchaseCost': instance.totalPurchaseCost,
  'mrp': instance.mrp,
  'salesPrice': instance.salesPrice,
  'taxRatePercent': instance.taxRatePercent,
  'taxIncluded': instance.taxIncluded,
  'purchaseTaxIncluded': instance.purchaseTaxIncluded,
  'expiryDate': _dateOnlyToJson(instance.expiryDate),
  'manufacturingDate': _dateOnlyToJson(instance.manufacturingDate),
};

_ReceivePurchaseOrderRequestDto _$ReceivePurchaseOrderRequestDtoFromJson(
  Map<String, dynamic> json,
) => _ReceivePurchaseOrderRequestDto(
  referenceNumber: json['referenceNumber'] as String?,
  notes: json['notes'] as String?,
  receivedAt: DateTime.parse(json['receivedAt'] as String),
  lines: (json['lines'] as List<dynamic>)
      .map(
        (e) => ReceivePurchaseOrderLineRequestDto.fromJson(
          e as Map<String, dynamic>,
        ),
      )
      .toList(),
);

Map<String, dynamic> _$ReceivePurchaseOrderRequestDtoToJson(
  _ReceivePurchaseOrderRequestDto instance,
) => <String, dynamic>{
  'referenceNumber': _trimNullable(instance.referenceNumber),
  'notes': _trimNullable(instance.notes),
  'receivedAt': instance.receivedAt.toIso8601String(),
  'lines': _receiveLineRequestDtosToJson(instance.lines),
};
