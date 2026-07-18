import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_detail_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_list_item_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_page_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
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

  static PurchaseOrderLine toLineDomain(PurchaseOrderLineDto dto) {
    return PurchaseOrderLine(
      lineId: dto.lineId,
      itemId: dto.itemId,
      description: dto.description,
      expectedQuantity: dto.expectedQuantity,
      receivedQuantity: dto.receivedQuantity,
      remainingQuantity: dto.remainingQuantity,
      unitCost: dto.unitCost,
      lineTotal: dto.lineTotal,
    );
  }

  static PurchaseOrder detailToDomain(PurchaseOrderDetailDto dto) {
    return PurchaseOrder(
      purchaseOrderId: dto.purchaseOrderId,
      purchaseOrderNumber: dto.purchaseOrderNumber,
      status: PurchaseOrderStatus.fromWire(dto.status),
      supplierId: dto.supplierId,
      orderDate: _parseDateOnly(dto.orderDate),
      expectedDeliveryDate: _parseDateOnly(dto.expectedDeliveryDate),
      supplierReferenceNumber: dto.supplierReferenceNumber,
      notes: dto.notes,
      lines: dto.lines.map(toLineDomain).toList(),
      expectedTotal: dto.expectedTotal,
      createdAt: dto.createdAt.toLocal(),
      supplierName: dto.supplierName,
      supplierReference: dto.supplierReference,
      receivedQuantity: dto.receivedQuantity,
    );
  }

  static DateTime? _parseDateOnly(String? value) {
    if (value == null) return null;
    return DateTime.parse('${value}T00:00:00');
  }
}
