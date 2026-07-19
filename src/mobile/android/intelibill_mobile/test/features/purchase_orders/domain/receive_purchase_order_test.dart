import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/receive_purchase_order_input.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/repositories/purchase_order_repository.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/receive_purchase_order.dart';
import 'package:mocktail/mocktail.dart';

class MockPurchaseOrderRepository extends Mock
    implements PurchaseOrderRepository {}

void main() {
  setUpAll(() {
    registerFallbackValue(
      ReceivePurchaseOrderInput(
        receivedAt: DateTime.utc(2026, 7, 19),
        lines: const [],
      ),
    );
  });

  late MockPurchaseOrderRepository repository;
  late ReceivePurchaseOrder useCase;

  setUp(() {
    repository = MockPurchaseOrderRepository();
    useCase = ReceivePurchaseOrder(repository);
  });

  test('delegates id and input to the repository', () async {
    final input = ReceivePurchaseOrderInput(
      referenceNumber: 'PO-REF',
      notes: 'Manual',
      receivedAt: DateTime.utc(2026, 7, 19),
      lines: const [
        ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR-1',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 1,
          unitCost: 1,
          mrp: 1,
          salesPrice: 1,
          taxRatePercent: 0,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        ),
      ],
    );
    final updated = _receivedPurchaseOrder();
    when(
      () => repository.receive('po-1', input),
    ).thenAnswer((_) async => updated);

    final result = await useCase('po-1', input);

    expect(result.purchaseOrderId, 'po-1');
    expect(result.status, PurchaseOrderStatus.received);
    verify(() => repository.receive('po-1', input)).called(1);
  });

  test('propagates repository exception', () async {
    final input = ReceivePurchaseOrderInput(
      receivedAt: DateTime.utc(2026, 7, 19),
      lines: const [
        ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR-1',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 1,
          unitCost: 1,
          mrp: 1,
          salesPrice: 1,
          taxRatePercent: 0,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        ),
      ],
    );

    when(() => repository.receive(any(), any())).thenThrow(
      AppException(failure: const Failure.unknown(message: 'fail')),
    );

    await expectLater(
      () => useCase('po-1', input),
      throwsA(isA<AppException>()),
    );
  });
}

PurchaseOrder _receivedPurchaseOrder() {
  return PurchaseOrder(
    purchaseOrderId: 'po-1',
    purchaseOrderNumber: 'PO-2026-001',
    status: PurchaseOrderStatus.received,
    lines: const [
      PurchaseOrderLine(
        lineId: 'line-1',
        itemId: 'item-1',
        description: 'Widget',
        expectedQuantity: 10,
        receivedQuantity: 5,
        remainingQuantity: 5,
        unitCost: 20,
        lineTotal: 100,
      ),
    ],
    expectedTotal: 100,
    createdAt: DateTime.utc(2026, 7),
    receivedQuantity: 5,
  );
}
