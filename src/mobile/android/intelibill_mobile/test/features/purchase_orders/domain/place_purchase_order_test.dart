import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/repositories/purchase_order_repository.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/place_purchase_order.dart';
import 'package:mocktail/mocktail.dart';

class MockPurchaseOrderRepository extends Mock
    implements PurchaseOrderRepository {}

void main() {
  late MockPurchaseOrderRepository mockRepository;
  late PlacePurchaseOrder useCase;

  setUp(() {
    mockRepository = MockPurchaseOrderRepository();
    useCase = PlacePurchaseOrder(mockRepository);
  });

  group('PlacePurchaseOrder', () {
    test('calls repository.place with id', () async {
      final placed = _placedPO();
      when(
        () => mockRepository.place('po-1'),
      ).thenAnswer((_) async => placed);

      final result = await useCase('po-1');

      expect(result.purchaseOrderId, 'po-1');
      expect(result.status, PurchaseOrderStatus.placed);
      verify(() => mockRepository.place('po-1')).called(1);
    });

    test('propagates repository errors', () async {
      when(
        () => mockRepository.place(any()),
      ).thenThrow(Exception('place failed'));

      expect(
        () => useCase('po-1'),
        throwsException,
      );
    });
  });
}

PurchaseOrder _placedPO() {
  return PurchaseOrder(
    purchaseOrderId: 'po-1',
    purchaseOrderNumber: 'PO-001',
    status: PurchaseOrderStatus.placed,
    lines: const [],
    expectedTotal: 100,
    createdAt: DateTime.utc(2026, 7),
  );
}
