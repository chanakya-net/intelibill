import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/generated_item_barcode.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/item.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/generate_item_barcode.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/get_items.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/get_product_details.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/items_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/receive_purchase_order_input.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/get_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/receive_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/receive_purchase_order_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/pages/receive_purchase_order_page.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_receive_line_card.dart';
import 'package:intelibill_mobile/src/shared/barcode_scanner/barcode_scan_result.dart';
import 'package:mocktail/mocktail.dart';

class _MockGetPurchaseOrder extends Mock implements GetPurchaseOrder {}

class _MockReceivePurchaseOrder extends Mock implements ReceivePurchaseOrder {}

class _MockGenerateItemBarcode extends Mock implements GenerateItemBarcode {}

class _MockGetProductDetails extends Mock implements GetProductDetails {}

class _MockGetItems extends Mock implements GetItems {}

class _Harness {
  _Harness({
    required this.container,
    required this.router,
    required this.app,
  });

  final ProviderContainer container;
  final GoRouter router;
  final Widget app;
}

void main() {
  setUpAll(() {
    registerFallbackValue(
      ReceivePurchaseOrderInput(
        receivedAt: DateTime.utc(2026, 7, 19),
        lines: [],
      ),
    );
  });

  _Harness buildHarness({
    required GetPurchaseOrder getPurchaseOrder,
    required ReceivePurchaseOrder receivePurchaseOrder,
    Future<BarcodeScanResult?> Function(BuildContext context)? scanBarcode,
    GenerateItemBarcode? generateItemBarcode,
    GetProductDetails? getProductDetails,
    GetItems? getItems,
  }) {
    final mockGetProductDetails = getProductDetails ?? _MockGetProductDetails();
    if (mockGetProductDetails is _MockGetProductDetails &&
        getProductDetails == null) {
      when(
        () => mockGetProductDetails(
          name: any(named: 'name'),
          barcode: any(named: 'barcode'),
        ),
      ).thenThrow(Exception('Product not found'));
    }
    final mockGetItems = getItems ?? _MockGetItems();
    if (mockGetItems is _MockGetItems && getItems == null) {
      when(() => mockGetItems()).thenAnswer(
        (_) async => const [
          Item(
            itemId: 'item-1',
            name: 'Widget A',
            barcode: 'CAT-001',
            uom: 'pcs',
            isActive: true,
            currentStock: 0,
          ),
        ],
      );
    }

    final container = ProviderContainer(
      overrides: [
        getPurchaseOrderProvider.overrideWithValue(getPurchaseOrder),
        receivePurchaseOrderProvider.overrideWithValue(receivePurchaseOrder),
        getProductDetailsProvider.overrideWithValue(mockGetProductDetails),
        getItemsProvider.overrideWithValue(mockGetItems),
        if (generateItemBarcode != null)
          generateItemBarcodeProvider.overrideWithValue(generateItemBarcode),
      ],
    );
    final router = GoRouter(
      initialLocation: AppRoutes.purchaseOrderReceiveFor('po-1'),
      routes: [
        GoRoute(
          path: AppRoutes.purchaseOrderReceive,
          builder: (context, state) => ReceivePurchaseOrderPage(
            purchaseOrderId: state.pathParameters['purchaseOrderId'] ?? '',
            scanBarcode: scanBarcode,
          ),
        ),
        GoRoute(
          path: AppRoutes.purchaseOrderDetail,
          builder: (context, state) => const Scaffold(
            body: Text('detail route'),
          ),
        ),
      ],
    );

    return _Harness(
      container: container,
      router: router,
      app: UncontrolledProviderScope(
        container: container,
        child: MaterialApp.router(
          theme: AppTheme.lightTheme,
          supportedLocales: const [Locale('en', 'IN')],
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          routerConfig: router,
        ),
      ),
    );
  }

  Future<void> expandFirstLine(WidgetTester tester) async {
    await tester.tap(
      find.byKey(PurchaseOrderReceiveLineCard.cardKey('line-1')),
    );
    await tester.pumpAndSettle();
  }

  Future<void> tapLineButton(WidgetTester tester, Key buttonKey) async {
    final finder = find.byKey(buttonKey);
    await tester.ensureVisible(finder);
    await tester.pump();
    await tester.tap(finder);
    await tester.pump();
  }

  testWidgets('renders receive form with prefilled quantities', (tester) async {
    final getPurchaseOrder = _MockGetPurchaseOrder();
    final receive = _MockReceivePurchaseOrder();
    when(
      () => getPurchaseOrder('po-1'),
    ).thenAnswer(
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
            expectedQuantity: 12,
            receivedQuantity: 5,
            remainingQuantity: 7,
            unitCost: 20,
            lineTotal: 140,
          ),
        ],
      ),
    );

    final harness = buildHarness(
      getPurchaseOrder: getPurchaseOrder,
      receivePurchaseOrder: receive,
    );
    addTearDown(() {
      harness.router.dispose();
      harness.container.dispose();
    });
    await tester.pumpWidget(harness.app);
    await tester.pumpAndSettle();

    expect(find.text('Receive purchase order'), findsOneWidget);
    expect(
      find.byKey(ReceivePurchaseOrderPage.referenceFieldKey),
      findsOneWidget,
    );
    expect(find.byKey(ReceivePurchaseOrderPage.notesFieldKey), findsOneWidget);
    expect(
      tester
          .widget<FilledButton>(
            find.byKey(ReceivePurchaseOrderPage.receiveButtonKey),
          )
          .onPressed,
      isNotNull,
    );

    await tester.scrollUntilVisible(
      find.textContaining('Line count:'),
      200,
      scrollable: find.byType(Scrollable).first,
    );

    expect(find.textContaining('Line count:'), findsOneWidget);
    expect(find.textContaining('Total quantity:'), findsOneWidget);
  });

  testWidgets('selects partial lines and updates receipt summary', (
    tester,
  ) async {
    final getPurchaseOrder = _MockGetPurchaseOrder();
    final receive = _MockReceivePurchaseOrder();
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
    final harness = buildHarness(
      getPurchaseOrder: getPurchaseOrder,
      receivePurchaseOrder: receive,
    );
    addTearDown(() {
      harness.router.dispose();
      harness.container.dispose();
    });
    await tester.pumpWidget(harness.app);
    await tester.pumpAndSettle();

    expect(
      find.byKey(PurchaseOrderReceiveLineCard.selectionCheckbox('line-1')),
      findsOneWidget,
    );
    await tester.tap(
      find.byKey(PurchaseOrderReceiveLineCard.selectionCheckbox('line-1')),
    );
    await tester.scrollUntilVisible(
      find.byKey(PurchaseOrderReceiveLineCard.cardKey('line-2')),
      100,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.drag(find.byType(ListView), const Offset(0, -120));
    await tester.pumpAndSettle();
    await tester.tap(
      find.byKey(PurchaseOrderReceiveLineCard.cardKey('line-2')),
    );
    await tester.pumpAndSettle();
    expect(
      find.byKey(PurchaseOrderReceiveLineCard.quantityField('line-2')),
      findsOneWidget,
    );
    expect(find.text('Remaining: 3'), findsOneWidget);

    await tester.enterText(
      find.byKey(PurchaseOrderReceiveLineCard.quantityField('line-2')),
      '2',
    );
    await tester.pumpAndSettle();

    final state = harness.container.read(
      receivePurchaseOrderControllerProvider('po-1'),
    );
    expect(state.selectedLineCount, 1);
    expect(state.selectedQuantity, 2);
    expect(state.selectedPurchaseCost, 40);

    await tester.scrollUntilVisible(
      find.text('Line count: 1'),
      200,
      scrollable: find.byType(Scrollable).first,
    );
    expect(find.text('Line count: 1'), findsOneWidget);
    expect(find.text('Total quantity: 2'), findsOneWidget);
    expect(find.text('Total purchase cost: ₹40'), findsOneWidget);
  });

  testWidgets('submits and navigates to detail route on success', (
    tester,
  ) async {
    final getPurchaseOrder = _MockGetPurchaseOrder();
    final receive = _MockReceivePurchaseOrder();
    when(
      () => getPurchaseOrder('po-1'),
    ).thenAnswer((_) async => _detail(status: PurchaseOrderStatus.placed));
    when(() => receive('po-1', any())).thenAnswer(
      (_) async => _detail(status: PurchaseOrderStatus.received),
    );

    final harness = buildHarness(
      getPurchaseOrder: getPurchaseOrder,
      receivePurchaseOrder: receive,
    );
    addTearDown(() {
      harness.router.dispose();
      harness.container.dispose();
    });
    await tester.pumpWidget(harness.app);
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(ReceivePurchaseOrderPage.receiveButtonKey));
    await tester.pumpAndSettle();

    expect(
      harness.router.routerDelegate.currentConfiguration.uri.toString(),
      AppRoutes.purchaseOrderDetailFor('po-1'),
    );
    verify(() => receive('po-1', any())).called(1);
    expect(find.text('detail route'), findsOneWidget);
  });

  testWidgets('shows submit failure message and stays on page', (tester) async {
    final getPurchaseOrder = _MockGetPurchaseOrder();
    final receive = _MockReceivePurchaseOrder();
    when(
      () => getPurchaseOrder('po-1'),
    ).thenAnswer((_) async => _detail(status: PurchaseOrderStatus.placed));
    when(
      () => receive('po-1', any()),
    ).thenThrow(
      AppException(failure: const Failure.network(message: 'offline')),
    );

    final harness = buildHarness(
      getPurchaseOrder: getPurchaseOrder,
      receivePurchaseOrder: receive,
    );
    addTearDown(() {
      harness.router.dispose();
      harness.container.dispose();
    });
    await tester.pumpWidget(harness.app);
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(ReceivePurchaseOrderPage.receiveButtonKey));
    await tester.pumpAndSettle();

    expect(
      find.text('Could not record receipt.'),
      findsOneWidget,
    );
    expect(
      harness.router.routerDelegate.currentConfiguration.uri.toString(),
      AppRoutes.purchaseOrderReceiveFor('po-1'),
    );
  });

  testWidgets('edits the unit cost and clears selected inventory dates', (
    tester,
  ) async {
    final getPurchaseOrder = _MockGetPurchaseOrder();
    final receive = _MockReceivePurchaseOrder();
    when(() => getPurchaseOrder('po-1')).thenAnswer((_) async => _detail());
    final harness = buildHarness(
      getPurchaseOrder: getPurchaseOrder,
      receivePurchaseOrder: receive,
    );
    addTearDown(() {
      harness.router.dispose();
      harness.container.dispose();
    });
    await tester.pumpWidget(harness.app);
    await tester.pumpAndSettle();
    await tester.tap(
      find.byKey(PurchaseOrderReceiveLineCard.cardKey('line-1')),
    );
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(PurchaseOrderReceiveLineCard.unitCostField('line-1')),
      '12.50',
    );
    final controller = harness.container.read(
      receivePurchaseOrderControllerProvider('po-1').notifier,
    );
    controller.updateManufacturingDate('line-1', DateTime(2026, 7, 1));
    controller.updateExpiryDate('line-1', DateTime(2026, 8, 1));
    await tester.pumpAndSettle();

    expect(
      harness.container
          .read(receivePurchaseOrderControllerProvider('po-1'))
          .lines
          .single
          .unitPurchaseCost,
      12.5,
    );
    await tester.ensureVisible(
      find.byKey(PurchaseOrderReceiveLineCard.manufacturingDateClear('line-1')),
    );
    await tester.pumpAndSettle();
    await tester.tap(
      find.byKey(PurchaseOrderReceiveLineCard.manufacturingDateClear('line-1')),
    );
    await tester.ensureVisible(
      find.byKey(PurchaseOrderReceiveLineCard.expiryDateClear('line-1')),
    );
    await tester.pumpAndSettle();
    await tester.tap(
      find.byKey(PurchaseOrderReceiveLineCard.expiryDateClear('line-1')),
    );
    await tester.pumpAndSettle();
    final line = harness.container
        .read(receivePurchaseOrderControllerProvider('po-1'))
        .lines
        .single;
    expect(line.manufacturingDate, isNull);
    expect(line.expiryDate, isNull);
  });

  testWidgets('renders barcode scan and generate controls for each line', (
    tester,
  ) async {
    final getPurchaseOrder = _MockGetPurchaseOrder();
    final receive = _MockReceivePurchaseOrder();
    when(() => getPurchaseOrder('po-1')).thenAnswer((_) async => _detail());
    final harness = buildHarness(
      getPurchaseOrder: getPurchaseOrder,
      receivePurchaseOrder: receive,
    );
    addTearDown(() {
      harness.router.dispose();
      harness.container.dispose();
    });
    await tester.pumpWidget(harness.app);
    await tester.pumpAndSettle();
    await expandFirstLine(tester);

    expect(
      find.byKey(PurchaseOrderReceiveLineCard.scanBarcodeButton('line-1')),
      findsOneWidget,
    );
    expect(
      find.byKey(PurchaseOrderReceiveLineCard.generateBarcodeButton('line-1')),
      findsOneWidget,
    );
  });

  testWidgets('keeps barcode on null scan and allows successful retry', (
    tester,
  ) async {
    final getPurchaseOrder = _MockGetPurchaseOrder();
    final receive = _MockReceivePurchaseOrder();
    var attempts = 0;
    when(() => getPurchaseOrder('po-1')).thenAnswer((_) async => _detail());
    final harness = buildHarness(
      getPurchaseOrder: getPurchaseOrder,
      receivePurchaseOrder: receive,
      scanBarcode: (_) async {
        attempts += 1;
        return attempts == 1 ? null : const BarcodeScanResult(value: 'SC-001');
      },
    );
    addTearDown(() {
      harness.router.dispose();
      harness.container.dispose();
    });

    await tester.pumpWidget(harness.app);
    await tester.pumpAndSettle();
    await expandFirstLine(tester);

    await tapLineButton(
      tester,
      PurchaseOrderReceiveLineCard.scanBarcodeButton('line-1'),
    );
    await tester.pumpAndSettle();

    final barcodeField = find.byKey(
      PurchaseOrderReceiveLineCard.barcodeField('line-1'),
    );
    expect(find.byType(AlertDialog), findsNothing);
    expect(
      harness.container
          .read(receivePurchaseOrderControllerProvider('po-1'))
          .lines
          .single
          .barcode,
      'CAT-001',
    );
    expect(tester.widget<TextField>(barcodeField).controller!.text, 'CAT-001');

    await tapLineButton(
      tester,
      PurchaseOrderReceiveLineCard.scanBarcodeButton('line-1'),
    );
    await tester.pumpAndSettle();
    await tester.tap(
      find.descendant(
        of: find.byType(AlertDialog),
        matching: find.byType(FilledButton),
      ),
    );
    await tester.pumpAndSettle();

    expect(attempts, 2);
    expect(
      harness.container
          .read(receivePurchaseOrderControllerProvider('po-1'))
          .lines
          .single
          .barcode,
      'SC-001',
    );
    expect(tester.widget<TextField>(barcodeField).controller!.text, 'SC-001');
  });

  testWidgets('keeps existing barcode when scan replacement is cancelled', (
    tester,
  ) async {
    final getPurchaseOrder = _MockGetPurchaseOrder();
    final receive = _MockReceivePurchaseOrder();
    when(() => getPurchaseOrder('po-1')).thenAnswer((_) async => _detail());
    final harness = buildHarness(
      getPurchaseOrder: getPurchaseOrder,
      receivePurchaseOrder: receive,
      scanBarcode: (_) async => const BarcodeScanResult(value: 'SC-001'),
    );
    addTearDown(() {
      harness.router.dispose();
      harness.container.dispose();
    });

    await tester.pumpWidget(harness.app);
    await tester.pumpAndSettle();
    await expandFirstLine(tester);
    harness.container
        .read(receivePurchaseOrderControllerProvider('po-1').notifier)
        .updateBarcode('line-1', 'ITEM-001');

    await tapLineButton(
      tester,
      PurchaseOrderReceiveLineCard.scanBarcodeButton('line-1'),
    );
    await tester.pumpAndSettle();
    expect(find.byType(AlertDialog), findsOneWidget);
    await tester.tap(
      find.descendant(
        of: find.byType(AlertDialog),
        matching: find.byType(TextButton),
      ),
    );
    await tester.pumpAndSettle();

    expect(
      harness.container
          .read(receivePurchaseOrderControllerProvider('po-1'))
          .lines
          .single
          .barcode,
      'ITEM-001',
    );
  });

  testWidgets('replaces barcode on scan with confirmation for nonblank value', (
    tester,
  ) async {
    final getPurchaseOrder = _MockGetPurchaseOrder();
    final receive = _MockReceivePurchaseOrder();
    when(() => getPurchaseOrder('po-1')).thenAnswer((_) async => _detail());
    final harness = buildHarness(
      getPurchaseOrder: getPurchaseOrder,
      receivePurchaseOrder: receive,
      scanBarcode: (_) async => const BarcodeScanResult(value: 'SC-001'),
    );
    addTearDown(() {
      harness.router.dispose();
      harness.container.dispose();
    });

    await tester.pumpWidget(harness.app);
    await tester.pumpAndSettle();
    await expandFirstLine(tester);
    harness.container
        .read(receivePurchaseOrderControllerProvider('po-1').notifier)
        .updateBarcode('line-1', 'ITEM-001');

    await tapLineButton(
      tester,
      PurchaseOrderReceiveLineCard.scanBarcodeButton('line-1'),
    );
    await tester.pumpAndSettle();
    await tester.tap(
      find.descendant(
        of: find.byType(AlertDialog),
        matching: find.byType(FilledButton),
      ),
    );
    await tester.pumpAndSettle();

    expect(
      harness.container
          .read(receivePurchaseOrderControllerProvider('po-1'))
          .lines
          .single
          .barcode,
      'SC-001',
    );
    expect(find.byType(AlertDialog), findsNothing);
  });

  testWidgets('scans and applies barcode when field is empty', (tester) async {
    final getPurchaseOrder = _MockGetPurchaseOrder();
    final receive = _MockReceivePurchaseOrder();
    when(() => getPurchaseOrder('po-1')).thenAnswer((_) async => _detail());
    final harness = buildHarness(
      getPurchaseOrder: getPurchaseOrder,
      receivePurchaseOrder: receive,
      scanBarcode: (_) async => const BarcodeScanResult(value: 'SC-001'),
    );
    addTearDown(() {
      harness.router.dispose();
      harness.container.dispose();
    });

    await tester.pumpWidget(harness.app);
    await tester.pumpAndSettle();
    await expandFirstLine(tester);
    await tester.enterText(
      find.byKey(PurchaseOrderReceiveLineCard.barcodeField('line-1')),
      '',
    );
    await tester.pumpAndSettle();

    await tapLineButton(
      tester,
      PurchaseOrderReceiveLineCard.scanBarcodeButton('line-1'),
    );
    await tester.pumpAndSettle();

    expect(
      harness.container
          .read(receivePurchaseOrderControllerProvider('po-1'))
          .lines
          .single
          .barcode,
      'SC-001',
    );
    expect(find.byType(AlertDialog), findsNothing);
  });

  testWidgets(
    'asks for confirmation before replacing existing barcode with generated one',
    (tester) async {
      final getPurchaseOrder = _MockGetPurchaseOrder();
      final receive = _MockReceivePurchaseOrder();
      final generateItemBarcode = _MockGenerateItemBarcode();
      when(
        () => generateItemBarcode(),
      ).thenAnswer(
        (_) async => const GeneratedItemBarcode(barcode: 'IB-000001'),
      );
      when(() => getPurchaseOrder('po-1')).thenAnswer((_) async => _detail());
      final harness = buildHarness(
        getPurchaseOrder: getPurchaseOrder,
        receivePurchaseOrder: receive,
        generateItemBarcode: generateItemBarcode,
      );
      addTearDown(() {
        harness.router.dispose();
        harness.container.dispose();
      });

      await tester.pumpWidget(harness.app);
      await tester.pumpAndSettle();
      await expandFirstLine(tester);
      await tapLineButton(
        tester,
        PurchaseOrderReceiveLineCard.generateBarcodeButton('line-1'),
      );
      await tester.pumpAndSettle();

      expect(find.byType(AlertDialog), findsOneWidget);
      await tester.tap(
        find.descendant(
          of: find.byType(AlertDialog),
          matching: find.byType(FilledButton),
        ),
      );
      await tester.pumpAndSettle();

      expect(
        harness.container
            .read(receivePurchaseOrderControllerProvider('po-1'))
            .lines
            .single
            .barcode,
        'IB-000001',
      );
      verify(() => generateItemBarcode()).called(1);
    },
  );

  testWidgets(
    'keeps existing barcode when generated barcode confirmation is cancelled',
    (tester) async {
      final getPurchaseOrder = _MockGetPurchaseOrder();
      final receive = _MockReceivePurchaseOrder();
      final generateItemBarcode = _MockGenerateItemBarcode();
      when(
        () => generateItemBarcode(),
      ).thenAnswer(
        (_) async => const GeneratedItemBarcode(barcode: 'IB-000001'),
      );
      when(() => getPurchaseOrder('po-1')).thenAnswer((_) async => _detail());
      final harness = buildHarness(
        getPurchaseOrder: getPurchaseOrder,
        receivePurchaseOrder: receive,
        generateItemBarcode: generateItemBarcode,
      );
      addTearDown(() {
        harness.router.dispose();
        harness.container.dispose();
      });

      await tester.pumpWidget(harness.app);
      await tester.pumpAndSettle();
      await expandFirstLine(tester);
      await tapLineButton(
        tester,
        PurchaseOrderReceiveLineCard.generateBarcodeButton('line-1'),
      );
      await tester.pumpAndSettle();
      await tester.tap(
        find.descendant(
          of: find.byType(AlertDialog),
          matching: find.byType(TextButton),
        ),
      );
      await tester.pumpAndSettle();

      expect(
        harness.container
            .read(receivePurchaseOrderControllerProvider('po-1'))
            .lines
            .single
            .barcode,
        'CAT-001',
      );
      final state = harness.container.read(
        receivePurchaseOrderControllerProvider('po-1'),
      );
      expect(state.barcodeGenerationLineIds, isEmpty);
      expect(state.barcodeGenerationFailures, isEmpty);
      verify(() => generateItemBarcode()).called(1);
    },
  );

  testWidgets(
    'retries generation after failure while preserving barcode',
    (tester) async {
      final getPurchaseOrder = _MockGetPurchaseOrder();
      final receive = _MockReceivePurchaseOrder();
      final generateItemBarcode = _MockGenerateItemBarcode();
      var attempts = 0;
      when(() => generateItemBarcode()).thenAnswer((_) async {
        attempts += 1;
        if (attempts == 1) {
          throw AppException(
            failure: const Failure.network(message: 'offline'),
          );
        }
        return const GeneratedItemBarcode(barcode: 'IB-000001');
      });
      when(() => getPurchaseOrder('po-1')).thenAnswer((_) async => _detail());
      final harness = buildHarness(
        getPurchaseOrder: getPurchaseOrder,
        receivePurchaseOrder: receive,
        generateItemBarcode: generateItemBarcode,
      );
      addTearDown(() {
        harness.router.dispose();
        harness.container.dispose();
      });

      await tester.pumpWidget(harness.app);
      await tester.pumpAndSettle();
      await expandFirstLine(tester);

      await tapLineButton(
        tester,
        PurchaseOrderReceiveLineCard.generateBarcodeButton('line-1'),
      );
      await tester.pumpAndSettle();

      expect(
        harness.container
            .read(receivePurchaseOrderControllerProvider('po-1'))
            .barcodeGenerationFailures['line-1'],
        isNotNull,
      );
      expect(
        harness.container
            .read(receivePurchaseOrderControllerProvider('po-1'))
            .lines
            .single
            .barcode,
        'CAT-001',
      );

      harness.container
          .read(receivePurchaseOrderControllerProvider('po-1').notifier)
          .updateBarcode('line-1', '');

      await tapLineButton(
        tester,
        PurchaseOrderReceiveLineCard.generateBarcodeButton('line-1'),
      );
      await tester.pumpAndSettle();

      expect(
        harness.container
            .read(receivePurchaseOrderControllerProvider('po-1'))
            .barcodeGenerationFailures,
        isEmpty,
      );
      expect(
        harness.container
            .read(receivePurchaseOrderControllerProvider('po-1'))
            .lines
            .single
            .barcode,
        'IB-000001',
      );
      expect(attempts, 2);
      verify(() => generateItemBarcode()).called(2);
    },
  );

  testWidgets('handles generation timeout without replacing barcode', (
    tester,
  ) async {
    final getPurchaseOrder = _MockGetPurchaseOrder();
    final receive = _MockReceivePurchaseOrder();
    final generateItemBarcode = _MockGenerateItemBarcode();
    when(() => generateItemBarcode()).thenThrow(TimeoutException('timed out'));
    when(() => getPurchaseOrder('po-1')).thenAnswer((_) async => _detail());
    final harness = buildHarness(
      getPurchaseOrder: getPurchaseOrder,
      receivePurchaseOrder: receive,
      generateItemBarcode: generateItemBarcode,
    );
    addTearDown(() {
      harness.router.dispose();
      harness.container.dispose();
    });

    await tester.pumpWidget(harness.app);
    await tester.pumpAndSettle();
    await expandFirstLine(tester);
    await tapLineButton(
      tester,
      PurchaseOrderReceiveLineCard.generateBarcodeButton('line-1'),
    );
    await tester.pumpAndSettle();

    expect(
      harness.container
          .read(receivePurchaseOrderControllerProvider('po-1'))
          .barcodeGenerationFailures['line-1'],
      isNotNull,
    );
    expect(
      harness.container
          .read(receivePurchaseOrderControllerProvider('po-1'))
          .lines
          .single
          .barcode,
      'CAT-001',
    );
  });

  testWidgets('disables duplicate generation taps for a single line', (
    tester,
  ) async {
    final getPurchaseOrder = _MockGetPurchaseOrder();
    final receive = _MockReceivePurchaseOrder();
    final generated = Completer<GeneratedItemBarcode>();
    final generateItemBarcode = _MockGenerateItemBarcode();
    when(() => generateItemBarcode()).thenAnswer((_) => generated.future);
    when(() => getPurchaseOrder('po-1')).thenAnswer((_) async => _detail());
    final harness = buildHarness(
      getPurchaseOrder: getPurchaseOrder,
      receivePurchaseOrder: receive,
      generateItemBarcode: generateItemBarcode,
    );
    addTearDown(() {
      harness.router.dispose();
      harness.container.dispose();
    });

    await tester.pumpWidget(harness.app);
    await tester.pumpAndSettle();
    await expandFirstLine(tester);
    harness.container
        .read(receivePurchaseOrderControllerProvider('po-1').notifier)
        .updateBarcode('line-1', '');
    await tester.pumpAndSettle();
    await tapLineButton(
      tester,
      PurchaseOrderReceiveLineCard.generateBarcodeButton('line-1'),
    );
    await tester.pump();
    await tapLineButton(
      tester,
      PurchaseOrderReceiveLineCard.generateBarcodeButton('line-1'),
    );
    await tester.pump();

    verify(() => generateItemBarcode()).called(1);
    generated.complete(const GeneratedItemBarcode(barcode: 'IB-000001'));
    await tester.pumpAndSettle();

    expect(
      harness.container
          .read(receivePurchaseOrderControllerProvider('po-1'))
          .lines
          .single
          .barcode,
      'IB-000001',
    );
  });

  testWidgets('keeps line controls editable after optional prefill failure', (
    tester,
  ) async {
    final getPurchaseOrder = _MockGetPurchaseOrder();
    final receive = _MockReceivePurchaseOrder();
    final getProductDetails = _MockGetProductDetails();
    when(() => getPurchaseOrder('po-1')).thenAnswer((_) async => _detail());
    when(
      () => getProductDetails(name: 'Widget A'),
    ).thenThrow(Exception('offline'));
    final harness = buildHarness(
      getPurchaseOrder: getPurchaseOrder,
      receivePurchaseOrder: receive,
      getProductDetails: getProductDetails,
    );
    addTearDown(() {
      harness.router.dispose();
      harness.container.dispose();
    });

    await tester.pumpWidget(harness.app);
    await tester.pumpAndSettle();
    await expandFirstLine(tester);
    await tester.enterText(
      find.byKey(PurchaseOrderReceiveLineCard.barcodeField('line-1')),
      'MANUAL-1',
    );
    await tester.pumpAndSettle();

    final state = harness.container.read(
      receivePurchaseOrderControllerProvider('po-1'),
    );
    expect(state.prefillFailures['line-1'], contains('offline'));
    expect(state.lines.single.barcode, 'MANUAL-1');
  });
}

PurchaseOrder _detail({
  PurchaseOrderStatus status = PurchaseOrderStatus.partiallyReceived,
  List<PurchaseOrderLine> lines = const [
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
}) {
  return PurchaseOrder(
    purchaseOrderId: 'po-1',
    purchaseOrderNumber: 'PO-2026-001',
    status: status,
    lines: lines,
    expectedTotal: 1000,
    createdAt: DateTime.utc(2026, 7, 1),
    receivedQuantity: 7,
  );
}
