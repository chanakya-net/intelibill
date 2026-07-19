import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_filters.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_page.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/receive_purchase_order_input.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/get_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/get_purchase_orders.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/receive_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/receive_purchase_order_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_orders_controller.dart';
import 'package:mocktail/mocktail.dart';

class _MockGetPurchaseOrder extends Mock implements GetPurchaseOrder {}

class _MockReceivePurchaseOrder extends Mock implements ReceivePurchaseOrder {}

class _MockGetPurchaseOrders extends Mock implements GetPurchaseOrders {}

void main() {
  final defaultBatchGenerator =
      ReceivePurchaseOrderController.batchNumberGenerator;

  setUpAll(() {
    registerFallbackValue(const PurchaseOrderFilters());
    registerFallbackValue(
      ReceivePurchaseOrderInput(
        receivedAt: DateTime.utc(2026, 7, 19),
        lines: [],
      ),
    );
  });

  late _MockGetPurchaseOrder getPurchaseOrder;
  late _MockReceivePurchaseOrder receivePurchaseOrder;

  setUp(() {
    getPurchaseOrder = _MockGetPurchaseOrder();
    receivePurchaseOrder = _MockReceivePurchaseOrder();
    ReceivePurchaseOrderController.batchNumberGenerator = _generateBatch;
  });

  tearDown(() {
    ReceivePurchaseOrderController.batchNumberGenerator = defaultBatchGenerator;
  });

  test(
    'loads only lines with remaining quantity and preselects full remaining',
    () async {
      when(() => getPurchaseOrder('po-1')).thenAnswer(
        (_) async => _detail(
          lines: [
            const PurchaseOrderLine(
              lineId: 'line-1',
              itemId: 'item-1',
              description: 'Widget A',
              expectedQuantity: 10,
              receivedQuantity: 7,
              remainingQuantity: 3,
              unitCost: 10,
              lineTotal: 100,
            ),
            const PurchaseOrderLine(
              lineId: 'line-2',
              itemId: 'item-2',
              description: 'Widget B',
              expectedQuantity: 5,
              receivedQuantity: 5,
              remainingQuantity: 0,
              unitCost: 20,
              lineTotal: 100,
            ),
            const PurchaseOrderLine(
              lineId: 'line-3',
              itemId: 'item-3',
              description: 'Widget C',
              expectedQuantity: 7,
              receivedQuantity: 1,
              remainingQuantity: 6,
              unitCost: 15,
              lineTotal: 90,
            ),
          ],
        ),
      );

      final container = _makeContainer(
        getPurchaseOrder: getPurchaseOrder,
        receivePurchaseOrder: receivePurchaseOrder,
      );
      addTearDown(container.dispose);
      _watchReceiveController(container);
      container.read(receivePurchaseOrderControllerProvider('po-1'));

      await Future<void>.delayed(const Duration(milliseconds: 20));
      final state = container.read(
        receivePurchaseOrderControllerProvider('po-1'),
      );

      expect(state.lines, hasLength(2));
      expect(state.lines[0].purchaseOrderLineId, 'line-1');
      expect(state.lines[0].quantity, 3);
      expect(state.lines[0].remainingQuantity, 3);
      expect(state.lines[0].isSelected, isTrue);
      expect(state.lines[0].totalPurchaseCost, 30);
      expect(state.lines[0].barcode, 'item-1');
      expect(state.lines[0].batchNumber, 'BN-line-1');
      expect(state.lines[1].purchaseOrderLineId, 'line-3');
      expect(state.lines[1].quantity, 6);
      expect(state.lines[1].isSelected, isTrue);
      expect(state.lines[1].totalPurchaseCost, 90);
      expect(state.lines[1].barcode, 'item-3');
      expect(state.lines[1].batchNumber, 'BN-line-3');
      expect(state.receivedAt, isNotNull);
      expect(state.receivedAt!.timeZoneOffset, Duration.zero);
    },
  );

  test(
    'updates selected receipt lines and integer quantities immutably',
    () async {
      ReceivePurchaseOrderInput? submittedInput;
      when(() => getPurchaseOrder('po-1')).thenAnswer(
        (_) async => _detail(
          lines: const [
            PurchaseOrderLine(
              lineId: 'line-1',
              itemId: 'item-1',
              description: 'Widget A',
              expectedQuantity: 10,
              receivedQuantity: 7,
              remainingQuantity: 3,
              unitCost: 10,
              lineTotal: 100,
            ),
            PurchaseOrderLine(
              lineId: 'line-2',
              itemId: 'item-2',
              description: 'Widget B',
              expectedQuantity: 10,
              receivedQuantity: 6,
              remainingQuantity: 4,
              unitCost: 15,
              lineTotal: 150,
            ),
          ],
        ),
      );
      when(() => receivePurchaseOrder('po-1', any())).thenAnswer((invocation) {
        submittedInput =
            invocation.positionalArguments[1] as ReceivePurchaseOrderInput;
        return Future<PurchaseOrder>.value(
          _detail(lines: const [], status: PurchaseOrderStatus.received),
        );
      });
      final container = _makeContainer(
        getPurchaseOrder: getPurchaseOrder,
        receivePurchaseOrder: receivePurchaseOrder,
      );
      addTearDown(container.dispose);
      _watchReceiveController(container);
      container.read(receivePurchaseOrderControllerProvider('po-1'));
      await Future<void>.delayed(const Duration(milliseconds: 20));
      final controller = container.read(
        receivePurchaseOrderControllerProvider('po-1').notifier,
      );

      controller.setLineSelected('line-1', isSelected: false);
      controller.updateQuantity('line-2', '2');
      var state = container.read(
        receivePurchaseOrderControllerProvider('po-1'),
      );
      expect(state.lines.map((line) => line.isSelected), [false, true]);
      expect(state.lines.map((line) => line.quantity), [3, 2]);
      expect(state.selectedLineCount, 1);
      expect(state.selectedQuantity, 2);
      expect(state.selectedPurchaseCost, 30);

      controller.setLineSelected('line-1', isSelected: true);
      state = container.read(receivePurchaseOrderControllerProvider('po-1'));
      expect(state.lines.map((line) => line.isSelected), [true, true]);
      expect(state.selectedLineCount, 2);
      expect(state.selectedQuantity, 5);
      expect(state.selectedPurchaseCost, 60);

      controller.setLineSelected('line-1', isSelected: false);
      await controller.submit();
      expect(submittedInput!.lines, hasLength(1));
      expect(submittedInput!.lines.single.purchaseOrderLineId, 'line-2');
      expect(submittedInput!.lines.single.quantity, 2);
      expect(submittedInput!.lines.single.totalPurchaseCost, 30);
    },
  );

  test('rejects zero, noninteger, and over-remaining quantities', () async {
    when(() => getPurchaseOrder('po-1')).thenAnswer(
      (_) async => _detail(
        lines: const [
          PurchaseOrderLine(
            lineId: 'line-1',
            itemId: 'item-1',
            description: 'Widget A',
            expectedQuantity: 10,
            receivedQuantity: 7,
            remainingQuantity: 3,
            unitCost: 10,
            lineTotal: 100,
          ),
        ],
      ),
    );
    final container = _makeContainer(
      getPurchaseOrder: getPurchaseOrder,
      receivePurchaseOrder: receivePurchaseOrder,
    );
    addTearDown(container.dispose);
    _watchReceiveController(container);
    container.read(receivePurchaseOrderControllerProvider('po-1'));
    await Future<void>.delayed(const Duration(milliseconds: 20));
    final controller = container.read(
      receivePurchaseOrderControllerProvider('po-1').notifier,
    );

    for (final value in ['0', '1.5', '4']) {
      controller.updateQuantity('line-1', value);
      final state = container.read(
        receivePurchaseOrderControllerProvider('po-1'),
      );
      expect(state.failure, isA<ValidationFailure>());
      expect(state.lines.single.quantity, 3);
    }
  });

  test(
    'submits a full-remaining payload and invalidates purchase order list',
    () async {
      final mockGetOrders = _MockGetPurchaseOrders();
      var receiveCalls = 0;
      ReceivePurchaseOrderInput? submittedInput;
      when(() => getPurchaseOrder('po-1')).thenAnswer(
        (_) async => _detail(
          lines: [
            const PurchaseOrderLine(
              lineId: 'line-1',
              itemId: 'item-1',
              description: 'Widget A',
              expectedQuantity: 10,
              receivedQuantity: 7,
              remainingQuantity: 3,
              unitCost: 10,
              lineTotal: 100,
            ),
            const PurchaseOrderLine(
              lineId: 'line-2',
              itemId: 'item-2',
              description: 'Widget B',
              expectedQuantity: 5,
              receivedQuantity: 2,
              remainingQuantity: 3,
              unitCost: 20,
              lineTotal: 100,
            ),
          ],
        ),
      );
      when(
        () => mockGetOrders(const PurchaseOrderFilters()),
      ).thenAnswer(
        (_) async => const PurchaseOrderPage(
          items: [],
          totalCount: 0,
          pageNumber: 1,
          pageSize: 20,
        ),
      );

      when(
        () => receivePurchaseOrder('po-1', any()),
      ).thenAnswer(
        (invocation) {
          receiveCalls += 1;
          submittedInput =
              invocation.positionalArguments[1] as ReceivePurchaseOrderInput;
          return Future<PurchaseOrder>.value(
            _detail(lines: const [], status: PurchaseOrderStatus.received),
          );
        },
      );

      final container = _makeContainer(
        getPurchaseOrder: getPurchaseOrder,
        receivePurchaseOrder: receivePurchaseOrder,
        getPurchaseOrders: mockGetOrders,
      );
      addTearDown(container.dispose);
      _watchReceiveController(container);
      container.listen(
        purchaseOrdersControllerProvider,
        (_, __) {},
        fireImmediately: true,
      );
      container.read(receivePurchaseOrderControllerProvider('po-1'));

      await Future<void>.delayed(const Duration(milliseconds: 20));
      clearInteractions(mockGetOrders);
      clearInteractions(receivePurchaseOrder);
      final stateBeforeSubmit = container.read(
        receivePurchaseOrderControllerProvider('po-1'),
      );
      expect(stateBeforeSubmit.lines, hasLength(2));
      expect(stateBeforeSubmit.failure, isNull);

      await container
          .read(receivePurchaseOrderControllerProvider('po-1').notifier)
          .submit();
      final stateAfterSubmit = container.read(
        receivePurchaseOrderControllerProvider('po-1'),
      );
      expect(stateAfterSubmit.failure, isNull);
      expect(receiveCalls, 1);
      final captured = submittedInput;
      expect(captured, isNotNull);
      expect(captured!.lines, hasLength(2));
      expect(captured.lines.map((line) => line.quantity), [3, 3]);
      await Future<void>.delayed(const Duration(milliseconds: 20));
      verify(() => mockGetOrders(const PurchaseOrderFilters())).called(1);
      expect(
        container
            .read(receivePurchaseOrderControllerProvider('po-1'))
            .detail
            ?.status,
        PurchaseOrderStatus.received,
      );
    },
  );

  test('does nothing when submitted with no eligible lines', () async {
    when(() => getPurchaseOrder('po-1')).thenAnswer(
      (_) async => _detail(
        lines: [
          const PurchaseOrderLine(
            lineId: 'line-1',
            itemId: 'item-1',
            description: 'Widget A',
            expectedQuantity: 10,
            receivedQuantity: 10,
            remainingQuantity: 0,
            unitCost: 10,
            lineTotal: 100,
          ),
        ],
      ),
    );

    final container = _makeContainer(
      getPurchaseOrder: getPurchaseOrder,
      receivePurchaseOrder: receivePurchaseOrder,
    );
    addTearDown(container.dispose);
    _watchReceiveController(container);
    container.read(receivePurchaseOrderControllerProvider('po-1'));
    await Future<void>.delayed(const Duration(milliseconds: 20));

    await container
        .read(receivePurchaseOrderControllerProvider('po-1').notifier)
        .submit();

    verifyNever(() => receivePurchaseOrder('po-1', any()));
    final state = container.read(
      receivePurchaseOrderControllerProvider('po-1'),
    );
    expect(state.failure, isA<ValidationFailure>());
    expect(state.lines, isEmpty);
  });

  test(
    'rejects an empty selected receipt before calling the use case',
    () async {
      when(() => getPurchaseOrder('po-1')).thenAnswer(
        (_) async => _detail(
          lines: const [
            PurchaseOrderLine(
              lineId: 'line-1',
              itemId: 'item-1',
              description: 'Widget A',
              expectedQuantity: 10,
              receivedQuantity: 7,
              remainingQuantity: 3,
              unitCost: 10,
              lineTotal: 100,
            ),
          ],
        ),
      );
      final container = _makeContainer(
        getPurchaseOrder: getPurchaseOrder,
        receivePurchaseOrder: receivePurchaseOrder,
      );
      addTearDown(container.dispose);
      _watchReceiveController(container);
      container.read(receivePurchaseOrderControllerProvider('po-1'));
      await Future<void>.delayed(const Duration(milliseconds: 20));
      final controller = container.read(
        receivePurchaseOrderControllerProvider('po-1').notifier,
      );

      controller.setLineSelected('line-1', isSelected: false);
      await controller.submit();

      verifyNever(() => receivePurchaseOrder('po-1', any()));
      expect(
        container.read(receivePurchaseOrderControllerProvider('po-1')).failure,
        isA<ValidationFailure>(),
      );
    },
  );

  test('rejects duplicate selected purchase-order line IDs', () async {
    when(() => getPurchaseOrder('po-1')).thenAnswer(
      (_) async => _detail(
        lines: const [
          PurchaseOrderLine(
            lineId: 'line-1',
            itemId: 'item-1',
            description: 'Widget A',
            expectedQuantity: 10,
            receivedQuantity: 7,
            remainingQuantity: 3,
            unitCost: 10,
            lineTotal: 100,
          ),
          PurchaseOrderLine(
            lineId: 'line-1',
            itemId: 'item-2',
            description: 'Widget B',
            expectedQuantity: 10,
            receivedQuantity: 8,
            remainingQuantity: 2,
            unitCost: 15,
            lineTotal: 150,
          ),
        ],
      ),
    );
    final container = _makeContainer(
      getPurchaseOrder: getPurchaseOrder,
      receivePurchaseOrder: receivePurchaseOrder,
    );
    addTearDown(container.dispose);
    _watchReceiveController(container);
    container.read(receivePurchaseOrderControllerProvider('po-1'));
    await Future<void>.delayed(const Duration(milliseconds: 20));

    await container
        .read(receivePurchaseOrderControllerProvider('po-1').notifier)
        .submit();

    verifyNever(() => receivePurchaseOrder('po-1', any()));
    expect(
      container.read(receivePurchaseOrderControllerProvider('po-1')).failure,
      isA<ValidationFailure>(),
    );
  });

  test('guards duplicate submissions', () async {
    final pending = Completer<PurchaseOrder>();
    when(() => getPurchaseOrder('po-1')).thenAnswer(
      (_) async => _detail(
        lines: [
          const PurchaseOrderLine(
            lineId: 'line-1',
            itemId: 'item-1',
            description: 'Widget A',
            expectedQuantity: 10,
            receivedQuantity: 7,
            remainingQuantity: 3,
            unitCost: 10,
            lineTotal: 100,
          ),
        ],
      ),
    );
    when(
      () => receivePurchaseOrder('po-1', any()),
    ).thenAnswer((_) => pending.future);

    final container = _makeContainer(
      getPurchaseOrder: getPurchaseOrder,
      receivePurchaseOrder: receivePurchaseOrder,
    );
    addTearDown(container.dispose);
    _watchReceiveController(container);
    container.read(receivePurchaseOrderControllerProvider('po-1'));
    await Future<void>.delayed(const Duration(milliseconds: 20));

    final first = container
        .read(receivePurchaseOrderControllerProvider('po-1').notifier)
        .submit();
    await container
        .read(receivePurchaseOrderControllerProvider('po-1').notifier)
        .submit();
    await Future<void>.delayed(const Duration(milliseconds: 20));

    verify(() => receivePurchaseOrder('po-1', any())).called(1);
    pending.complete(
      _detail(lines: const [], status: PurchaseOrderStatus.received),
    );
    await first;
  });

  test(
    'preserves drafts on API failure and keeps submitting state false',
    () async {
      final failure = const Failure.server(
        message: 'conflict',
        statusCode: 409,
      );
      when(() => getPurchaseOrder('po-1')).thenAnswer(
        (_) async => _detail(
          lines: [
            const PurchaseOrderLine(
              lineId: 'line-1',
              itemId: 'item-1',
              description: 'Widget A',
              expectedQuantity: 10,
              receivedQuantity: 7,
              remainingQuantity: 3,
              unitCost: 10,
              lineTotal: 100,
            ),
          ],
        ),
      );
      when(
        () => receivePurchaseOrder('po-1', any()),
      ).thenThrow(AppException(failure: failure));

      final container = _makeContainer(
        getPurchaseOrder: getPurchaseOrder,
        receivePurchaseOrder: receivePurchaseOrder,
      );
      addTearDown(container.dispose);
      _watchReceiveController(container);
      container.read(receivePurchaseOrderControllerProvider('po-1'));
      await Future<void>.delayed(const Duration(milliseconds: 20));

      final controller = container.read(
        receivePurchaseOrderControllerProvider('po-1').notifier,
      );
      controller.updateBarcode('line-1', 'CUSTOM-BARCODE');

      await expectLater(
        controller.submit(),
        throwsA(isA<AppException>()),
      );

      verify(() => getPurchaseOrder('po-1')).called(1);
      final state = container.read(
        receivePurchaseOrderControllerProvider('po-1'),
      );
      expect(state.isSubmitting, isFalse);
      expect(state.failure, failure);
      expect(state.lines.single.barcode, 'CUSTOM-BARCODE');
    },
  );

  test('validates price/tax boundaries and blocks invalid submission', () async {
    when(() => getPurchaseOrder('po-1')).thenAnswer(
      (_) async => _detail(
        lines: [
          const PurchaseOrderLine(
            lineId: 'line-1',
            itemId: 'item-1',
            description: 'Widget A',
            expectedQuantity: 10,
            receivedQuantity: 0,
            remainingQuantity: 10,
            unitCost: 50,
            lineTotal: 500,
          ),
        ],
      ),
    );
    final container = _makeContainer(
      getPurchaseOrder: getPurchaseOrder,
      receivePurchaseOrder: receivePurchaseOrder,
    );
    addTearDown(container.dispose);
    _watchReceiveController(container);
    container.read(receivePurchaseOrderControllerProvider('po-1'));
    await Future<void>.delayed(const Duration(milliseconds: 20));
    final controller = container.read(
      receivePurchaseOrderControllerProvider('po-1').notifier,
    );

    controller.updateSalesPrice('line-1', '100');
    controller.updateMrp('line-1', '80');
    await controller.submit();

    final state = container.read(receivePurchaseOrderControllerProvider('po-1'));
    expect(state.failure, isA<ValidationFailure>());
    expect(state.isSubmitting, isFalse);
  });
}

