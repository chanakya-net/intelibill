import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_receipt.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_receipt_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/cancel_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/get_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/pages/purchase_order_detail_page.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_cancel_sheet.dart';
import 'package:intl/intl.dart';
import 'package:mocktail/mocktail.dart';

class _MockGetPurchaseOrder extends Mock implements GetPurchaseOrder {}

class _MockCancelPurchaseOrder extends Mock implements CancelPurchaseOrder {}

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

class _SimpleHarness {
  _SimpleHarness({
    required this.container,
    required this.app,
  });

  final ProviderContainer container;
  final Widget app;
}

void main() {
  _Harness buildHarness({
    required GetPurchaseOrder getPurchaseOrder,
    String initialLocation = AppRoutes.purchaseOrders,
  }) {
    final container = ProviderContainer(
      overrides: [getPurchaseOrderProvider.overrideWithValue(getPurchaseOrder)],
    );
    final router = GoRouter(
      initialLocation: initialLocation,
      routes: [
        GoRoute(
          path: AppRoutes.root,
          builder: (context, state) => const SizedBox.shrink(),
        ),
        GoRoute(
          path: AppRoutes.purchaseOrders,
          builder: (context, state) => const SizedBox(
            child: Text('Purchase orders list'),
          ),
        ),
        GoRoute(
          path: AppRoutes.purchaseOrderDetail,
          builder: (context, state) => PurchaseOrderDetailPage(
            purchaseOrderId: state.pathParameters['purchaseOrderId'] ?? '',
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

  _SimpleHarness buildSimpleHarness({
    required GetPurchaseOrder getPurchaseOrder,
    required String purchaseOrderId,
    Locale locale = const Locale('en', 'IN'),
  }) {
    final container = ProviderContainer(
      overrides: [getPurchaseOrderProvider.overrideWithValue(getPurchaseOrder)],
    );

    return _SimpleHarness(
      container: container,
      app: UncontrolledProviderScope(
        container: container,
        child: MaterialApp(
          theme: AppTheme.lightTheme,
          supportedLocales: [locale],
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          home: PurchaseOrderDetailPage(
            purchaseOrderId: purchaseOrderId,
          ),
        ),
      ),
    );
  }

  testWidgets('loads by route ID on deep link and reloads on changed ID', (
    tester,
  ) async {
    final calls = <String>[];
    final getPurchaseOrder = _MockGetPurchaseOrder();
    when(
      () => getPurchaseOrder(any()),
    ).thenAnswer((invocation) async {
      final id = invocation.positionalArguments.first as String;
      calls.add(id);
      return _detail(purchaseOrderId: id);
    });

    final harness = buildHarness(
      getPurchaseOrder: getPurchaseOrder,
      initialLocation: AppRoutes.purchaseOrderDetailFor('po-1'),
    );
    addTearDown(() {
      harness.router.dispose();
      harness.container.dispose();
    });
    await tester.pumpWidget(harness.app);
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));

    expect(calls, equals(['po-1']));
    expect(_detailNumberFinder('PO-po-1'), findsOneWidget);
    expect(find.byKey(PurchaseOrderDetailPage.pageKey), findsOneWidget);

    harness.router.go(AppRoutes.purchaseOrderDetailFor('po-2'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));

    expect(calls, equals(['po-1', 'po-2']));
    expect(_detailNumberFinder('PO-po-2'), findsOneWidget);
  });

  testWidgets('renders status badge, header, lines, and totals', (
    tester,
  ) async {
    final createdAt = DateTime.parse('2026-07-01T03:00:00.000-05:00').toLocal();
    final getPurchaseOrder = _MockGetPurchaseOrder();
    when(() => getPurchaseOrder(any())).thenAnswer(
      (_) async => _detail(
        purchaseOrderId: 'po-77',
        status: PurchaseOrderStatus.received,
        createdAt: createdAt,
        expectedTotal: 1450.5,
      ),
    );

    final harness = buildSimpleHarness(
      getPurchaseOrder: getPurchaseOrder,
      purchaseOrderId: 'po-77',
    );
    addTearDown(harness.container.dispose);
    await tester.pumpWidget(harness.app);
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));

    expect(
      find.byKey(const Key('purchase-order-status-received')),
      findsOneWidget,
    );
    expect(find.text('Purchase order details'), findsNothing);
    expect(_detailNumberFinder('PO-po-77'), findsOneWidget);
    expect(find.text('Supplier: Fresh Grocers'), findsOneWidget);
    expect(find.text('Supplier reference number: SRN-77'), findsOneWidget);
    expect(find.text('Supplier reference: RG-77'), findsOneWidget);
    final orderDate = DateFormat.yMMMd('en_IN').format(DateTime(2026, 7, 11));
    final deliveryDate = DateFormat.yMMMd(
      'en_IN',
    ).format(DateTime(2026, 7, 14));
    expect(find.text('Order date: $orderDate'), findsOneWidget);
    expect(find.text('Expected delivery: $deliveryDate'), findsOneWidget);
    expect(find.text('Notes: Urgent restock'), findsOneWidget);
    expect(
      find.text(
        'Created at: ${DateFormat.yMMMd('en_IN').add_jm().format(createdAt)}',
      ),
      findsOneWidget,
    );
    expect(find.text('Lines'), findsOneWidget);
    expect(find.text('Expected quantity: 18'), findsOneWidget);
    expect(find.text('Received quantity: 7'), findsOneWidget);
    expect(find.text('Remaining quantity: 11'), findsOneWidget);
    expect(find.text('Expected total: ₹1,451'), findsOneWidget);
    expect(find.text('Expected: 10'), findsOneWidget);
    expect(find.text('Received: 5'), findsOneWidget);
    expect(find.text('Remaining: 5'), findsOneWidget);
    expect(find.text('Line total: ₹701'), findsOneWidget);
    expect(find.text('Expected total: ₹1,451'), findsOneWidget);
    expect(find.text('Received: 7 / 18'), findsOneWidget);
  });

  testWidgets('renders lifecycle metadata and empty receipt history', (
    tester,
  ) async {
    final getPurchaseOrder = _MockGetPurchaseOrder();
    when(() => getPurchaseOrder(any())).thenAnswer(
      (_) async => _detail(
        status: PurchaseOrderStatus.cancelled,
        cancellationReason: 'Supplier unavailable',
      ),
    );
    final harness = buildSimpleHarness(
      getPurchaseOrder: getPurchaseOrder,
      purchaseOrderId: 'po-cancelled',
    );
    addTearDown(harness.container.dispose);

    await tester.pumpWidget(harness.app);
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));

    await tester.scrollUntilVisible(
      find.text('Cancellation reason: Supplier unavailable'),
      500,
      scrollable: find.byType(Scrollable),
    );
    expect(
      find.text('Cancellation reason: Supplier unavailable'),
      findsOneWidget,
    );
    await tester.scrollUntilVisible(
      find.text('Receipt history'),
      500,
      scrollable: find.byType(Scrollable),
    );

    expect(find.text('Receipt history'), findsOneWidget);
    expect(find.text('No receipts recorded'), findsOneWidget);
  });

  testWidgets('renders closure metadata and receipt line details', (
    tester,
  ) async {
    final receivedAt = DateTime.parse('2026-07-14T06:00:00Z').toLocal();
    final getPurchaseOrder = _MockGetPurchaseOrder();
    when(() => getPurchaseOrder(any())).thenAnswer(
      (_) async => _detail(
        status: PurchaseOrderStatus.closed,
        closedAt: DateTime.parse('2026-07-15T10:30:00Z').toLocal(),
        closedBy: 'user-closed',
        closeReason: 'Remaining stock discontinued',
        receipts: [
          PurchaseOrderReceipt(
            receiptId: 'receipt-1',
            receiptNumber: 'GRN-001',
            receivedAt: receivedAt,
            referenceNumber: 'REF-001',
            notes: 'Counted at dock',
            receivedByUserId: 'user-receiver',
            receivedByDisplayName: 'Riya Receiver',
            lines: const [
              PurchaseOrderReceiptLine(
                receiptLineId: 'receipt-line-1',
                purchaseOrderLineId: 'line-1',
                itemId: 'item-1',
                inventoryBatchId: 'batch-1',
                batchNumber: 'BATCH-001',
                batchVoided: true,
                stockTransactionId: 'transaction-1',
                quantity: 2.5,
                totalPurchaseCost: 250,
                unitCost: 100,
                mrp: 150,
                salesPrice: 125,
                taxRatePercent: 5,
                taxIncluded: false,
                purchaseTaxIncluded: true,
              ),
            ],
          ),
        ],
      ),
    );
    final harness = buildSimpleHarness(
      getPurchaseOrder: getPurchaseOrder,
      purchaseOrderId: 'po-closed',
    );
    addTearDown(harness.container.dispose);

    await tester.pumpWidget(harness.app);
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));

    await tester.scrollUntilVisible(
      find.text('Closed by: user-closed'),
      500,
      scrollable: find.byType(Scrollable),
    );
    expect(find.text('Closed by: user-closed'), findsOneWidget);
    await tester.scrollUntilVisible(
      find.text('Close reason: Remaining stock discontinued'),
      200,
      scrollable: find.byType(Scrollable),
    );
    await tester.scrollUntilVisible(
      find.text('GRN-001'),
      500,
      scrollable: find.byType(Scrollable),
    );

    expect(find.text('GRN-001'), findsOneWidget);
    await tester.tap(find.text('GRN-001'));
    await tester.pump();

    expect(find.text('Received by: Riya Receiver'), findsOneWidget);
    expect(find.text('Reference: REF-001'), findsOneWidget);
    expect(find.text('Notes: Counted at dock'), findsOneWidget);
    expect(find.text('Batch: BATCH-001'), findsOneWidget);
    expect(find.text('Batch state: Voided'), findsOneWidget);
    expect(find.text('Stock transaction: transaction-1'), findsOneWidget);
    expect(find.text('Quantity: 2.5'), findsOneWidget);
    expect(find.text('Total purchase cost: ₹250'), findsOneWidget);
  });

  testWidgets('refresh gesture on short content reloads by the same id', (
    tester,
  ) async {
    var calls = 0;
    final getPurchaseOrder = _MockGetPurchaseOrder();
    when(() => getPurchaseOrder(any())).thenAnswer(
      (_) async {
        calls += 1;
        return _detailWithNoLines(purchaseOrderId: 'po-short');
      },
    );

    final harness = buildSimpleHarness(
      getPurchaseOrder: getPurchaseOrder,
      purchaseOrderId: 'po-short',
    );
    addTearDown(harness.container.dispose);

    await tester.pumpWidget(harness.app);
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));

    expect(calls, equals(1));
    expect(_detailNumberFinder('PO-po-short'), findsOneWidget);

    await tester.drag(find.byType(ListView), const Offset(0, 250));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));
    await tester.pump(const Duration(milliseconds: 800));

    expect(calls, equals(2));
    expect(
      _detailNumberFinder('PO-po-short'),
      findsOneWidget,
    );
  });

  testWidgets('renders localized text for non-English locale', (tester) async {
    final getPurchaseOrder = _MockGetPurchaseOrder();
    when(() => getPurchaseOrder(any())).thenAnswer(
      (_) async => _detail(
        purchaseOrderId: 'po-locale',
      ),
    );

    final harness = buildSimpleHarness(
      getPurchaseOrder: getPurchaseOrder,
      purchaseOrderId: 'po-locale',
      locale: const Locale('hi', 'IN'),
    );
    addTearDown(harness.container.dispose);

    await tester.pumpWidget(harness.app);
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));

    expect(find.text('No lines on this order'), findsNothing);
    expect(find.text('Supplier: Fresh Grocers'), findsOneWidget);
    expect(find.text('Supplier reference number: SRN-77'), findsOneWidget);

    // Dates use the active app locale (not Intl's default) while preserving
    // the original calendar day (no timezone conversion on DateOnly values).
    final localeTag = const Locale('hi', 'IN').toLanguageTag();
    final orderDate = DateFormat.yMMMd(localeTag).format(DateTime(2026, 7, 11));
    final deliveryDate = DateFormat.yMMMd(
      localeTag,
    ).format(DateTime(2026, 7, 14));
    expect(find.text('Order date: $orderDate'), findsOneWidget);
    expect(find.text('Expected delivery: $deliveryDate'), findsOneWidget);
    expect(
      orderDate,
      isNot(DateFormat.yMMMd('en_IN').format(DateTime(2026, 7, 11))),
    );
    final createdAt =
        DateFormat.yMMMd(
          localeTag,
        ).add_jm().format(
          DateTime.parse('2026-07-01T08:30:00.000+05:30').toLocal(),
        );
    expect(find.text('Created at: $createdAt'), findsOneWidget);
  });

  testWidgets('retries load and then clears not-found with safe back action', (
    tester,
  ) async {
    var calls = 0;
    final getPurchaseOrder = _MockGetPurchaseOrder();
    when(() => getPurchaseOrder(any())).thenAnswer((_) async {
      calls += 1;
      if (calls == 1) {
        throw AppException(failure: const Failure.network());
      }
      return _detail(purchaseOrderId: 'po-retry');
    });

    final harness = buildHarness(
      getPurchaseOrder: getPurchaseOrder,
      initialLocation: AppRoutes.purchaseOrderDetailFor('po-retry'),
    );
    addTearDown(() {
      harness.router.dispose();
      harness.container.dispose();
    });
    await tester.pumpWidget(harness.app);
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));

    expect(find.text('Could not load purchase order.'), findsOneWidget);
    expect(find.widgetWithText(FilledButton, 'Retry'), findsOneWidget);

    await tester.tap(find.widgetWithText(FilledButton, 'Retry'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));

    expect(calls, equals(2));
    expect(find.byKey(PurchaseOrderDetailPage.pageKey), findsOneWidget);
    expect(_detailNumberFinder('PO-po-retry'), findsOneWidget);
    expect(find.text('Could not load purchase order.'), findsNothing);
  });

  testWidgets('shows not-found view with no data and allows safe back', (
    tester,
  ) async {
    final getPurchaseOrder = _MockGetPurchaseOrder();
    when(
      () => getPurchaseOrder(any()),
    ).thenAnswer(
      (_) async => throw AppException(
        failure: const Failure.notFound(),
      ),
    );

    final harness = buildHarness(
      getPurchaseOrder: getPurchaseOrder,
      initialLocation: AppRoutes.purchaseOrderDetailFor('po-missing'),
    );
    addTearDown(() {
      harness.router.dispose();
      harness.container.dispose();
    });
    await tester.pumpWidget(harness.app);
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));

    expect(
      find.text('Purchase order not found.'),
      findsOneWidget,
    );
    expect(find.text('Fresh Grocers'), findsNothing);
    expect(
      find.widgetWithText(FilledButton, 'Back to purchase orders'),
      findsOneWidget,
    );

    await tester.tap(
      find.widgetWithText(FilledButton, 'Back to purchase orders'),
    );
    await tester.pumpAndSettle();

    expect(
      harness.router.routeInformationProvider.value.uri.toString(),
      equals(AppRoutes.purchaseOrders),
    );
    expect(find.text('Purchase orders list'), findsOneWidget);
  });

  testWidgets('renders every status badge in the detail header', (
    tester,
  ) async {
    for (final status in PurchaseOrderStatus.values) {
      final getPurchaseOrder = _MockGetPurchaseOrder();
      when(
        () => getPurchaseOrder(any()),
      ).thenAnswer(
        (_) async => _detail(purchaseOrderId: 'po-status', status: status),
      );

      final harness = buildSimpleHarness(
        getPurchaseOrder: getPurchaseOrder,
        purchaseOrderId: 'po-status',
      );

      await tester.pumpWidget(harness.app);
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 50));

      expect(
        find.byKey(Key('purchase-order-status-${status.name}')),
        findsOneWidget,
      );
      expect(find.text(status.wireValue), findsOneWidget);

      harness.container.dispose();
      await tester.pump(const Duration(milliseconds: 50));
    }
  });

  group('Cancellation Action Visibility & Confirmation Flow', () {
    late _MockGetPurchaseOrder mockGetPurchaseOrder;
    late _MockCancelPurchaseOrder mockCancelPurchaseOrder;

    setUp(() {
      mockGetPurchaseOrder = _MockGetPurchaseOrder();
      mockCancelPurchaseOrder = _MockCancelPurchaseOrder();
    });

    _SimpleHarness buildHarnessWithCancel({
      required PurchaseOrderStatus status,
    }) {
      final getPO = _detail(purchaseOrderId: 'po-test', status: status);
      when(() => mockGetPurchaseOrder(any())).thenAnswer((_) async => getPO);

      final container = ProviderContainer(
        overrides: [
          getPurchaseOrderProvider.overrideWithValue(mockGetPurchaseOrder),
          cancelPurchaseOrderProvider.overrideWithValue(
            mockCancelPurchaseOrder,
          ),
        ],
      );

      return _SimpleHarness(
        container: container,
        app: UncontrolledProviderScope(
          container: container,
          child: MaterialApp(
            theme: AppTheme.lightTheme,
            supportedLocales: const [Locale('en', 'IN')],
            localizationsDelegates: AppLocalizations.localizationsDelegates,
            home: PurchaseOrderDetailPage(purchaseOrderId: 'po-test'),
          ),
        ),
      );
    }

    testWidgets('shows cancel button only when status is Placed', (
      WidgetTester tester,
    ) async {
      for (final status in PurchaseOrderStatus.values) {
        final harness = buildHarnessWithCancel(status: status);
        await tester.pumpWidget(harness.app);
        await tester.pump();
        await tester.pump(const Duration(milliseconds: 50));

        final buttonFinder = find.byKey(
          const Key('purchase-order-detail-cancel-button'),
        );
        if (status == PurchaseOrderStatus.placed) {
          expect(buttonFinder, findsOneWidget);
        } else {
          expect(buttonFinder, findsNothing);
        }

        harness.container.dispose();
        await tester.pump(const Duration(milliseconds: 50));
      }
    });

    testWidgets(
      'shows sheet on cancel button click and executes cancellation',
      (WidgetTester tester) async {
        final harness = buildHarnessWithCancel(
          status: PurchaseOrderStatus.placed,
        );

        final cancelledPO = _detail(
          purchaseOrderId: 'po-test',
          status: PurchaseOrderStatus.cancelled,
          cancellationReason: 'Vendor issue',
        );
        when(
          () => mockCancelPurchaseOrder(any(), any()),
        ).thenAnswer((_) async => cancelledPO);

        await tester.pumpWidget(harness.app);
        await tester.pump();
        await tester.pump(const Duration(milliseconds: 50));

        final buttonFinder = find.byKey(
          const Key('purchase-order-detail-cancel-button'),
        );
        expect(buttonFinder, findsOneWidget);

        await tester.tap(buttonFinder);
        await tester.pumpAndSettle();

        // Expect sheet is visible
        expect(find.byType(PurchaseOrderCancelSheet), findsOneWidget);

        // Enter invalid (empty) reason and verify button is disabled
        final textFormField = find.byType(TextField);
        expect(textFormField, findsOneWidget);

        final cancelBtnFinder = find.widgetWithText(
          ElevatedButton,
          'Cancel Order',
        );
        expect(
          tester.widget<ElevatedButton>(cancelBtnFinder).onPressed,
          isNull,
        );

        // Enter valid reason
        await tester.enterText(textFormField, 'Vendor issue');
        await tester.pumpAndSettle();
        expect(
          tester.widget<ElevatedButton>(cancelBtnFinder).onPressed,
          isNotNull,
        );

        // Tap cancel order button
        await tester.tap(cancelBtnFinder);
        await tester.pumpAndSettle();

        // Verify cancel was called on use case
        verify(
          () => mockCancelPurchaseOrder('po-test', 'Vendor issue'),
        ).called(1);

        // Verify sheet is closed
        expect(find.byType(PurchaseOrderCancelSheet), findsNothing);

        harness.container.dispose();
        await tester.pump(const Duration(milliseconds: 50));
      },
    );
  });
}

