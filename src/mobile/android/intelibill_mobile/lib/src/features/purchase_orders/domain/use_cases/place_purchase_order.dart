import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/repositories/purchase_order_repository.dart';

class PlacePurchaseOrder {
  const PlacePurchaseOrder(this._repository);

  final PurchaseOrderRepository _repository;

  Future<PurchaseOrder> call(String purchaseOrderId) {
    return _repository.place(purchaseOrderId);
  }
}
