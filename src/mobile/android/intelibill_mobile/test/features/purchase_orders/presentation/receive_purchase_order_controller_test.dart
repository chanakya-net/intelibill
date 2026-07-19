import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/generated_item_barcode.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/product_details.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/generate_item_barcode.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/get_product_details.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/items_controller.dart';
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
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_orders_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/receive_purchase_order_controller.dart';
import 'package:mocktail/mocktail.dart';

class _MockGetPurchaseOrder extends Mock implements GetPurchaseOrder {}

class _MockReceivePurchaseOrder extends Mock implements ReceivePurchaseOrder {}

class _MockGetPurchaseOrders extends Mock implements GetPurchaseOrders {}

class _MockGenerateItemBarcode extends Mock implements GenerateItemBarcode {}

class _MockGetProductDetails extends Mock implements GetProductDetails {}

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
  late _MockGenerateItemBarcode generateItemBarcode;
  late _MockGetProductDetails getProductDetails;

  setUp(() {
    getPurchaseOrder = _MockGetPurchaseOrder();
    receivePurchaseOrder = _MockReceivePurchaseOrder();
    generateItemBarcode = _MockGenerateItemBarcode();
    getProductDetails = _MockGetProductDetails();
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
      expect(state.lines[0].barcode, '');
      expect(state.lines[0].batchNumber, 'BN-line-1');
      expect(state.lines[1].purchaseOrderLineId, 'line-3');
      expect(state.lines[1].quantity, 6);
      expect(state.lines[1].isSelected, isTrue);
      expect(state.lines[1].totalPurchaseCost, 90);
      expect(state.lines[1].barcode, '');
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

  test(
    'generates barcode without applying until controller caller does',
    () async {
      when(
        () => generateItemBarcode(),
      ).thenAnswer(
        (_) async => const GeneratedItemBarcode(barcode: 'IB-000001'),
      );
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
        generateItemBarcode: generateItemBarcode,
      );
      addTearDown(container.dispose);
      _watchReceiveController(container);
      container.read(receivePurchaseOrderControllerProvider('po-1'));
      await Future<void>.delayed(const Duration(milliseconds: 20));
      final controller = container.read(
        receivePurchaseOrderControllerProvider('po-1').notifier,
      );

      final generated = await controller.generateItemBarcodeForLine('line-1');
      expect(generated, 'IB-000001');
      expect(
        container
            .read(receivePurchaseOrderControllerProvider('po-1'))
            .lines
            .single
            .barcode,
        '',
      );

      controller.applyGeneratedBarcode('line-1', generated!);
      expect(
        container
            .read(receivePurchaseOrderControllerProvider('po-1'))
            .lines
            .single
            .barcode,
        'IB-000001',
      );
      verify(() => generateItemBarcode()).called(1);
    },
  );

  test('guards duplicate barcode-generation requests per line', () async {
    final generated = Completer<GeneratedItemBarcode>();
    when(() => generateItemBarcode()).thenAnswer((_) => generated.future);
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
      generateItemBarcode: generateItemBarcode,
    );
    addTearDown(container.dispose);
    _watchReceiveController(container);
    container.read(receivePurchaseOrderControllerProvider('po-1'));
    await Future<void>.delayed(const Duration(milliseconds: 20));

    final controller = container.read(
      receivePurchaseOrderControllerProvider('po-1').notifier,
    );
    final first = controller.generateItemBarcodeForLine('line-1');
    final second = controller.generateItemBarcodeForLine('line-1');
    generated.complete(const GeneratedItemBarcode(barcode: 'IB-000001'));

    await first;
    await second;
    expect(
      container
          .read(receivePurchaseOrderControllerProvider('po-1'))
          .barcodeGenerationLineIds,
      isEmpty,
    );
    verify(() => generateItemBarcode()).called(1);
  });

  test(
    'preserves manual barcode on barcode-generation failure',
    () async {
      when(
        () => generateItemBarcode(),
      ).thenThrow(
        AppException(failure: const Failure.network(message: 'offline')),
      );
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
        generateItemBarcode: generateItemBarcode,
      );
      addTearDown(container.dispose);
      _watchReceiveController(container);
      container.read(receivePurchaseOrderControllerProvider('po-1'));
      await Future<void>.delayed(const Duration(milliseconds: 20));
      final controller = container.read(
        receivePurchaseOrderControllerProvider('po-1').notifier,
      );

      final generated = await controller.generateItemBarcodeForLine('line-1');
      final state = container.read(
        receivePurchaseOrderControllerProvider('po-1'),
      );
      expect(generated, isNull);
      expect(state.lines.single.barcode, '');
      expect(state.barcodeGenerationFailures['line-1'], contains('offline'));
    },
  );

  test(
    'allows generation retry after failure while preserving barcode',
    () async {
      var attempt = 0;
      when(() => generateItemBarcode()).thenAnswer((_) async {
        attempt += 1;
        if (attempt == 1) {
          throw AppException(
            failure: const Failure.network(message: 'offline'),
          );
        }
        return const GeneratedItemBarcode(barcode: 'IB-000001');
      });
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
        generateItemBarcode: generateItemBarcode,
      );
      addTearDown(container.dispose);
      _watchReceiveController(container);
      container.read(receivePurchaseOrderControllerProvider('po-1'));
      await Future<void>.delayed(const Duration(milliseconds: 20));
      final controller = container.read(
        receivePurchaseOrderControllerProvider('po-1').notifier,
      );

      expect(await controller.generateItemBarcodeForLine('line-1'), isNull);
      await Future<void>.delayed(const Duration(milliseconds: 20));
      expect(
        container
            .read(
              receivePurchaseOrderControllerProvider('po-1'),
            )
            .barcodeGenerationFailures,
        containsPair('line-1', isNotNull),
      );

      container
          .read(receivePurchaseOrderControllerProvider('po-1').notifier)
          .updateBarcode('line-1', '');
      final generated = await controller.generateItemBarcodeForLine('line-1');
      expect(generated, 'IB-000001');

      container
          .read(receivePurchaseOrderControllerProvider('po-1').notifier)
          .applyGeneratedBarcode('line-1', generated!);

      final state = container.read(
        receivePurchaseOrderControllerProvider('po-1'),
      );
      expect(attempt, 2);
      expect(state.lines.single.barcode, 'IB-000001');
      expect(state.barcodeGenerationFailures, isEmpty);
    },
  );

  test('tracks generation loading independently for each line', () async {
    var nextGeneration = 0;
    final first = Completer<GeneratedItemBarcode>();
    final second = Completer<GeneratedItemBarcode>();
    when(() => generateItemBarcode()).thenAnswer((_) {
      nextGeneration += 1;
      return nextGeneration == 1 ? first.future : second.future;
    });
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
            expectedQuantity: 12,
            receivedQuantity: 5,
            remainingQuantity: 7,
            unitCost: 20,
            lineTotal: 140,
          ),
        ],
      ),
    );
    final container = _makeContainer(
      getPurchaseOrder: getPurchaseOrder,
      receivePurchaseOrder: receivePurchaseOrder,
      generateItemBarcode: generateItemBarcode,
    );
    addTearDown(container.dispose);
    _watchReceiveController(container);
    container.read(receivePurchaseOrderControllerProvider('po-1'));
    await Future<void>.delayed(const Duration(milliseconds: 20));
    final controller = container.read(
      receivePurchaseOrderControllerProvider('po-1').notifier,
    );

    final line1 = controller.generateItemBarcodeForLine('line-1');
    await Future<void>.delayed(const Duration(milliseconds: 20));
    container
        .read(receivePurchaseOrderControllerProvider('po-1').notifier)
        .updateBarcode('line-1', '');
    container
        .read(receivePurchaseOrderControllerProvider('po-1').notifier)
        .updateBarcode('line-2', '');
    final line2 = controller.generateItemBarcodeForLine('line-2');

    await Future<void>.delayed(const Duration(milliseconds: 20));
    expect(
      container
          .read(receivePurchaseOrderControllerProvider('po-1'))
          .barcodeGenerationLineIds,
      contains('line-1'),
    );
    expect(
      container
          .read(receivePurchaseOrderControllerProvider('po-1'))
          .barcodeGenerationLineIds,
      contains('line-2'),
    );

    first.complete(const GeneratedItemBarcode(barcode: 'IB-000001'));
    second.complete(const GeneratedItemBarcode(barcode: 'IB-000002'));
    await line1;
    await line2;
    container
        .read(receivePurchaseOrderControllerProvider('po-1').notifier)
        .applyGeneratedBarcode('line-1', 'IB-000001');
    container
        .read(receivePurchaseOrderControllerProvider('po-1').notifier)
        .applyGeneratedBarcode('line-2', 'IB-000002');

    expect(
      container
          .read(receivePurchaseOrderControllerProvider('po-1'))
          .barcodeGenerationLineIds,
      isEmpty,
    );
    expect(
      container
          .read(receivePurchaseOrderControllerProvider('po-1'))
          .lines[0]
          .barcode,
      'IB-000001',
    );
    expect(
      container
          .read(receivePurchaseOrderControllerProvider('po-1'))
          .lines[1]
          .barcode,
      'IB-000002',
    );
    verify(() => generateItemBarcode()).called(2);
  });

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

  test(
    'validates price/tax boundaries and blocks invalid submission',
    () async {
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

      final state = container.read(
        receivePurchaseOrderControllerProvider('po-1'),
      );
      expect(state.failure, isA<ValidationFailure>());
      expect(state.isSubmitting, isFalse);
    },
  );

  test(
    'validates every receipt field and expands the first invalid line',
    () async {
      when(() => getPurchaseOrder('po-1')).thenAnswer(
        (_) async => _detail(
          lines: const [
            PurchaseOrderLine(
              lineId: 'line-1',
              itemId: 'item-1',
              description: 'A',
              expectedQuantity: 2,
              receivedQuantity: 0,
              remainingQuantity: 2,
              unitCost: 10,
              lineTotal: 20,
            ),
            PurchaseOrderLine(
              lineId: 'line-2',
              itemId: 'item-2',
              description: 'B',
              expectedQuantity: 2,
              receivedQuantity: 0,
              remainingQuantity: 2,
              unitCost: 10,
              lineTotal: 20,
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

      controller.updateBarcode('line-1', 'x' * 121);
      controller.updateBatchNumber('line-1', 'x' * 81);
      controller.updateUnitPurchaseCost('line-1', '-1');
      controller.updateTotalPurchaseCost('line-1', '-1');
      controller.updateMrp('line-1', '1');
      controller.updateSalesPrice('line-1', '2');
      controller.updateTaxRatePercent('line-1', '101');
      controller.updateManufacturingDate('line-1', DateTime(2026, 8, 1));
      controller.updateExpiryDate('line-1', DateTime(2026, 7, 1));
      controller.updateBarcode('line-2', '');

      expect(await controller.submit(), isFalse);
      verifyNever(() => receivePurchaseOrder('po-1', any()));
      final state = container.read(
        receivePurchaseOrderControllerProvider('po-1'),
      );
      expect(
        state.lineErrors['line-1']!.keys,
        containsAll([
          'barcode',
          'batchNumber',
          'unitCost',
          'totalPurchaseCost',
          'salesPrice',
          'taxRatePercent',
          'expiryDate',
        ]),
      );
      expect(state.expandedLineId, 'line-1');
      expect(state.focusedLineId, 'barcode');
    },
  );

  test(
    'maps server line validation errors while preserving receipt drafts',
    () async {
      when(() => getPurchaseOrder('po-1')).thenAnswer(
        (_) async => _detail(
          lines: const [
            PurchaseOrderLine(
              lineId: 'line-1',
              itemId: 'item-1',
              description: 'A',
              expectedQuantity: 2,
              receivedQuantity: 0,
              remainingQuantity: 2,
              unitCost: 10,
              lineTotal: 20,
            ),
            PurchaseOrderLine(
              lineId: 'line-2',
              itemId: 'item-2',
              description: 'B',
              expectedQuantity: 2,
              receivedQuantity: 0,
              remainingQuantity: 2,
              unitCost: 10,
              lineTotal: 20,
            ),
          ],
        ),
      );
      const failure = Failure.validation(
        errors: {
          'Lines[0].Barcode': ['Barcode is already used.'],
          'Lines[1].ExpiryDate': ['Expiry date is invalid.'],
        },
      );
      when(() => receivePurchaseOrder('po-1', any())).thenThrow(
        AppException(failure: failure),
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
      controller.updateBarcode('line-1', 'DRAFT-BARCODE');

      await expectLater(controller.submit(), throwsA(isA<AppException>()));
      final state = container.read(
        receivePurchaseOrderControllerProvider('po-1'),
      );
      expect(state.lines.first.barcode, 'DRAFT-BARCODE');
      expect(
        state.lineErrors['line-1']!['barcode'],
        'Barcode is already used.',
      );
      expect(
        state.lineErrors['line-2']!['expiryDate'],
        'Expiry date is invalid.',
      );
      expect(state.expandedLineId, 'line-1');
      expect(state.focusedLineId, 'barcode');
    },
  );

  test(
    'prefill does not block later lines when first lookup is slow',
    () async {
      final line1Completer = Completer<ProductDetails>();
      final line2Completer = Completer<ProductDetails>();

      when(() => getPurchaseOrder('po-1')).thenAnswer(
        (_) async => _detail(
          lines: [
            const PurchaseOrderLine(
              lineId: 'line-1',
              itemId: 'item-1',
              description: 'Item1',
              expectedQuantity: 5,
              receivedQuantity: 0,
              remainingQuantity: 5,
              unitCost: 10,
              lineTotal: 50,
            ),
            const PurchaseOrderLine(
              lineId: 'line-2',
              itemId: 'item-2',
              description: 'Item2',
              expectedQuantity: 5,
              receivedQuantity: 0,
              remainingQuantity: 5,
              unitCost: 10,
              lineTotal: 50,
            ),
          ],
        ),
      );

      when(() => getProductDetails(name: 'Item1')).thenAnswer(
        (_) => line1Completer.future,
      );
      when(() => getProductDetails(name: 'Item2')).thenAnswer(
        (_) => line2Completer.future,
      );

      final container = _makeContainer(
        getPurchaseOrder: getPurchaseOrder,
        receivePurchaseOrder: receivePurchaseOrder,
        getProductDetails: getProductDetails,
      );
      addTearDown(container.dispose);
      _watchReceiveController(container);

      container.read(receivePurchaseOrderControllerProvider('po-1'));
      // Give time for load and initial prefill state to be set
      await Future<void>.delayed(const Duration(milliseconds: 100));

      var state = container.read(receivePurchaseOrderControllerProvider('po-1'));
      // Both should be loading or one completed; both start in parallel
      expect(
        state.prefillLoadingLineIds.contains('line-1') || state.lines[0].mrp != 0 ||
        state.prefillFailures.containsKey('line-1'),
        isTrue,
        reason: 'Line 1 should have prefill attempt in progress or completed/failed',
      );
      expect(
        state.prefillLoadingLineIds.contains('line-2') || state.lines[1].mrp != 0 ||
        state.prefillFailures.containsKey('line-2'),
        isTrue,
        reason: 'Line 2 should have prefill attempt in progress or completed/failed',
      );

      // Complete line 2 faster
      line2Completer.complete(
        const ProductDetails(
          name: 'Item2',
          description: 'Item2 desc',
          uom: 'units',
          costPrice: 30,
          mrp: 50,
          salesPrice: 40,
        ),
      );
      await Future<void>.delayed(const Duration(milliseconds: 50));

      state = container.read(receivePurchaseOrderControllerProvider('po-1'));
      // Line 2 should have mrp set, line 1 still loading/pending
      expect(state.lines[1].mrp, 50);
      expect(!state.prefillLoadingLineIds.contains('line-2'), isTrue);

      // Now complete line 1
      line1Completer.complete(
        const ProductDetails(
          name: 'Item1',
          description: 'Item1 desc',
          uom: 'units',
          costPrice: 60,
          mrp: 100,
          salesPrice: 80,
        ),
      );
      await Future<void>.delayed(const Duration(milliseconds: 50));

      state = container.read(receivePurchaseOrderControllerProvider('po-1'));
      expect(state.lines[0].mrp, 100);
      expect(state.prefillLoadingLineIds, isEmpty);
    },
  );

  test(
    'user edit to MRP blocks later async prefill',
    () async {
      final prefillCompleter = Completer<ProductDetails>();

      when(() => getPurchaseOrder('po-1')).thenAnswer(
        (_) async => _detail(
          lines: const [
            PurchaseOrderLine(
              lineId: 'line-1',
              itemId: 'item-1',
              description: 'Widget',
              expectedQuantity: 5,
              receivedQuantity: 0,
              remainingQuantity: 5,
              unitCost: 10,
              lineTotal: 50,
            ),
          ],
        ),
      );

      when(() => getProductDetails(name: 'Widget')).thenAnswer(
        (_) => prefillCompleter.future,
      );

      final container = _makeContainer(
        getPurchaseOrder: getPurchaseOrder,
        receivePurchaseOrder: receivePurchaseOrder,
        getProductDetails: getProductDetails,
      );
      addTearDown(container.dispose);
      _watchReceiveController(container);

      container.read(receivePurchaseOrderControllerProvider('po-1'));
      await Future<void>.delayed(const Duration(milliseconds: 30));

      container
          .read(receivePurchaseOrderControllerProvider('po-1').notifier)
          .updateMrp('line-1', '200');

      await Future<void>.delayed(const Duration(milliseconds: 30));
      prefillCompleter.complete(
        const ProductDetails(
          name: 'Widget',
          description: 'Widget desc',
          uom: 'units',
          costPrice: 60,
          mrp: 100,
          salesPrice: 80,
          taxIncluded: false,
        ),
      );

      await Future<void>.delayed(const Duration(milliseconds: 30));

      final state =
          container.read(receivePurchaseOrderControllerProvider('po-1'));
      expect(
        state.lines[0].mrp,
        200,
        reason: 'User-edited MRP should not be overwritten by prefill',
      );
    },
  );
}

ProviderContainer _makeContainer({
  required _MockGetPurchaseOrder getPurchaseOrder,
  required _MockReceivePurchaseOrder receivePurchaseOrder,
  _MockGetPurchaseOrders? getPurchaseOrders,
  _MockGenerateItemBarcode? generateItemBarcode,
  GetProductDetails? getProductDetails,
}) {
  final mockGetProductDetails = getProductDetails ?? _MockGetProductDetails();
  if (mockGetProductDetails is _MockGetProductDetails && getProductDetails == null) {
    when(
      () => mockGetProductDetails(
        name: any(named: 'name'),
        barcode: any(named: 'barcode'),
      ),
    ).thenThrow(Exception('Product not found'));
  }

  return ProviderContainer(
    overrides: [
      getPurchaseOrderProvider.overrideWithValue(getPurchaseOrder),
      receivePurchaseOrderProvider.overrideWithValue(receivePurchaseOrder),
      getProductDetailsProvider.overrideWithValue(mockGetProductDetails),
      if (generateItemBarcode != null)
        generateItemBarcodeProvider.overrideWithValue(generateItemBarcode),
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
