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

_PurchaseOrderReceiptLineDto _$PurchaseOrderReceiptLineDtoFromJson(
  Map<String, dynamic> json,
) => _PurchaseOrderReceiptLineDto(
  receiptLineId: json['receiptLineId'] as String,
  purchaseOrderLineId: json['purchaseOrderLineId'] as String,
  itemId: json['itemId'] as String,
  inventoryBatchId: json['inventoryBatchId'] as String,
  batchNumber: json['batchNumber'] as String?,
  batchVoided: json['batchVoided'] as bool?,
  stockTransactionId: json['stockTransactionId'] as String,
  quantity: (json['quantity'] as num).toDouble(),
  totalPurchaseCost: (json['totalPurchaseCost'] as num).toDouble(),
  unitCost: (json['unitCost'] as num).toDouble(),
  mrp: (json['mrp'] as num).toDouble(),
  salesPrice: (json['salesPrice'] as num).toDouble(),
  taxRatePercent: (json['taxRatePercent'] as num).toDouble(),
  taxIncluded: json['taxIncluded'] as bool,
  purchaseTaxIncluded: json['purchaseTaxIncluded'] as bool,
);

Map<String, dynamic> _$PurchaseOrderReceiptLineDtoToJson(
  _PurchaseOrderReceiptLineDto instance,
) => <String, dynamic>{
  'receiptLineId': instance.receiptLineId,
  'purchaseOrderLineId': instance.purchaseOrderLineId,
  'itemId': instance.itemId,
  'inventoryBatchId': instance.inventoryBatchId,
  'batchNumber': instance.batchNumber,
  'batchVoided': instance.batchVoided,
  'stockTransactionId': instance.stockTransactionId,
  'quantity': instance.quantity,
  'totalPurchaseCost': instance.totalPurchaseCost,
  'unitCost': instance.unitCost,
  'mrp': instance.mrp,
  'salesPrice': instance.salesPrice,
  'taxRatePercent': instance.taxRatePercent,
  'taxIncluded': instance.taxIncluded,
  'purchaseTaxIncluded': instance.purchaseTaxIncluded,
};

_PurchaseOrderReceiptDto _$PurchaseOrderReceiptDtoFromJson(
  Map<String, dynamic> json,
) => _PurchaseOrderReceiptDto(
  receiptId: json['receiptId'] as String,
  receiptNumber: json['receiptNumber'] as String,
  receivedAt: DateTime.parse(json['receivedAt'] as String),
  referenceNumber: json['referenceNumber'] as String?,
  notes: json['notes'] as String?,
  receivedByUserId: json['receivedByUserId'] as String,
  receivedByDisplayName: json['receivedByDisplayName'] as String?,
  lines: (json['lines'] as List<dynamic>)
      .map(
        (e) => PurchaseOrderReceiptLineDto.fromJson(e as Map<String, dynamic>),
      )
      .toList(),
);

Map<String, dynamic> _$PurchaseOrderReceiptDtoToJson(
  _PurchaseOrderReceiptDto instance,
) => <String, dynamic>{
  'receiptId': instance.receiptId,
  'receiptNumber': instance.receiptNumber,
  'receivedAt': instance.receivedAt.toIso8601String(),
  'referenceNumber': instance.referenceNumber,
  'notes': instance.notes,
  'receivedByUserId': instance.receivedByUserId,
  'receivedByDisplayName': instance.receivedByDisplayName,
  'lines': instance.lines,
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
  receivedQuantity: (json['receivedQuantity'] as num).toInt(),
  cancellationReason: json['cancellationReason'] as String?,
  closedAt: json['closedAt'] as String?,
  closedBy: json['closedBy'] as String?,
  closeReason: json['closeReason'] as String?,
  receipts: (json['receipts'] as List<dynamic>?)
      ?.map((e) => PurchaseOrderReceiptDto.fromJson(e as Map<String, dynamic>))
      .toList(),
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
  'cancellationReason': instance.cancellationReason,
  'closedAt': instance.closedAt,
  'closedBy': instance.closedBy,
  'closeReason': instance.closeReason,
  'receipts': instance.receipts,
};
