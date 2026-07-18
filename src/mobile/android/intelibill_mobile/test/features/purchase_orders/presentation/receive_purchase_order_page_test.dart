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
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/receive_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/receive_purchase_order_input.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/pages/receive_purchase_order_page.dart';
import 'package:mocktail/mocktail.dart';

class _MockGetPurchaseOrder extends Mock implements GetPurchaseOrder {}

class _MockReceivePurchaseOrder extends Mock implements ReceivePurchaseOrder {}

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
  }) {
    final container = ProviderContainer(
      overrides: [
        getPurchaseOrderProvider.overrideWithValue(getPurchaseOrder),
        receivePurchaseOrderProvider.overrideWithValue(receivePurchaseOrder),
      ],
    );
    final router = GoRouter(
      initialLocation: AppRoutes.purchaseOrderReceiveFor('po-1'),
      routes: [
        GoRoute(
          path: AppRoutes.purchaseOrderReceive,
          builder: (context, state) => ReceivePurchaseOrderPage(
            purchaseOrderId: state.pathParameters['purchaseOrderId'] ?? '',
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

    await tester.drag(find.byType(ListView), const Offset(0, -600));
    await tester.pumpAndSettle();

    expect(find.textContaining('Line count:'), findsOneWidget);
    expect(find.textContaining('Total quantity:'), findsOneWidget);
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
