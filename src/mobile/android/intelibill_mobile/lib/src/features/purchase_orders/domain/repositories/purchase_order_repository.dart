import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_filters.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_page.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';

interface class PurchaseOrderRepository {
  Future<PurchaseOrderPage> getPurchaseOrders(PurchaseOrderFilters filters) {
    throw UnimplementedError();
  }

  Future<PurchaseOrder> getPurchaseOrder(String purchaseOrderId) {
    throw UnimplementedError();
  }
}