PurchaseOrder _detail({
  String purchaseOrderId = 'po-1',
  PurchaseOrderStatus status = PurchaseOrderStatus.placed,
  DateTime? createdAt,
  double? expectedTotal,
  List<PurchaseOrderLine>? lines,
  String? cancellationReason,
  DateTime? closedAt,
  String? closedBy,
  String? closeReason,
  List<PurchaseOrderReceipt>? receipts,
}) {
  return PurchaseOrder(
    purchaseOrderId: purchaseOrderId,
    purchaseOrderNumber: 'PO-$purchaseOrderId',
    status: status,
    supplierId: 'supplier-1',
    orderDate: DateTime(2026, 7, 11),
    expectedDeliveryDate: DateTime(2026, 7, 14),
    supplierReferenceNumber: 'SRN-77',
    notes: 'Urgent restock',
    lines:
        lines ??
        const [
          PurchaseOrderLine(
            lineId: 'line-1',
            itemId: 'item-1',
            description: 'Widget A',
            expectedQuantity: 10,
            receivedQuantity: 5,
            remainingQuantity: 5,
            unitCost: 75,
            lineTotal: 750,
          ),
          PurchaseOrderLine(
            lineId: 'line-2',
            itemId: 'item-2',
            description: 'Widget B',
            expectedQuantity: 8,
            receivedQuantity: 2,
            remainingQuantity: 6,
            unitCost: 87.5,
            lineTotal: 700.5,
          ),
        ],
    expectedTotal: expectedTotal ?? 1450.5,
    createdAt:
        createdAt ?? DateTime.parse('2026-07-01T08:30:00.000+05:30').toLocal(),
    supplierName: 'Fresh Grocers',
    supplierReference: 'RG-77',
    receivedQuantity: 7,
    cancellationReason: cancellationReason,
    closedAt: closedAt,
    closedBy: closedBy,
    closeReason: closeReason,
    receipts: receipts ?? const [],
  );
}

PurchaseOrder _detailWithNoLines({
  String purchaseOrderId = 'po-1',
  PurchaseOrderStatus status = PurchaseOrderStatus.placed,
}) {
  return _detail(
    purchaseOrderId: purchaseOrderId,
    status: status,
    lines: const [],
  );
}

Finder _detailNumberFinder(String text) {
  return find.descendant(
    of: find.byType(Card),
    matching: find.text(text),
  );
}
