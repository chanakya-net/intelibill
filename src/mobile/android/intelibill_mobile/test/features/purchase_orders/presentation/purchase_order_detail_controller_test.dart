import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/get_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_detail_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:mocktail/mocktail.dart';

class MockGetPurchaseOrder extends Mock implements GetPurchaseOrder {}

void main() {
  late MockGetPurchaseOrder getPurchaseOrder;

  ProviderContainer makeContainer() {
    return ProviderContainer(
      overrides: [getPurchaseOrderProvider.overrideWithValue(getPurchaseOrder)],
    );
  }

  setUp(() {
    getPurchaseOrder = MockGetPurchaseOrder();
  });

  test('loads purchase-order detail on initial build', () async {
    when(() => getPurchaseOrder(any())).thenAnswer((_) async => _detail());

    final container = makeContainer();
    addTearDown(container.dispose);
    container.listen(
      purchaseOrderDetailControllerProvider('po-1'),
      (_, _) {},
      fireImmediately: true,
    );

    await Future<void>.delayed(const Duration(milliseconds: 10));

    final state = container.read(purchaseOrderDetailControllerProvider('po-1'));
    expect(state.isLoading, isFalse);
    expect(state.detail?.purchaseOrderId, 'po-1');
    expect(state.failure, isNull);
  });

  test('starts in loading state', () {
    when(() => getPurchaseOrder(any())).thenAnswer((_) async => _detail());

    final container = makeContainer();
    addTearDown(container.dispose);

    final state = container.read(purchaseOrderDetailControllerProvider('po-1'));
    expect(state.isLoading, isTrue);
    expect(state.detail, isNull);
  });

  test('calls same purchase order id on retry and refresh', () async {
    when(() => getPurchaseOrder(any())).thenAnswer((_) async => _detail());

    final container = makeContainer();
    addTearDown(container.dispose);
    container.listen(purchaseOrderDetailControllerProvider('po-1'), (_, _) {});
    await Future<void>.delayed(const Duration(milliseconds: 10));
    clearInteractions(getPurchaseOrder);

    await container
        .read(purchaseOrderDetailControllerProvider('po-1').notifier)
        .refresh();

    verify(() => getPurchaseOrder('po-1')).called(1);
    verifyNever(() => getPurchaseOrder('po-2'));
  });

  test('retries with the same id and clears data on not-found', () async {
    var calls = 0;
    when(() => getPurchaseOrder(any())).thenAnswer((_) async {
      calls += 1;
      if (calls == 1) {
        return _detail();
      }
      throw AppException(failure: const Failure.notFound());
    });

    final container = makeContainer();
    addTearDown(container.dispose);
    container.listen(
      purchaseOrderDetailControllerProvider('po-1'),
      (_, _) {},
      fireImmediately: true,
    );
    await Future<void>.delayed(const Duration(milliseconds: 10));
    expect(
      container.read(purchaseOrderDetailControllerProvider('po-1')).detail,
      isNotNull,
    );

    await container
        .read(purchaseOrderDetailControllerProvider('po-1').notifier)
        .refresh();
    await Future<void>.delayed(const Duration(milliseconds: 10));

    final state = container.read(purchaseOrderDetailControllerProvider('po-1'));
    expect(state.detail, isNull);
    expect(state.failure, isA<NotFoundFailure>());
  });
}

PurchaseOrder _detail() {
  return PurchaseOrder(
    purchaseOrderId: 'po-1',
    purchaseOrderNumber: 'PO-2026-001',
    status: PurchaseOrderStatus.placed,
    supplierId: 'supplier-1',
    orderDate: DateTime(2026, 7, 11),
    expectedDeliveryDate: DateTime(2026, 7, 13),
    supplierReferenceNumber: 'SRN-1',
    notes: 'Top priority',
    lines: const [
      PurchaseOrderLine(
        lineId: 'line-1',
        itemId: 'item-1',
        description: 'Widget A',
        expectedQuantity: 10,
        receivedQuantity: 5,
        remainingQuantity: 5,
        unitCost: 2.5,
        lineTotal: 25,
      ),
      PurchaseOrderLine(
        lineId: 'line-2',
        itemId: 'item-2',
        description: 'Widget B',
        expectedQuantity: 8,
        receivedQuantity: 2,
        remainingQuantity: 6,
        unitCost: 3.25,
        lineTotal: 26,
      ),
    ],
    expectedTotal: 1450.5,
    createdAt: DateTime(2026, 7, 1, 8, 30),
    supplierName: 'Acme Supplies',
    supplierReference: 'AC-77',
    receivedQuantity: 7,
  );
}
