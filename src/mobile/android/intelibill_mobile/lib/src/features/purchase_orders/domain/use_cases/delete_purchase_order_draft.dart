import 'package:intelibill_mobile/src/features/purchase_orders/domain/repositories/purchase_order_repository.dart';

class DeletePurchaseOrderDraft {
  const DeletePurchaseOrderDraft(this._repository);

  final PurchaseOrderRepository _repository;

  Future<void> call(String purchaseOrderId) {
    return _repository.deleteDraft(purchaseOrderId);
  }
}
