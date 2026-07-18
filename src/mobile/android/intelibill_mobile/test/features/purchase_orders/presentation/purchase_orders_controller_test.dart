import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_filters.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_list_item.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_page.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/get_purchase_orders.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_orders_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockGetPurchaseOrders extends Mock implements GetPurchaseOrders {}

void main() {
  late MockGetPurchaseOrders getPurchaseOrders;

  setUpAll(() {
    registerFallbackValue(const PurchaseOrderFilters());
  });

  setUp(() => getPurchaseOrders = MockGetPurchaseOrders());

  ProviderContainer makeContainer({bool? stubInitialLoad}) {
    reset(getPurchaseOrders);
    final willStub = stubInitialLoad ?? true;
    if (willStub) {
      when(() => getPurchaseOrders(any())).thenAnswer((_) async => _page());
    }
    return ProviderContainer(
      overrides: [
        getPurchaseOrdersProvider.overrideWithValue(getPurchaseOrders),
      ],
    );
  }

  test(
    'starts loading page 1 at 20 rows then exposes success and count',
    () async {
      final container = makeContainer();
      addTearDown(container.dispose);

      expect(
        container.read(purchaseOrdersControllerProvider).isInitialLoading,
        isTrue,
      );
      await container.read(purchaseOrdersControllerProvider.notifier).refresh();

      final state = container.read(purchaseOrdersControllerProvider);
      expect(state.items, hasLength(1));
      expect(state.totalCount, 31);
      expect(state.isInitialLoading, isFalse);
      verify(
        () => getPurchaseOrders(const PurchaseOrderFilters()),
      ).called(greaterThanOrEqualTo(1));
    },
  );

  test(
    'exposes an initial serialization error then retries successfully',
    () async {
      var calls = 0;
      reset(getPurchaseOrders);
      when(() => getPurchaseOrders(any())).thenAnswer((_) async {
        calls += 1;
        if (calls == 1) {
          throw AppException(
            failure: const Failure.serialization(message: 'bad'),
          );
        }
        return _page();
      });
      final container = ProviderContainer(
        overrides: [
          getPurchaseOrdersProvider.overrideWithValue(getPurchaseOrders),
        ],
      );
      addTearDown(container.dispose);

      await container.read(purchaseOrdersControllerProvider.notifier).refresh();
      expect(
        container.read(purchaseOrdersControllerProvider).failure,
        isA<SerializationFailure>(),
      );

      await container.read(purchaseOrdersControllerProvider.notifier).retry();
      final state = container.read(purchaseOrdersControllerProvider);
      expect(state.failure, isNull);
      expect(state.items, hasLength(1));
    },
  );

  test('distinguishes a true empty result', () async {
    reset(getPurchaseOrders);
    when(
      () => getPurchaseOrders(any()),
    ).thenAnswer(
      (_) async => const PurchaseOrderPage(
        items: [],
        totalCount: 0,
        pageNumber: 1,
        pageSize: 20,
      ),
    );
    final container = ProviderContainer(
      overrides: [
        getPurchaseOrdersProvider.overrideWithValue(getPurchaseOrders),
      ],
    );
    addTearDown(container.dispose);

    await container.read(purchaseOrdersControllerProvider.notifier).refresh();

    final state = container.read(purchaseOrdersControllerProvider);
    expect(state.isEmpty, isTrue);
    expect(state.failure, isNull);
  });

  testWidgets('debounces latest trimmed search for 300 milliseconds', (
    tester,
  ) async {
    final container = makeContainer();
    addTearDown(container.dispose);
    _keepControllerAlive(container);
    final notifier = container.read(purchaseOrdersControllerProvider.notifier);
    await tester.pump();
    clearInteractions(getPurchaseOrders);

    notifier.updateSearch(' first ');
    await tester.pump(const Duration(milliseconds: 299));
    verifyNever(() => getPurchaseOrders(any()));

    notifier.updateSearch('  paper  ');
    await tester.pump(const Duration(milliseconds: 299));
    verifyNever(() => getPurchaseOrders(any()));
    await tester.pump(const Duration(milliseconds: 1));

    verify(
      () => getPurchaseOrders(const PurchaseOrderFilters(search: 'paper')),
    ).called(1);
  });

  testWidgets('disposing cancels a pending debounced search', (tester) async {
    final container = makeContainer();
    _keepControllerAlive(container);
    final notifier = container.read(purchaseOrdersControllerProvider.notifier);
    await tester.pump();
    clearInteractions(getPurchaseOrders);

    notifier.updateSearch('paper');
    container.dispose();
    await tester.pump(const Duration(milliseconds: 300));

    verifyNever(() => getPurchaseOrders(any()));
  });

  testWidgets('refresh and retry retain active search and newest result wins', (
    tester,
  ) async {
    final search = Completer<PurchaseOrderPage>();
    final refresh = Completer<PurchaseOrderPage>();
    final retry = Completer<PurchaseOrderPage>();
    var calls = 0;
    when(() => getPurchaseOrders(any())).thenAnswer((_) {
      return switch (calls++) {
        0 => Future.value(_page(itemId: 'initial')),
        1 => search.future,
        2 => refresh.future,
        _ => retry.future,
      };
    });
    final container = ProviderContainer(
      overrides: [
        getPurchaseOrdersProvider.overrideWithValue(getPurchaseOrders),
      ],
    );
    addTearDown(container.dispose);
    _keepControllerAlive(container);
    final notifier = container.read(purchaseOrdersControllerProvider.notifier);
    await tester.pump();

    notifier.updateSearch('  paper  ');
    await tester.pump(const Duration(milliseconds: 300));
    search.complete(_page(itemId: 'search'));
    await tester.pump();

    final refreshFuture = notifier.refresh();
    final retryFuture = notifier.retry();
    verify(
      () => getPurchaseOrders(const PurchaseOrderFilters(search: 'paper')),
    ).called(3);

    retry.complete(_page(itemId: 'retry'));
    await retryFuture;
    refresh.complete(_page(itemId: 'refresh'));
    await refreshFuture;

    expect(
      container
          .read(purchaseOrdersControllerProvider)
          .items
          .single
          .purchaseOrderId,
      'retry',
    );
  });

  testWidgets('late successful search cannot overwrite a newer result', (
    tester,
  ) async {
    final older = Completer<PurchaseOrderPage>();
    final newer = Completer<PurchaseOrderPage>();
    var calls = 0;
    when(() => getPurchaseOrders(any())).thenAnswer((_) {
      return switch (calls++) {
        0 => Future.value(_page(itemId: 'initial')),
        1 => older.future,
        _ => newer.future,
      };
    });
    final container = ProviderContainer(
      overrides: [
        getPurchaseOrdersProvider.overrideWithValue(getPurchaseOrders),
      ],
    );
    addTearDown(container.dispose);
    _keepControllerAlive(container);
    final notifier = container.read(purchaseOrdersControllerProvider.notifier);
    await tester.pump();

    notifier.updateSearch('old');
    await tester.pump(const Duration(milliseconds: 300));
    notifier.updateSearch('new');
    await tester.pump(const Duration(milliseconds: 300));
    newer.complete(_page(itemId: 'newer'));
    await tester.pump();
    older.complete(_page(itemId: 'older'));
    await tester.pump();

    expect(
      container
          .read(purchaseOrdersControllerProvider)
          .items
          .single
          .purchaseOrderId,
      'newer',
    );
  });

  testWidgets('late failed search cannot overwrite a newer result', (
    tester,
  ) async {
    final older = Completer<PurchaseOrderPage>();
    final newer = Completer<PurchaseOrderPage>();
    var calls = 0;
    when(() => getPurchaseOrders(any())).thenAnswer((_) {
      return switch (calls++) {
        0 => Future.value(_page(itemId: 'initial')),
        1 => older.future,
        _ => newer.future,
      };
    });
    final container = ProviderContainer(
      overrides: [
        getPurchaseOrdersProvider.overrideWithValue(getPurchaseOrders),
      ],
    );
    addTearDown(container.dispose);
    _keepControllerAlive(container);
    final notifier = container.read(purchaseOrdersControllerProvider.notifier);
    await tester.pump();

    notifier.updateSearch('old');
    await tester.pump(const Duration(milliseconds: 300));
    notifier.updateSearch('new');
    await tester.pump(const Duration(milliseconds: 300));
    newer.complete(_page(itemId: 'newer'));
    await tester.pump();
    older.completeError(
      AppException(failure: const Failure.network(message: 'stale')),
    );
    await tester.pump();

    final state = container.read(purchaseOrdersControllerProvider);
    expect(state.items.single.purchaseOrderId, 'newer');
    expect(state.failure, isNull);
  });

  testWidgets('status filter passes through to getPurchaseOrders', (
    tester,
  ) async {
    final container = makeContainer();
    addTearDown(container.dispose);
    _keepControllerAlive(container);
    final notifier = container.read(purchaseOrdersControllerProvider.notifier);
    await tester.pump();
    clearInteractions(getPurchaseOrders);

    notifier.updateStatus(PurchaseOrderStatus.placed);
    await tester.pumpAndSettle();

    verify(
      () => getPurchaseOrders(
        const PurchaseOrderFilters(status: PurchaseOrderStatus.placed),
      ),
    ).called(1);
  });

  testWidgets('orderDateFrom filter passes through to getPurchaseOrders', (
    tester,
  ) async {
    final from = DateTime(2026, 1, 1);
    final container = makeContainer();
    addTearDown(container.dispose);
    _keepControllerAlive(container);
    final notifier = container.read(purchaseOrdersControllerProvider.notifier);
    await tester.pump();
    clearInteractions(getPurchaseOrders);

    notifier.updateOrderDateFrom(from);
    await tester.pumpAndSettle();

    verify(
      () => getPurchaseOrders(
        PurchaseOrderFilters(orderDateFrom: from),
      ),
    ).called(1);
  });

  testWidgets('orderDateTo filter passes through to getPurchaseOrders', (
    tester,
  ) async {
    final to = DateTime(2026, 12, 31);
    final container = makeContainer();
    addTearDown(container.dispose);
    _keepControllerAlive(container);
    final notifier = container.read(purchaseOrdersControllerProvider.notifier);
    await tester.pump();
    clearInteractions(getPurchaseOrders);

    notifier.updateOrderDateTo(to);
    await tester.pumpAndSettle();

    verify(
      () => getPurchaseOrders(
        PurchaseOrderFilters(orderDateTo: to),
      ),
    ).called(1);
  });

  testWidgets('multiple filters combine', (tester) async {
    final from = DateTime(2026, 1, 1);
    final to = DateTime(2026, 12, 31);
    final container = makeContainer();
    addTearDown(container.dispose);
    _keepControllerAlive(container);
    final notifier = container.read(purchaseOrdersControllerProvider.notifier);
    await tester.pump();
    clearInteractions(getPurchaseOrders);

    notifier.updateSearch('widget');
    await tester.pump(const Duration(milliseconds: 300));
    notifier.updateStatus(PurchaseOrderStatus.received);
    await tester.pumpAndSettle();
    notifier.updateOrderDateFrom(from);
    await tester.pumpAndSettle();
    notifier.updateOrderDateTo(to);
    await tester.pumpAndSettle();

    verify(
      () => getPurchaseOrders(
        PurchaseOrderFilters(
          search: 'widget',
          status: PurchaseOrderStatus.received,
          orderDateFrom: from,
          orderDateTo: to,
        ),
      ),
    ).called(1);
  });

  testWidgets('clearFilters resets search, status, and dates', (tester) async {
    final from = DateTime(2026, 1, 1);
    final to = DateTime(2026, 12, 31);
    final container = makeContainer();
    addTearDown(container.dispose);
    _keepControllerAlive(container);
    final notifier = container.read(purchaseOrdersControllerProvider.notifier);
    await tester.pump();
    clearInteractions(getPurchaseOrders);

    notifier.updateSearch('widget');
    await tester.pump(const Duration(milliseconds: 300));
    notifier.updateStatus(PurchaseOrderStatus.placed);
    await tester.pumpAndSettle();
    notifier.updateOrderDateFrom(from);
    await tester.pumpAndSettle();
    notifier.updateOrderDateTo(to);
    await tester.pumpAndSettle();

    notifier.clearFilters();
    await tester.pumpAndSettle();

    verify(
      () => getPurchaseOrders(const PurchaseOrderFilters()),
    ).called(1);
  });

  testWidgets('rejects invalid date range (from > to)', (tester) async {
    final from = DateTime(2026, 12, 31);
    final to = DateTime(2026, 1, 1);
    final container = makeContainer();
    addTearDown(container.dispose);
    _keepControllerAlive(container);
    final notifier = container.read(purchaseOrdersControllerProvider.notifier);
    await tester.pump();
    clearInteractions(getPurchaseOrders);

    notifier.updateOrderDateFrom(from);
    await tester.pumpAndSettle();
    clearInteractions(getPurchaseOrders);

    notifier.updateOrderDateTo(to);
    await tester.pumpAndSettle();

    verifyNever(() => getPurchaseOrders(any()));
  });

  testWidgets('rejects invalid date range when from is set second', (
    tester,
  ) async {
    final from = DateTime(2026, 12, 31);
    final to = DateTime(2026, 1, 1);
    final container = makeContainer();
    addTearDown(container.dispose);
    _keepControllerAlive(container);
    final notifier = container.read(purchaseOrdersControllerProvider.notifier);
    await tester.pump();
    clearInteractions(getPurchaseOrders);

    notifier.updateOrderDateTo(to);
    await tester.pumpAndSettle();
    clearInteractions(getPurchaseOrders);

    notifier.updateOrderDateFrom(from);
    await tester.pumpAndSettle();

    verifyNever(() => getPurchaseOrders(any()));
  });
}

PurchaseOrderPage _page({String itemId = 'po-1'}) => PurchaseOrderPage(
  items: [_item(id: itemId)],
  totalCount: 31,
  pageNumber: 1,
  pageSize: 20,
);

PurchaseOrderListItem _item({String id = 'po-1'}) => PurchaseOrderListItem(
  purchaseOrderId: id,
  purchaseOrderNumber: 'PO-2026-001',
  status: PurchaseOrderStatus.partiallyReceived,
  supplierName: 'Acme Supplies',
  supplierReference: 'ACME-42',
  lineCount: 3,
  expectedQuantity: 12,
  receivedQuantity: 7,
  expectedTotal: 1240.5,
  createdAt: DateTime.utc(2026, 7, 1, 10),
);

void _keepControllerAlive(ProviderContainer container) {
  final subscription = container.listen(
    purchaseOrdersControllerProvider,
    (_, _) {},
  );
  addTearDown(subscription.close);
}
