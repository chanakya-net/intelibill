import 'package:freezed_annotation/freezed_annotation.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_list_item_dto.dart';

part 'purchase_order_page_dto.freezed.dart';
part 'purchase_order_page_dto.g.dart';

@freezed
sealed class PurchaseOrderPageDto with _$PurchaseOrderPageDto {
  const factory PurchaseOrderPageDto({
    @Default([]) List<PurchaseOrderListItemDto> items,
    required int totalCount,
    required int pageNumber,
    required int pageSize,
  }) = _PurchaseOrderPageDto;

  factory PurchaseOrderPageDto.fromJson(Map<String, dynamic> json) =>
      _$PurchaseOrderPageDtoFromJson(json);
}
