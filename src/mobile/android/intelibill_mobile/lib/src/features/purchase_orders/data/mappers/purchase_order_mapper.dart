import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_list_item_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_page_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_list_item.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_page.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';

class PurchaseOrderMapper {
  const PurchaseOrderMapper._();

  static PurchaseOrderListItem toDomain(PurchaseOrderListItemDto dto) {
    return PurchaseOrderListItem(
      purchaseOrderId: dto.purchaseOrderId,
      purchaseOrderNumber: dto.purchaseOrderNumber,
      status: PurchaseOrderStatus.fromWire(dto.status),
      supplierName: dto.supplierName,
      supplierReference: dto.supplierReference,
      lineCount: dto.lineCount,
      expectedQuantity: dto.expectedQuantity,
      receivedQuantity: dto.receivedQuantity,
      expectedTotal: dto.expectedTotal,
      createdAt: dto.createdAt.toLocal(),
    );
  }

  static PurchaseOrderPage pageToDomain(PurchaseOrderPageDto dto) {
    return PurchaseOrderPage(
      items: dto.items.map(toDomain).toList(),
      totalCount: dto.totalCount,
      pageNumber: dto.pageNumber,
      pageSize: dto.pageSize,
    );
  }
}
