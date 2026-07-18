import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/get_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/pages/purchase_order_detail_page.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/pages/receive_purchase_order_page.dart';
import 'package:mocktail/mocktail.dart';

class _MockGetPurchaseOrder extends Mock implements GetPurchaseOrder {}

class _Harness {
  _Harness({required this.container, required this.router, required this.app});

  final ProviderContainer container;
  final GoRouter router;
  final Widget app;
}

void main() {
  _Harness buildHarness({required GetPurchaseOrder getPurchaseOrder}) {
    final container = ProviderContainer(
      overrides: [getPurchaseOrderProvider.overrideWithValue(getPurchaseOrder)],
    );
    final router = GoRouter(
      initialLocation: AppRoutes.purchaseOrderDetailFor('po-1'),
      routes: [
        GoRoute(
          path: AppRoutes.purchaseOrderDetail,
          builder: (context, state) => PurchaseOrderDetailPage(
            purchaseOrderId: state.pathParameters['purchaseOrderId'] ?? '',
          ),
        ),
        GoRoute(
          path: AppRoutes.purchaseOrderReceive,
          builder: (context, state) => ReceivePurchaseOrderPage(
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

  testWidgets('shows receive action only for placed and partially received', (
    tester,
  ) async {
    final get = _MockGetPurchaseOrder();
    when(() => get(any())).thenAnswer(
      (_) async => _detail(status: PurchaseOrderStatus.placed),
    );

    final harness = buildHarness(getPurchaseOrder: get);
    addTearDown(() {
      harness.router.dispose();
      harness.container.dispose();
    });
    await tester.pumpWidget(harness.app);
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));
    expect(
      find.byKey(PurchaseOrderDetailPage.receiveButtonKey),
      findsOneWidget,
    );

    when(() => get(any())).thenAnswer(
      (_) async => _detail(status: PurchaseOrderStatus.partiallyReceived),
    );
    harness.router.go(AppRoutes.purchaseOrderDetailFor('po-1'));
    await tester.pumpAndSettle();

    expect(
      find.byKey(PurchaseOrderDetailPage.receiveButtonKey),
      findsOneWidget,
    );
  });

  for (final status in <PurchaseOrderStatus>[
    PurchaseOrderStatus.cancelled,
    PurchaseOrderStatus.received,
    PurchaseOrderStatus.closed,
    PurchaseOrderStatus.draft,
  ]) {
    testWidgets('hides receive action for status $status', (tester) async {
      final get = _MockGetPurchaseOrder();
      when(() => get(any())).thenAnswer(
        (_) async => _detail(status: status),
      );
      final harness = buildHarness(getPurchaseOrder: get);

      await tester.pumpWidget(harness.app);
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 50));

      expect(
        find.byKey(PurchaseOrderDetailPage.receiveButtonKey),
        findsNothing,
      );
      harness.router.dispose();
      harness.container.dispose();
    });
  }

  testWidgets('navigates to receive route from detail page', (tester) async {
    final get = _MockGetPurchaseOrder();
    when(() => get(any())).thenAnswer(
      (_) async => _detail(status: PurchaseOrderStatus.placed),
    );

    final harness = buildHarness(getPurchaseOrder: get);
    addTearDown(() {
      harness.router.dispose();
      harness.container.dispose();
    });
    await tester.pumpWidget(harness.app);
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));

    await tester.tap(find.byKey(PurchaseOrderDetailPage.receiveButtonKey));
    await tester.pumpAndSettle();

    expect(
      harness.router.routerDelegate.currentConfiguration.uri.toString(),
      AppRoutes.purchaseOrderReceiveFor('po-1'),
    );
    expect(find.byType(ReceivePurchaseOrderPage), findsOneWidget);
  });
}

PurchaseOrder _detail({
  PurchaseOrderStatus status = PurchaseOrderStatus.placed,
}) {
  return PurchaseOrder(
    purchaseOrderId: 'po-1',
    purchaseOrderNumber: 'PO-001',
    status: status,
    lines: const [
      PurchaseOrderLine(
        lineId: 'line-1',
        itemId: 'item-1',
        description: 'Widget',
        expectedQuantity: 10,
        receivedQuantity: 0,
        remainingQuantity: 10,
        unitCost: 10,
        lineTotal: 100,
      ),
    ],
    expectedTotal: 100,
    createdAt: DateTime.utc(2026, 7, 1),
  );
}
