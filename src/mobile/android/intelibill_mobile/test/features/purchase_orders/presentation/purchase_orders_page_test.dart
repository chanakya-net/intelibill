import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_list_item.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_orders_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/pages/purchase_orders_page.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_card.dart';

class _TrackingStubController extends PurchaseOrdersController {
  _TrackingStubController(this._initialState, {this.stateAfterStatus});

  final PurchaseOrdersState _initialState;
  final PurchaseOrdersState? stateAfterStatus;
  DateTime? lastDateFrom;
  DateTime? lastDateTo;
  PurchaseOrderStatus? lastStatus;
  bool clearFiltersCalled = false;
  int loadMoreCalls = 0;
  int retryLoadMoreCalls = 0;

  @override
  PurchaseOrdersState build() => _initialState;

  @override
  void updateSearch(String query) {}

  @override
  void updateStatus(PurchaseOrderStatus? status) {
    lastStatus = status;
    if (stateAfterStatus != null) state = stateAfterStatus!;
  }

  @override
  void updateOrderDateFrom(DateTime? date) {
    lastDateFrom = date;
  }

  @override
  void updateOrderDateTo(DateTime? date) {
    lastDateTo = date;
  }

  @override
  void clearFilters() {
    clearFiltersCalled = true;
  }

  @override
  Future<void> loadMore() async {
    loadMoreCalls += 1;
  }

  @override
  Future<void> retryLoadMore() async {
    retryLoadMoreCalls += 1;
  }
}

class _StubPurchaseOrdersController extends PurchaseOrdersController {
  _StubPurchaseOrdersController(this._initialState);

  final PurchaseOrdersState _initialState;

  @override
  PurchaseOrdersState build() => _initialState;

  @override
  void updateSearch(String query) {}

  @override
  void updateStatus(PurchaseOrderStatus? status) {}

  @override
  void updateOrderDateFrom(DateTime? date) {}

  @override
  void updateOrderDateTo(DateTime? date) {}

  @override
  void clearFilters() {}
}

