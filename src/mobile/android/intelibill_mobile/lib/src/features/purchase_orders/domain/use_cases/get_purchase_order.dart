import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/repositories/purchase_order_repository.dart';

class GetPurchaseOrder {
  const GetPurchaseOrder(this._repository);

  final PurchaseOrderRepository _repository;

  Future<PurchaseOrder> call(String purchaseOrderId) {
    return _repository.getPurchaseOrder(purchaseOrderId);
  }
}
