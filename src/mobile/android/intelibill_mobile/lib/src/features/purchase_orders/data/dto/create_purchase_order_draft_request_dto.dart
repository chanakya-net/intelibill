import 'package:freezed_annotation/freezed_annotation.dart';

part 'create_purchase_order_draft_request_dto.freezed.dart';
part 'create_purchase_order_draft_request_dto.g.dart';

@freezed
sealed class CreatePurchaseOrderDraftLineRequestDto
    with _$CreatePurchaseOrderDraftLineRequestDto {
  const factory CreatePurchaseOrderDraftLineRequestDto({
    required String itemId,
    required String description,
    required int expectedQuantity,
    required double unitCost,
  }) = _CreatePurchaseOrderDraftLineRequestDto;

  factory CreatePurchaseOrderDraftLineRequestDto.fromJson(
    Map<String, dynamic> json,
  ) => _$CreatePurchaseOrderDraftLineRequestDtoFromJson(json);
}

@freezed
sealed class CreatePurchaseOrderDraftRequestDto
    with _$CreatePurchaseOrderDraftRequestDto {
  const factory CreatePurchaseOrderDraftRequestDto({
    String? supplierId,
    String? orderDate,
    String? expectedDeliveryDate,
    String? supplierReferenceNumber,
    String? notes,
    String? supplierName,
    String? supplierReference,
    @Default([]) List<CreatePurchaseOrderDraftLineRequestDto> lines,
  }) = _CreatePurchaseOrderDraftRequestDto;

  factory CreatePurchaseOrderDraftRequestDto.fromJson(
    Map<String, dynamic> json,
  ) => _$CreatePurchaseOrderDraftRequestDtoFromJson(json);
}
