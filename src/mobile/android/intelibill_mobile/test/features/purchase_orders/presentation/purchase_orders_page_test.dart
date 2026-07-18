import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_list_item.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_orders_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/pages/purchase_orders_page.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_card.dart';

class _StubPurchaseOrdersController extends PurchaseOrdersController {
  _StubPurchaseOrdersController(this._initialState);

  final PurchaseOrdersState _initialState;

  @override
  PurchaseOrdersState build() => _initialState;

  @override
  void updateSearch(String query) {}
}

void main() {
  Widget buildApp(PurchaseOrdersState state) {
    return ProviderScope(
      overrides: [
        purchaseOrdersControllerProvider.overrideWith(
          () => _StubPurchaseOrdersController(state),
        ),
      ],
      child: MaterialApp(
        theme: AppTheme.lightTheme,
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: const PurchaseOrdersPage(),
      ),
    );
  }

  testWidgets('shows initial loading, empty, and retry states distinctly', (
    tester,
  ) async {
    await tester.pumpWidget(
      buildApp(const PurchaseOrdersState(isLoading: true)),
    );
    expect(find.byType(CircularProgressIndicator), findsOneWidget);

    await tester.pumpWidget(const SizedBox());
    await tester.pumpWidget(buildApp(const PurchaseOrdersState()));
    expect(find.text('No purchase orders yet'), findsOneWidget);

    await tester.pumpWidget(const SizedBox());
    await tester.pumpWidget(
      buildApp(
        const PurchaseOrdersState(
          failure: SerializationFailure(message: 'Invalid response'),
        ),
      ),
    );
    expect(find.text('Data could not be read.'), findsOneWidget);
    expect(find.widgetWithText(FilledButton, 'Retry'), findsOneWidget);
  });

  testWidgets('renders total count and every confirmed card field', (
    tester,
  ) async {
    await tester.pumpWidget(
      buildApp(PurchaseOrdersState(items: [_item()], totalCount: 31)),
    );

    expect(find.byKey(PurchaseOrdersPage.countKey), findsOneWidget);
    expect(find.text('31 purchase orders'), findsOneWidget);
    expect(find.text('PO-2026-001'), findsOneWidget);
    expect(find.text('Acme Supplies'), findsOneWidget);
    expect(find.text('Ref: ACME-42'), findsOneWidget);
    expect(find.text('3 lines'), findsOneWidget);
    expect(find.text('Expected: 12'), findsOneWidget);
    expect(find.text('Received: 7 / 12'), findsOneWidget);
    expect(find.text('₹1,241'), findsOneWidget);
    expect(find.text('1 Jul 2026'), findsOneWidget);
  });

  testWidgets('card displays every purchase-order status', (tester) async {
    for (final status in PurchaseOrderStatus.values) {
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: PurchaseOrderCard(purchaseOrder: _item(status: status)),
          ),
        ),
      );
      expect(
        find.byKey(Key('purchase-order-status-${status.name}')),
        findsOneWidget,
      );
      expect(find.text(status.wireValue), findsOneWidget);
    }
  });

  testWidgets('card navigates to the purchase-order detail route', (
    tester,
  ) async {
    final router = GoRouter(
      initialLocation: '/',
      routes: [
        GoRoute(path: '/', builder: (_, _) => const PurchaseOrdersPage()),
        GoRoute(
          path: AppRoutes.purchaseOrderDetail,
          builder: (_, state) => Text(state.pathParameters['purchaseOrderId']!),
        ),
      ],
    );
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          purchaseOrdersControllerProvider.overrideWith(
            () => _StubPurchaseOrdersController(
              PurchaseOrdersState(items: [_item()], totalCount: 1),
            ),
          ),
        ],
        child: MaterialApp.router(
          theme: AppTheme.lightTheme,
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          routerConfig: router,
        ),
      ),
    );

    await tester.pumpAndSettle();
    tester
        .widget<InkWell>(find.byKey(const Key('purchase-order-card-po-1')))
        .onTap!();
    await tester.pumpAndSettle();
    expect(
      router.routerDelegate.currentConfiguration.uri.path,
      AppRoutes.purchaseOrderDetailFor('po-1'),
    );
    expect(find.text('po-1'), findsOneWidget);
  });

  testWidgets('search field is keyed and mounted in success state', (
    tester,
  ) async {
    await tester.pumpWidget(
      buildApp(
        PurchaseOrdersState(items: [_item()], totalCount: 1),
      ),
    );
    expect(find.byKey(PurchaseOrdersPage.searchFieldKey), findsOneWidget);
  });

  testWidgets('search field is keyed and mounted in empty state', (
    tester,
  ) async {
    await tester.pumpWidget(buildApp(const PurchaseOrdersState()));
    expect(find.byKey(PurchaseOrdersPage.searchFieldKey), findsOneWidget);
    expect(find.text('No purchase orders yet'), findsOneWidget);
  });

  testWidgets('typed query shows filtered empty state and retains search', (
    tester,
  ) async {
    await tester.pumpWidget(buildApp(const PurchaseOrdersState()));

    await tester.enterText(
      find.descendant(
        of: find.byKey(PurchaseOrdersPage.searchFieldKey),
        matching: find.byType(EditableText),
      ),
      'paper',
    );
    await tester.pump();

    expect(find.byKey(PurchaseOrdersPage.searchFieldKey), findsOneWidget);
    expect(find.text('No results for "paper"'), findsOneWidget);
    expect(find.text('No purchase orders yet'), findsNothing);
  });
}

PurchaseOrderListItem _item({
  PurchaseOrderStatus status = PurchaseOrderStatus.partiallyReceived,
}) => PurchaseOrderListItem(
  purchaseOrderId: 'po-1',
  purchaseOrderNumber: 'PO-2026-001',
  status: status,
  supplierName: 'Acme Supplies',
  supplierReference: 'ACME-42',
  lineCount: 3,
  expectedQuantity: 12,
  receivedQuantity: 7,
  expectedTotal: 1240.5,
  createdAt: DateTime(2026, 7, 1, 10),
);
