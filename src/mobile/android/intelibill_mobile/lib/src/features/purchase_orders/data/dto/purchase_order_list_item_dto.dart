import 'package:freezed_annotation/freezed_annotation.dart';

part 'purchase_order_list_item_dto.freezed.dart';
part 'purchase_order_list_item_dto.g.dart';

@freezed
sealed class PurchaseOrderListItemDto with _$PurchaseOrderListItemDto {
  const factory PurchaseOrderListItemDto({
    required String purchaseOrderId,
    required String purchaseOrderNumber,
    required String status,
    String? supplierName,
    String? supplierReference,
    required int lineCount,
    required int expectedQuantity,
    required int receivedQuantity,
    required double expectedTotal,
    required DateTime createdAt,
  }) = _PurchaseOrderListItemDto;

  factory PurchaseOrderListItemDto.fromJson(Map<String, dynamic> json) =>
      _$PurchaseOrderListItemDtoFromJson(json);
}
