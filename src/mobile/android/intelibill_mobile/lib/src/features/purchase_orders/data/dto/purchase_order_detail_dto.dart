import 'package:freezed_annotation/freezed_annotation.dart';

part 'purchase_order_detail_dto.freezed.dart';
part 'purchase_order_detail_dto.g.dart';

@freezed
sealed class PurchaseOrderLineDto with _$PurchaseOrderLineDto {
  const factory PurchaseOrderLineDto({
    required String lineId,
    required String itemId,
    required String description,
    required int expectedQuantity,
    required int receivedQuantity,
    required int remainingQuantity,
    required double unitCost,
    required double lineTotal,
  }) = _PurchaseOrderLineDto;

  factory PurchaseOrderLineDto.fromJson(Map<String, dynamic> json) =>
      _$PurchaseOrderLineDtoFromJson(json);
}

@freezed
sealed class PurchaseOrderReceiptLineDto with _$PurchaseOrderReceiptLineDto {
  const factory PurchaseOrderReceiptLineDto({
    required String receiptLineId,
    required String purchaseOrderLineId,
    required String itemId,
    required String inventoryBatchId,
    String? batchNumber,
    bool? batchVoided,
    required String stockTransactionId,
    required double quantity,
    required double totalPurchaseCost,
    required double unitCost,
    required double mrp,
    required double salesPrice,
    required double taxRatePercent,
    required bool taxIncluded,
    required bool purchaseTaxIncluded,
  }) = _PurchaseOrderReceiptLineDto;

  factory PurchaseOrderReceiptLineDto.fromJson(Map<String, dynamic> json) =>
      _$PurchaseOrderReceiptLineDtoFromJson(json);
}

@freezed
sealed class PurchaseOrderReceiptDto with _$PurchaseOrderReceiptDto {
  const factory PurchaseOrderReceiptDto({
    required String receiptId,
    required String receiptNumber,
    required DateTime receivedAt,
    String? referenceNumber,
    String? notes,
    required String receivedByUserId,
    String? receivedByDisplayName,
    required List<PurchaseOrderReceiptLineDto> lines,
  }) = _PurchaseOrderReceiptDto;

  factory PurchaseOrderReceiptDto.fromJson(Map<String, dynamic> json) =>
      _$PurchaseOrderReceiptDtoFromJson(json);
}

@freezed
sealed class PurchaseOrderDetailDto with _$PurchaseOrderDetailDto {
  const factory PurchaseOrderDetailDto({
    required String purchaseOrderId,
    required String purchaseOrderNumber,
    required String status,
    String? supplierId,
    String? orderDate,
    String? expectedDeliveryDate,
    String? supplierReferenceNumber,
    String? notes,
    required List<PurchaseOrderLineDto> lines,
    required double expectedTotal,
    required DateTime createdAt,
    String? supplierName,
    String? supplierReference,
    required int receivedQuantity,
    String? cancellationReason,
    String? closedAt,
    String? closedBy,
    String? closeReason,
    List<PurchaseOrderReceiptDto>? receipts,
  }) = _PurchaseOrderDetailDto;

  factory PurchaseOrderDetailDto.fromJson(Map<String, dynamic> json) =>
      _$PurchaseOrderDetailDtoFromJson(json);
}