void main() {
  Widget buildApp(
    PurchaseOrdersState state, {
    Locale locale = const Locale('en', 'IN'),
    TextScaler textScaler = TextScaler.noScaling,
  }) {
    return ProviderScope(
      overrides: [
        purchaseOrdersControllerProvider.overrideWith(
          () => _StubPurchaseOrdersController(state),
        ),
      ],
      child: MaterialApp(
        theme: AppTheme.lightTheme,
        locale: locale,
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: Builder(
          builder: (context) => MediaQuery(
            data: MediaQuery.of(context).copyWith(textScaler: textScaler),
            child: const PurchaseOrdersPage(),
          ),
        ),
      ),
    );
  }

  Widget buildTrackingApp(_TrackingStubController controller) {
    return ProviderScope(
      overrides: [
        purchaseOrdersControllerProvider.overrideWith(() => controller),
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
    expect(find.text('Purchase-order data could not be read.'), findsOneWidget);
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

  testWidgets('triggers load more near the end of the list', (tester) async {
    final controller = _TrackingStubController(
      PurchaseOrdersState(
        items: List.generate(12, (index) => _item(id: 'po-$index')),
        totalCount: 24,
        hasMore: true,
      ),
    );
    await tester.pumpWidget(buildTrackingApp(controller));

    await tester.drag(find.byType(ListView), const Offset(0, -2000));
    await tester.pumpAndSettle();

    expect(controller.loadMoreCalls, greaterThan(0));
  });

  testWidgets('shows load-more retry footer while retaining cards', (
    tester,
  ) async {
    final controller = _TrackingStubController(
      PurchaseOrdersState(
        items: [_item()],
        totalCount: 3,
        loadMoreFailure: const Failure.network(message: 'failed'),
      ),
    );
    await tester.pumpWidget(buildTrackingApp(controller));

    expect(find.byKey(const Key('purchase-order-card-po-1')), findsOneWidget);
    expect(find.text('Could not load more purchase orders.'), findsOneWidget);
    await tester.tap(find.widgetWithText(FilledButton, 'Retry'));
    await tester.pump();

    expect(controller.retryLoadMoreCalls, 1);
  });

  testWidgets('shows load-more progress while retaining cards', (tester) async {
    await tester.pumpWidget(
      buildApp(
        PurchaseOrdersState(
          items: [_item()],
          totalCount: 3,
          isLoadingMore: true,
          hasMore: true,
        ),
      ),
    );

    expect(find.byKey(const Key('purchase-order-card-po-1')), findsOneWidget);
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
  });

  testWidgets('shows loaded footer after final page while retaining cards', (
    tester,
  ) async {
    await tester.pumpWidget(
      buildApp(PurchaseOrdersState(items: [_item()], totalCount: 1)),
    );

    expect(find.byKey(const Key('purchase-order-card-po-1')), findsOneWidget);
    expect(find.text('Loaded 1 of 1'), findsOneWidget);
  });

  testWidgets('card displays every purchase-order status', (tester) async {
    for (final status in PurchaseOrderStatus.values) {
      await tester.pumpWidget(
        MaterialApp(
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          home: Scaffold(
            body: PurchaseOrderCard(purchaseOrder: _item(status: status)),
          ),
        ),
      );
      expect(
        find.byKey(Key('purchase-order-status-${status.name}')),
        findsOneWidget,
      );
      expect(find.text(_statusText(status)), findsOneWidget);
    }
  });

  testWidgets(
    'announces card status and receipt progress as meaningful units',
    (
      tester,
    ) async {
      final semantics = tester.ensureSemantics();

      await tester.pumpWidget(
        buildApp(PurchaseOrdersState(items: [_item()], totalCount: 1)),
      );

      expect(
        tester
            .getSemantics(
              find.byKey(const Key('purchase-order-status-partiallyReceived')),
            )
            .label,
        'Status: Partially received',
      );
      expect(
        tester.getSemantics(find.byType(LinearProgressIndicator)).label,
        'Received: 7 / 12',
      );
      semantics.dispose();
    },
  );

  testWidgets('keeps list controls usable with large text on narrow screens', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(320, 640);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      buildApp(
        PurchaseOrdersState(items: [_item()], totalCount: 1),
        textScaler: const TextScaler.linear(1.8),
      ),
    );

    expect(find.byKey(PurchaseOrdersPage.searchFieldKey), findsOneWidget);
    expect(tester.takeException(), isNull);
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

  testWidgets(
    'empty filtered result keeps controls and shows localized refresh failure',
    (
      tester,
    ) async {
      await tester.pumpWidget(
        buildApp(
          const PurchaseOrdersState(
            refreshFailure: Failure.network(message: 'refresh failed'),
          ),
          locale: const Locale('hi'),
        ),
      );

      await tester.enterText(
        find.descendant(
          of: find.byKey(PurchaseOrdersPage.searchFieldKey),
          matching: find.byType(EditableText),
        ),
        'paper',
      );
      await tester.pump();

      expect(find.byKey(PurchaseOrdersPage.searchFieldKey), findsOneWidget);
      expect(find.byType(FilterChip), findsWidgets);
      expect(find.text('खरीद आदेश रीफ़्रेश नहीं किए जा सके'), findsOneWidget);
    },
  );

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

  testWidgets('shows status chips when filter bar renders', (tester) async {
    final controller = _TrackingStubController(
      PurchaseOrdersState(items: [_item()], totalCount: 1),
    );
    await tester.pumpWidget(buildTrackingApp(controller));

    expect(find.byType(FilterChip), findsWidgets);
    for (final status in PurchaseOrderStatus.values) {
      expect(find.text(_statusText(status)), findsWidgets);
    }
  });

  testWidgets('tapping status chip calls updateStatus', (tester) async {
    final controller = _TrackingStubController(
      PurchaseOrdersState(items: [_item()], totalCount: 1),
    );
    await tester.pumpWidget(buildTrackingApp(controller));

    await tester.tap(
      find.widgetWithText(FilterChip, PurchaseOrderStatus.placed.wireValue),
    );
    await tester.pumpAndSettle();

    expect(controller.lastStatus, PurchaseOrderStatus.placed);
  });

  testWidgets('tapping Clear button calls clearFilters', (tester) async {
    final controller = _TrackingStubController(
      PurchaseOrdersState(items: [_item()], totalCount: 1),
    );
    await tester.pumpWidget(buildTrackingApp(controller));

    await tester.tap(
      find.widgetWithText(FilterChip, PurchaseOrderStatus.placed.wireValue),
    );
    await tester.pumpAndSettle();

    final clearChip = find.widgetWithText(ActionChip, 'Clear');
    await tester.ensureVisible(clearChip);
    await tester.tap(clearChip);
    await tester.pumpAndSettle();

    expect(controller.clearFiltersCalled, isTrue);
  });

  testWidgets('updates the count after applying a status filter', (
    tester,
  ) async {
    final controller = _TrackingStubController(
      PurchaseOrdersState(items: [_item()], totalCount: 31),
      stateAfterStatus: PurchaseOrdersState(items: [_item()], totalCount: 1),
    );
    await tester.pumpWidget(buildTrackingApp(controller));

    expect(find.text('31 purchase orders'), findsOneWidget);
    await tester.tap(
      find.widgetWithText(FilterChip, PurchaseOrderStatus.placed.wireValue),
    );
    await tester.pumpAndSettle();

    expect(find.text('1 purchase order'), findsOneWidget);
  });

  testWidgets('shows a filtered empty state after applying a status filter', (
    tester,
  ) async {
    final controller = _TrackingStubController(
      PurchaseOrdersState(items: [_item()], totalCount: 1),
      stateAfterStatus: const PurchaseOrdersState(),
    );
    await tester.pumpWidget(buildTrackingApp(controller));

    await tester.tap(
      find.widgetWithText(FilterChip, PurchaseOrderStatus.placed.wireValue),
    );
    await tester.pumpAndSettle();

    expect(find.text('No purchase orders match your filters'), findsOneWidget);
    expect(find.text('No purchase orders yet'), findsNothing);
  });

  testWidgets('keeps filter controls horizontally scrollable on mobile', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(320, 640);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final controller = _TrackingStubController(
      const PurchaseOrdersState(),
    );
    await tester.pumpWidget(buildTrackingApp(controller));
    final filterBar = find.byType(SingleChildScrollView);
    final scrollable = find.descendant(
      of: filterBar,
      matching: find.byType(Scrollable),
    );
    final scrollableState = tester.state<ScrollableState>(scrollable);

    await tester.drag(scrollable, const Offset(-300, 0));
    await tester.pumpAndSettle();

    expect(scrollableState.position.pixels, greaterThan(0));
  });

  testWidgets('labels status and date filters for assistive technology', (
    tester,
  ) async {
    final controller = _TrackingStubController(
      PurchaseOrdersState(items: [_item()], totalCount: 1),
    );
    final semantics = tester.ensureSemantics();

    await tester.pumpWidget(buildTrackingApp(controller));

    final statusFilter = find.byKey(
      PurchaseOrdersPage.statusFilterKey(PurchaseOrderStatus.placed),
    );
    expect(tester.getSemantics(statusFilter).label, contains('Placed'));
    expect(
      find.ancestor(of: statusFilter, matching: find.byType(Tooltip)),
      findsOneWidget,
    );
    for (final dateFilter in [
      PurchaseOrdersPage.dateFromFilterKey,
      PurchaseOrdersPage.dateToFilterKey,
    ]) {
      expect(tester.getSemantics(find.byKey(dateFilter)).label, isNotEmpty);
      expect(
        tester.getSize(find.byKey(dateFilter)).height,
        greaterThanOrEqualTo(48),
      );
    }

    await tester.tap(statusFilter);
    await tester.pump();

    expect(controller.lastStatus, PurchaseOrderStatus.placed);
    expect(find.byKey(PurchaseOrdersPage.clearFiltersKey), findsOneWidget);
    expect(
      tester.getSemantics(find.byKey(PurchaseOrdersPage.clearFiltersKey)).label,
      contains('Clear'),
    );
    semantics.dispose();
  });

  testWidgets('has date-picker buttons for from/to dates', (tester) async {
    final controller = _TrackingStubController(
      PurchaseOrdersState(items: [_item()], totalCount: 1),
    );
    await tester.pumpWidget(buildTrackingApp(controller));

    final dateButtons = find.byType(OutlinedButton);
    expect(dateButtons, findsWidgets);
  });
}

PurchaseOrderListItem _item({
  String id = 'po-1',
  PurchaseOrderStatus status = PurchaseOrderStatus.partiallyReceived,
}) => PurchaseOrderListItem(
  purchaseOrderId: id,
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

String _statusText(PurchaseOrderStatus status) =>
    status == PurchaseOrderStatus.partiallyReceived
    ? 'Partially received'
    : status.wireValue;