ProviderContainer _makeContainer({
  required _MockGetPurchaseOrder getPurchaseOrder,
  required _MockReceivePurchaseOrder receivePurchaseOrder,
  _MockGetPurchaseOrders? getPurchaseOrders,
}) {
  return ProviderContainer(
    overrides: [
      getPurchaseOrderProvider.overrideWithValue(getPurchaseOrder),
      receivePurchaseOrderProvider.overrideWithValue(receivePurchaseOrder),
      if (getPurchaseOrders != null)
        getPurchaseOrdersProvider.overrideWithValue(getPurchaseOrders),
    ],
  );
}

void _watchReceiveController(ProviderContainer container) {
  container.listen(
    receivePurchaseOrderControllerProvider('po-1'),
    (_, __) {},
    fireImmediately: true,
  );
}

PurchaseOrder _detail({
  PurchaseOrderStatus status = PurchaseOrderStatus.placed,
  List<PurchaseOrderLine> lines = const [],
}) {
  return PurchaseOrder(
    purchaseOrderId: 'po-1',
    purchaseOrderNumber: 'PO-2026-0001',
    status: status,
    lines: lines,
    expectedTotal: 1000,
    createdAt: DateTime.utc(2026, 7, 1),
    receivedQuantity: 0,
  );
}

String _generateBatch(PurchaseOrderLine line) => 'BN-${line.lineId}';
