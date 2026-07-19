import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_filters.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_page.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/cancel_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/get_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/get_purchase_orders.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/place_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_detail_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_orders_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockGetPurchaseOrder extends Mock implements GetPurchaseOrder {}

class MockCancelPurchaseOrder extends Mock implements CancelPurchaseOrder {}

class MockGetPurchaseOrders extends Mock implements GetPurchaseOrders {}

class MockPlacePurchaseOrder extends Mock implements PlacePurchaseOrder {}

void main() {
  setUpAll(() {
    registerFallbackValue(const PurchaseOrderFilters());
  });

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

  test(
    'invalidates purchaseOrdersControllerProvider on successful cancellation',
    () async {
      final mockCancel = MockCancelPurchaseOrder();
      final mockGetOrders = MockGetPurchaseOrders();

      when(() => getPurchaseOrder(any())).thenAnswer((_) async => _detail());
      when(
        () => mockCancel(any(), any()),
      ).thenAnswer((_) async => _detail(status: PurchaseOrderStatus.cancelled));
      when(() => mockGetOrders(any())).thenAnswer(
        (_) async => const PurchaseOrderPage(
          items: [],
          totalCount: 0,
          pageNumber: 1,
          pageSize: 20,
        ),
      );

      final container = ProviderContainer(
        overrides: [
          getPurchaseOrderProvider.overrideWithValue(getPurchaseOrder),
          cancelPurchaseOrderProvider.overrideWithValue(mockCancel),
          getPurchaseOrdersProvider.overrideWithValue(mockGetOrders),
        ],
      );
      addTearDown(container.dispose);

      // Keep providers alive by listening to them
      container.listen(
        purchaseOrderDetailControllerProvider('po-1'),
        (_, __) {},
      );
      container.listen(purchaseOrdersControllerProvider, (_, __) {});
      await Future<void>.delayed(const Duration(milliseconds: 10));

      // Reset mockGetOrders to track rebuild after invalidation
      clearInteractions(mockGetOrders);

      // Call cancel
      await container
          .read(purchaseOrderDetailControllerProvider('po-1').notifier)
          .cancel('No longer needed');

      // The list controller should be invalidated/rebuilt, triggering getPurchaseOrders again
      await Future<void>.delayed(const Duration(milliseconds: 10));
      verify(() => mockGetOrders(any())).called(1);
    },
  );

  test(
    'handles lifecycle conflict: preserves detail and retains failure on failed cancel',
    () async {
      final mockCancel = MockCancelPurchaseOrder();

      // Initial fetch succeeds
      when(
        () => getPurchaseOrder('po-1'),
      ).thenAnswer((_) async => _detail(status: PurchaseOrderStatus.placed));
      // Cancel throws AppException
      final conflictFailure = const Failure.server(message: 'Conflict status');
      when(
        () => mockCancel('po-1', any()),
      ).thenThrow(AppException(failure: conflictFailure));

      final container = ProviderContainer(
        overrides: [
          getPurchaseOrderProvider.overrideWithValue(getPurchaseOrder),
          cancelPurchaseOrderProvider.overrideWithValue(mockCancel),
        ],
      );
      addTearDown(container.dispose);

      // Keep detail provider alive by listening to it
      container.listen(
        purchaseOrderDetailControllerProvider('po-1'),
        (_, __) {},
      );
      await Future<void>.delayed(const Duration(milliseconds: 10));

      expect(
        container.read(purchaseOrderDetailControllerProvider('po-1')).detail,
        isNotNull,
      );

      // 2. Mock refresh/load during cancel failure to fail as well
      // "preserve existing detail if that refresh fails"
      when(
        () => getPurchaseOrder('po-1'),
      ).thenThrow(AppException(failure: const Failure.network()));

      // 3. Trigger cancel
      try {
        await container
            .read(purchaseOrderDetailControllerProvider('po-1').notifier)
            .cancel('Conflict reason');
      } catch (_) {} // ignore rethrown error

      // 4. Assertions
      final state = container.read(
        purchaseOrderDetailControllerProvider('po-1'),
      );
      // - preserves displayed content ("retains detail")
      expect(state.detail, isNotNull);
      // - cancel failure is visible
      expect(state.cancelState.failure, conflictFailure);
    },
  );

  test(
    'places with authoritative detail and invalidates the directory',
    () async {
      final placePurchaseOrder = MockPlacePurchaseOrder();
      final getPurchaseOrders = MockGetPurchaseOrders();
      final placed = _detail(status: PurchaseOrderStatus.placed);
      when(() => getPurchaseOrder('po-1')).thenAnswer(
        (_) async => _detail(status: PurchaseOrderStatus.draft),
      );
      when(() => placePurchaseOrder('po-1')).thenAnswer((_) async => placed);
      when(() => getPurchaseOrders(any())).thenAnswer(
        (_) async => const PurchaseOrderPage(
          items: [],
          totalCount: 0,
          pageNumber: 1,
          pageSize: 20,
        ),
      );
      final container = ProviderContainer(
        overrides: [
          getPurchaseOrderProvider.overrideWithValue(getPurchaseOrder),
          placePurchaseOrderProvider.overrideWithValue(placePurchaseOrder),
          getPurchaseOrdersProvider.overrideWithValue(getPurchaseOrders),
        ],
      );
      addTearDown(container.dispose);
      container.listen(
        purchaseOrderDetailControllerProvider('po-1'),
        (_, __) {},
      );
      container.listen(purchaseOrdersControllerProvider, (_, __) {});
      await Future<void>.delayed(const Duration(milliseconds: 10));
      clearInteractions(getPurchaseOrders);

      await container
          .read(purchaseOrderDetailControllerProvider('po-1').notifier)
          .place();
      await Future<void>.delayed(const Duration(milliseconds: 10));

      expect(
        container.read(purchaseOrderDetailControllerProvider('po-1')).detail,
        placed,
      );
      verify(() => placePurchaseOrder('po-1')).called(1);
      verify(() => getPurchaseOrders(any())).called(1);
    },
  );

  test('refreshes authoritative detail after a failed place', () async {
    final placePurchaseOrder = MockPlacePurchaseOrder();
    final refreshed = _detail(status: PurchaseOrderStatus.placed);
    var calls = 0;
    when(() => getPurchaseOrder('po-1')).thenAnswer((_) async {
      calls++;
      return calls == 1
          ? _detail(status: PurchaseOrderStatus.draft)
          : refreshed;
    });
    when(() => placePurchaseOrder('po-1')).thenThrow(
      AppException(failure: const Failure.server(statusCode: 409)),
    );
    final container = ProviderContainer(
      overrides: [
        getPurchaseOrderProvider.overrideWithValue(getPurchaseOrder),
        placePurchaseOrderProvider.overrideWithValue(placePurchaseOrder),
      ],
    );
    addTearDown(container.dispose);
    container.listen(purchaseOrderDetailControllerProvider('po-1'), (_, __) {});
    await Future<void>.delayed(const Duration(milliseconds: 10));

    await expectLater(
      container
          .read(purchaseOrderDetailControllerProvider('po-1').notifier)
          .place(),
      throwsA(isA<AppException>()),
    );

    final state = container.read(purchaseOrderDetailControllerProvider('po-1'));
    expect(state.detail, refreshed);
    expect(state.placeState.failure, isA<ServerFailure>());
  });
}

PurchaseOrder _detail({
  PurchaseOrderStatus status = PurchaseOrderStatus.placed,
}) {
  return PurchaseOrder(
    purchaseOrderId: 'po-1',
    purchaseOrderNumber: 'PO-2026-001',
    status: status,
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
