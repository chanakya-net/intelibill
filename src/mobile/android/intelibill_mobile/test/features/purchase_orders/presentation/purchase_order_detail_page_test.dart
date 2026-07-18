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
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/get_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/pages/purchase_order_detail_page.dart';
import 'package:mocktail/mocktail.dart';

class _MockGetPurchaseOrder extends Mock implements GetPurchaseOrder {}

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
  _Harness _buildHarness({
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
          builder: (_, __) => const SizedBox.shrink(),
        ),
        GoRoute(
          path: AppRoutes.purchaseOrders,
          builder: (_, __) => const SizedBox(
            child: Text('Purchase orders list'),
          ),
        ),
        GoRoute(
          path: AppRoutes.purchaseOrderDetail,
          builder: (_, state) => PurchaseOrderDetailPage(
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

  _SimpleHarness _buildSimpleHarness({
    required GetPurchaseOrder getPurchaseOrder,
    required String purchaseOrderId,
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
          supportedLocales: const [Locale('en', 'IN')],
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

    final harness = _buildHarness(
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
    final getPurchaseOrder = _MockGetPurchaseOrder();
    when(() => getPurchaseOrder(any())).thenAnswer(
      (_) async => _detail(
        purchaseOrderId: 'po-77',
        status: PurchaseOrderStatus.received,
      ),
    );

    final harness = _buildSimpleHarness(
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
    expect(find.text('Order date: 11 Jul 2026'), findsOneWidget);
    expect(find.text('Expected delivery: 14 Jul 2026'), findsOneWidget);
    expect(find.text('Notes: Urgent restock'), findsOneWidget);
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

    final harness = _buildHarness(
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

    final harness = _buildHarness(
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

      final harness = _buildSimpleHarness(
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
}

PurchaseOrder _detail({
  String purchaseOrderId = 'po-1',
  PurchaseOrderStatus status = PurchaseOrderStatus.placed,
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
    lines: const [
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
    expectedTotal: 1450.5,
    createdAt: DateTime(2026, 7, 1, 8, 30),
    supplierName: 'Fresh Grocers',
    supplierReference: 'RG-77',
    receivedQuantity: 7,
  );
}

Finder _detailNumberFinder(String text) {
  return find.descendant(
    of: find.byType(Card),
    matching: find.text(text),
  );
}
