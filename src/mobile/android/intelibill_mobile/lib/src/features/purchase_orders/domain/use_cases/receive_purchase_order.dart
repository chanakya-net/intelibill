import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/receive_purchase_order_input.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/repositories/purchase_order_repository.dart';

class ReceivePurchaseOrder {
  const ReceivePurchaseOrder(this._repository);

  final PurchaseOrderRepository _repository;

  Future<PurchaseOrder> call(
    String purchaseOrderId,
    ReceivePurchaseOrderInput input,
  ) {
    return _repository.receive(purchaseOrderId, input);
  }
}
