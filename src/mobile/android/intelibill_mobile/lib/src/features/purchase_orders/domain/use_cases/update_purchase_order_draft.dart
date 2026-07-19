import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/repositories/purchase_order_repository.dart';

class UpdatePurchaseOrderDraft {
  const UpdatePurchaseOrderDraft(this._repository);

  final PurchaseOrderRepository _repository;

  Future<PurchaseOrder> call(
    String purchaseOrderId,
    PurchaseOrderDraft draft,
  ) {
    return _repository.updateDraft(purchaseOrderId, draft);
  }
}
