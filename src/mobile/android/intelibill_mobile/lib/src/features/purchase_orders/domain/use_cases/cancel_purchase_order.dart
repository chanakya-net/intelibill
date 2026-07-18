import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/repositories/purchase_order_repository.dart';

class CancelPurchaseOrder {
  const CancelPurchaseOrder(this._repository);

  final PurchaseOrderRepository _repository;

  Future<PurchaseOrder> call(String purchaseOrderId, String reason) {
    return _repository.cancel(purchaseOrderId, reason);
  }
}
