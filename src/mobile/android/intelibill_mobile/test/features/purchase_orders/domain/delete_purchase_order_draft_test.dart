import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/repositories/purchase_order_repository.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/delete_purchase_order_draft.dart';
import 'package:mocktail/mocktail.dart';

class _MockPurchaseOrderRepository extends Mock
    implements PurchaseOrderRepository {}

void main() {
  test(
    'DeletePurchaseOrderDraft delegates the draft ID to its repository',
    () async {
      final repository = _MockPurchaseOrderRepository();
      final useCase = DeletePurchaseOrderDraft(repository);
      when(() => repository.deleteDraft('po-1')).thenAnswer((_) async {});

      await useCase('po-1');

      verify(() => repository.deleteDraft('po-1')).called(1);
    },
  );
}
