import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_filters.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_page.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/repositories/purchase_order_repository.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/close_purchase_order.dart';

class _Repository implements PurchaseOrderRepository {
  @override
  Future<PurchaseOrderPage> getPurchaseOrders(PurchaseOrderFilters filters) {
    throw UnimplementedError();
  }

  @override
  Future<PurchaseOrder> getPurchaseOrder(String purchaseOrderId) {
    throw UnimplementedError();
  }

  @override
  Future<PurchaseOrder> createDraft(PurchaseOrderDraft draft) {
    throw UnimplementedError();
  }

  @override
  Future<PurchaseOrder> updateDraft(
    String purchaseOrderId,
    PurchaseOrderDraft draft,
  ) {
    throw UnimplementedError();
  }

  @override
  Future<PurchaseOrder> receive(
    String purchaseOrderId,
    input,
  ) {
    throw UnimplementedError();
  }

  @override
  Future<PurchaseOrder> cancel(String purchaseOrderId, String reason) {
    throw UnimplementedError();
  }

  String? id;
  String? reason;

  @override
  Future<PurchaseOrder> close(
    String purchaseOrderId,
    String closeReason,
  ) async {
    id = purchaseOrderId;
    reason = closeReason;
    throw UnimplementedError();
  }
}

void main() {
  test('delegates purchase order id and reason to the repository', () async {
    final repository = _Repository();
    final useCase = ClosePurchaseOrder(repository);

    await expectLater(
      useCase('po-1', 'Discontinued'),
      throwsUnimplementedError,
    );
    expect(repository.id, 'po-1');
    expect(repository.reason, 'Discontinued');
  });
}
