import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/repositories/purchase_order_repository.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/update_purchase_order_draft.dart';
import 'package:mocktail/mocktail.dart';

class _MockRepository extends Mock implements PurchaseOrderRepository {}

void main() {
  test('delegates the route ID and draft to the repository', () async {
    final repository = _MockRepository();
    const draft = PurchaseOrderDraft(notes: 'Updated');
    final expected = PurchaseOrder(
      purchaseOrderId: 'po-1',
      purchaseOrderNumber: 'PO-1',
      status: PurchaseOrderStatus.draft,
      lines: const [],
      expectedTotal: 0,
      createdAt: DateTime(2026, 7, 19),
    );
    when(() => repository.updateDraft('po-1', draft)).thenAnswer(
      (_) async => expected,
    );

    final result = await UpdatePurchaseOrderDraft(repository)('po-1', draft);

    expect(result, expected);
    verify(() => repository.updateDraft('po-1', draft)).called(1);
  });
}
