import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/repositories/purchase_order_repository.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/cancel_purchase_order.dart';
import 'package:mocktail/mocktail.dart';

class MockPurchaseOrderRepository extends Mock
    implements PurchaseOrderRepository {}

void main() {
  late MockPurchaseOrderRepository mockRepository;
  late CancelPurchaseOrder useCase;

  setUp(() {
    mockRepository = MockPurchaseOrderRepository();
    useCase = CancelPurchaseOrder(mockRepository);
  });

  group('CancelPurchaseOrder', () {
    test('calls repository.cancel with id and reason', () async {
      final cancelled = _cancelledPO();
      when(
        () => mockRepository.cancel('po-1', 'No longer needed'),
      ).thenAnswer((_) async => cancelled);

      final result = await useCase('po-1', 'No longer needed');

      expect(result.purchaseOrderId, 'po-1');
      expect(result.status, PurchaseOrderStatus.cancelled);
      expect(result.cancellationReason, 'No longer needed');
      verify(() => mockRepository.cancel('po-1', 'No longer needed')).called(1);
    });

    test('accepts 1-character reason', () async {
      final cancelled = _cancelledPO();
      when(
        () => mockRepository.cancel('po-1', 'X'),
      ).thenAnswer((_) async => cancelled);

      await useCase('po-1', 'X');

      verify(() => mockRepository.cancel('po-1', 'X')).called(1);
    });

    test('accepts 500-character reason', () async {
      final reason = 'x' * 500;
      final cancelled = _cancelledPO();
      when(
        () => mockRepository.cancel('po-1', reason),
      ).thenAnswer((_) async => cancelled);

      await useCase('po-1', reason);

      verify(() => mockRepository.cancel('po-1', reason)).called(1);
    });

    test('propagates repository errors', () async {
      when(
        () => mockRepository.cancel(any(), any()),
      ).thenThrow(Exception('cancel failed'));

      expect(
        () => useCase('po-1', 'No longer needed'),
        throwsException,
      );
    });
  });
}

PurchaseOrder _cancelledPO() {
  return PurchaseOrder(
    purchaseOrderId: 'po-1',
    purchaseOrderNumber: 'PO-001',
    status: PurchaseOrderStatus.cancelled,
    lines: const [
      PurchaseOrderLine(
        lineId: 'line-1',
        itemId: 'item-1',
        description: 'Widget',
        expectedQuantity: 10,
        receivedQuantity: 0,
        remainingQuantity: 10,
        unitCost: 10.0,
        lineTotal: 100.0,
      ),
    ],
    expectedTotal: 100.0,
    createdAt: DateTime.utc(2026, 7, 1),
    cancellationReason: 'No longer needed',
  );
}
