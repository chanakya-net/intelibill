import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_filters.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_page.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/repositories/purchase_order_repository.dart';

class GetPurchaseOrders {
  const GetPurchaseOrders(this._repository);

  final PurchaseOrderRepository _repository;

  Future<PurchaseOrderPage> call(PurchaseOrderFilters filters) {
    return _repository.getPurchaseOrders(filters);
  }
}
