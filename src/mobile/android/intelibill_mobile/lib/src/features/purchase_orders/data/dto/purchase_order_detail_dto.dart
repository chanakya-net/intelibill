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
  }) = _PurchaseOrderDetailDto;

  factory PurchaseOrderDetailDto.fromJson(Map<String, dynamic> json) =>
      _$PurchaseOrderDetailDtoFromJson(json);
}
