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

  testWidgets('exposes isLoadingMore and hasMore states', (tester) async {
    final container = makeContainer();
    addTearDown(container.dispose);

    await container.read(purchaseOrdersControllerProvider.notifier).refresh();
    await tester.pumpAndSettle();
    final state = container.read(purchaseOrdersControllerProvider);
    await tester.pumpAndSettle();

    expect(state.isLoadingMore, isFalse);
    expect(state.hasMore, isA<bool>());
  });

  testWidgets('loadMore increments page and appends items', (tester) async {
    final page1 = Completer<PurchaseOrderPage>();
    var calls = 0;
    when(() => getPurchaseOrders(any())).thenAnswer((_) {
      return switch (calls++) {
        0 => Future.value(_page(itemId: 'po-1')),
        _ => page1.future,
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
    await tester.pumpAndSettle();
    clearInteractions(getPurchaseOrders);

    expect(
      container.read(purchaseOrdersControllerProvider).items,
      hasLength(1),
    );

    page1.complete(
      PurchaseOrderPage(
        items: [
          _item(id: 'po-2'),
          _item(id: 'po-3'),
        ],
        totalCount: 50,
        pageNumber: 2,
        pageSize: 20,
      ),
    );
    unawaited(notifier.loadMore());
    await tester.pumpAndSettle();

    expect(
      container.read(purchaseOrdersControllerProvider).items,
      hasLength(3),
    );
    expect(
      container.read(purchaseOrdersControllerProvider).items[1].purchaseOrderId,
      'po-2',
    );
  });

  testWidgets('loadMore keeps page size and active filters', (tester) async {
    final from = DateTime(2026, 1, 1);
    final to = DateTime(2026, 12, 31);
    final container = makeContainer();
    addTearDown(container.dispose);
    _keepControllerAlive(container);
    final notifier = container.read(purchaseOrdersControllerProvider.notifier);
    await tester.pump();

    notifier.updateSearch(' paper ');
    await tester.pump(const Duration(milliseconds: 300));
    notifier.updateStatus(PurchaseOrderStatus.placed);
    await tester.pumpAndSettle();
    notifier.updateOrderDateFrom(from);
    await tester.pumpAndSettle();
    notifier.updateOrderDateTo(to);
    await tester.pumpAndSettle();
    clearInteractions(getPurchaseOrders);

    await notifier.loadMore();

    verify(
      () => getPurchaseOrders(
        PurchaseOrderFilters(
          search: 'paper',
          status: PurchaseOrderStatus.placed,
          orderDateFrom: from,
          orderDateTo: to,
          page: 2,
          pageSize: 20,
        ),
      ),
    ).called(1);
  });

  testWidgets('appends each page once and stops at total count', (
    tester,
  ) async {
    var calls = 0;
    when(() => getPurchaseOrders(any())).thenAnswer((_) {
      return switch (calls++) {
        0 => Future.value(
          PurchaseOrderPage(
            items: [_item(id: 'po-1')],
            totalCount: 3,
            pageNumber: 1,
            pageSize: 20,
          ),
        ),
        1 => Future.value(
          PurchaseOrderPage(
            items: [_item(id: 'po-2')],
            totalCount: 3,
            pageNumber: 2,
            pageSize: 20,
          ),
        ),
        _ => Future.value(
          PurchaseOrderPage(
            items: [_item(id: 'po-3')],
            totalCount: 3,
            pageNumber: 3,
            pageSize: 20,
          ),
        ),
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
    await tester.pumpAndSettle();
    clearInteractions(getPurchaseOrders);

    await notifier.loadMore();
    await notifier.loadMore();
    await notifier.loadMore();

    expect(
      container
          .read(purchaseOrdersControllerProvider)
          .items
          .map((item) => item.purchaseOrderId),
      ['po-1', 'po-2', 'po-3'],
    );
    verify(
      () => getPurchaseOrders(
        const PurchaseOrderFilters(page: 2, pageSize: 20),
      ),
    ).called(1);
    verify(
      () => getPurchaseOrders(
        const PurchaseOrderFilters(page: 3, pageSize: 20),
      ),
    ).called(1);
  });

  testWidgets('loadMore guards against concurrent loads', (tester) async {
    final page2 = Completer<PurchaseOrderPage>();
    var calls = 0;
    when(() => getPurchaseOrders(any())).thenAnswer((_) {
      return switch (calls++) {
        0 => Future.value(_page(itemId: 'po-1')),
        _ => page2.future,
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
    await tester.pumpAndSettle();
    clearInteractions(getPurchaseOrders);

    final first = notifier.loadMore();
    final second = notifier.loadMore();
    page2.complete(_page(itemId: 'po-2'));
    await first;
    await second;
    await tester.pumpAndSettle();

    verify(() => getPurchaseOrders(any())).called(1);
  });

  testWidgets('loadMore stops when hasMore is false', (tester) async {
    reset(getPurchaseOrders);
    when(() => getPurchaseOrders(any())).thenAnswer(
      (_) async => PurchaseOrderPage(
        items: [_item()],
        totalCount: 1,
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
    _keepControllerAlive(container);
    final notifier = container.read(purchaseOrdersControllerProvider.notifier);
    await tester.pump();
    await tester.pumpAndSettle();
    clearInteractions(getPurchaseOrders);

    await notifier.loadMore();
    await tester.pumpAndSettle();

    verifyNever(() => getPurchaseOrders(any()));
  });

  testWidgets('loadMore retains cards after failure', (tester) async {
    final page2 = Completer<PurchaseOrderPage>();
    var calls = 0;
    when(() => getPurchaseOrders(any())).thenAnswer((_) {
      return switch (calls++) {
        0 => Future.value(_page(itemId: 'po-1')),
        _ => page2.future,
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
    await tester.pumpAndSettle();
    clearInteractions(getPurchaseOrders);

    expect(
      container.read(purchaseOrdersControllerProvider).items,
      hasLength(1),
    );

    page2.completeError(
      AppException(failure: const Failure.network(message: 'load failed')),
    );
    await notifier.loadMore();
    await tester.pumpAndSettle();

    final state = container.read(purchaseOrdersControllerProvider);
    expect(state.items, hasLength(1));
    expect(state.loadMoreFailure, isNotNull);
  });

  testWidgets('retryLoadMore retries the failed load-more request', (
    tester,
  ) async {
    final page2 = Completer<PurchaseOrderPage>();
    var calls = 0;
    when(() => getPurchaseOrders(any())).thenAnswer((_) {
      return switch (calls++) {
        0 => Future.value(_page(itemId: 'po-1')),
        1 => page2.future,
        _ => Future.value(
          PurchaseOrderPage(
            items: [_item(id: 'po-2')],
            totalCount: 40,
            pageNumber: 2,
            pageSize: 20,
          ),
        ),
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
    await tester.pumpAndSettle();
    clearInteractions(getPurchaseOrders);

    page2.completeError(
      AppException(failure: const Failure.network(message: 'load failed')),
    );
    await notifier.loadMore();
    await tester.pumpAndSettle();

    expect(
      container.read(purchaseOrdersControllerProvider).loadMoreFailure,
      isNotNull,
    );

    await notifier.retryLoadMore();
    await tester.pumpAndSettle();

    final state = container.read(purchaseOrdersControllerProvider);
    expect(state.items, hasLength(2));
    expect(state.loadMoreFailure, isNull);
  });

  testWidgets('loadMore separates from initial/refresh failures', (
    tester,
  ) async {
    final page2 = Completer<PurchaseOrderPage>();
    var calls = 0;
    when(() => getPurchaseOrders(any())).thenAnswer((_) {
      return switch (calls++) {
        0 => Future.value(_page(itemId: 'po-1')),
        _ => page2.future,
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
    await tester.pumpAndSettle();
    clearInteractions(getPurchaseOrders);

    page2.completeError(
      AppException(failure: const Failure.network(message: 'load failed')),
    );
    await notifier.loadMore();
    await tester.pumpAndSettle();

    final state = container.read(purchaseOrdersControllerProvider);
    expect(state.failure, isNull);
    expect(state.loadMoreFailure, isNotNull);
  });

  testWidgets('retryLoadMore guards against concurrent retries', (
    tester,
  ) async {
    final retryPage = Completer<PurchaseOrderPage>();
    var calls = 0;
    when(() => getPurchaseOrders(any())).thenAnswer((_) {
      return switch (calls++) {
        0 => Future.value(_page(itemId: 'po-1')),
        1 => Future.error(
          AppException(failure: const Failure.network(message: 'failed')),
        ),
        2 => retryPage.future,
        _ => Future.value(_page(itemId: 'po-3')),
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
    await tester.pumpAndSettle();

    await notifier.loadMore();
    clearInteractions(getPurchaseOrders);
    final firstRetry = notifier.retryLoadMore();
    final secondRetry = notifier.retryLoadMore();
    retryPage.complete(_page(itemId: 'po-2'));
    await firstRetry;
    await secondRetry;
    await tester.pumpAndSettle();

    verify(
      () => getPurchaseOrders(
        const PurchaseOrderFilters(page: 2, pageSize: 20),
      ),
    ).called(1);
    expect(
      container
          .read(purchaseOrdersControllerProvider)
          .items
          .map((item) => item.purchaseOrderId),
      ['po-1', 'po-2'],
    );
  });

  testWidgets('discards stale append success after refresh', (tester) async {
    final append = Completer<PurchaseOrderPage>();
    final refresh = Completer<PurchaseOrderPage>();
    var calls = 0;
    when(() => getPurchaseOrders(any())).thenAnswer((_) {
      return switch (calls++) {
        0 => Future.value(_page(itemId: 'initial')),
        1 => append.future,
        _ => refresh.future,
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
    await tester.pumpAndSettle();

    final appendFuture = notifier.loadMore();
    final refreshFuture = notifier.refresh();
    refresh.complete(_page(itemId: 'refreshed'));
    await refreshFuture;
    append.complete(_page(itemId: 'stale'));
    await appendFuture;
    await tester.pumpAndSettle();

    final state = container.read(purchaseOrdersControllerProvider);
    expect(state.items.single.purchaseOrderId, 'refreshed');
    expect(state.isLoadingMore, isFalse);
    expect(state.loadMoreFailure, isNull);
  });

  testWidgets('discards stale append failure after filter refresh', (
    tester,
  ) async {
    final append = Completer<PurchaseOrderPage>();
    final refresh = Completer<PurchaseOrderPage>();
    var calls = 0;
    when(() => getPurchaseOrders(any())).thenAnswer((_) {
      return switch (calls++) {
        0 => Future.value(_page(itemId: 'initial')),
        1 => append.future,
        _ => refresh.future,
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
    await tester.pumpAndSettle();

    final appendFuture = notifier.loadMore();
    notifier.updateStatus(PurchaseOrderStatus.received);
    refresh.complete(_page(itemId: 'filtered'));
    await tester.pumpAndSettle();
    append.completeError(
      AppException(failure: const Failure.network(message: 'stale')),
    );
    await appendFuture;
    await tester.pumpAndSettle();

    final state = container.read(purchaseOrdersControllerProvider);
    expect(state.items.single.purchaseOrderId, 'filtered');
    expect(state.isLoadingMore, isFalse);
    expect(state.loadMoreFailure, isNull);
  });

  testWidgets('refresh exposes isRefreshing state separately', (tester) async {
    final refreshFuture = Completer<PurchaseOrderPage>();
    var calls = 0;
    when(() => getPurchaseOrders(any())).thenAnswer((_) {
      return switch (calls++) {
        0 => Future.value(_page(itemId: 'initial')),
        _ => refreshFuture.future,
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
    await tester.pumpAndSettle();

    final refreshHandle = notifier.refresh();
    await tester.pump();

    final state = container.read(purchaseOrdersControllerProvider);
    expect(state.isRefreshing, isTrue);
    expect(state.refreshFailure, isNull);

    refreshFuture.complete(_page(itemId: 'refreshed'));
    await refreshHandle;
    await tester.pumpAndSettle();

    final finalState = container.read(purchaseOrdersControllerProvider);
    expect(finalState.isRefreshing, isFalse);
  });

  testWidgets('refresh failure retains cards and exposes refreshFailure', (
    tester,
  ) async {
    final refreshFuture = Completer<PurchaseOrderPage>();
    var calls = 0;
    when(() => getPurchaseOrders(any())).thenAnswer((_) {
      return switch (calls++) {
        0 => Future.value(_page(itemId: 'po-1')),
        _ => refreshFuture.future,
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
    await tester.pumpAndSettle();

    expect(
      container.read(purchaseOrdersControllerProvider).items,
      hasLength(1),
    );

    refreshFuture.completeError(
      AppException(failure: const Failure.network(message: 'refresh failed')),
    );
    await notifier.refresh();
    await tester.pumpAndSettle();

    final state = container.read(purchaseOrdersControllerProvider);
    expect(state.items, hasLength(1));
    expect(state.isRefreshing, isFalse);
    expect(state.refreshFailure, isNotNull);
  });

  testWidgets('refresh resets pageNumber to 1 only on success', (
    tester,
  ) async {
    final refreshFuture = Completer<PurchaseOrderPage>();
    var calls = 0;
    when(() => getPurchaseOrders(any())).thenAnswer((_) {
      return switch (calls++) {
        0 => Future.value(_page(itemId: 'po-1')),
        _ => refreshFuture.future,
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
    await tester.pumpAndSettle();

    refreshFuture.completeError(
      AppException(failure: const Failure.network(message: 'fail')),
    );
    await notifier.refresh();
    await tester.pumpAndSettle();

    var state = container.read(purchaseOrdersControllerProvider);
    expect(state.pageNumber, 1);

    reset(getPurchaseOrders);
    when(() => getPurchaseOrders(any())).thenAnswer(
      (_) async => _page(itemId: 'refreshed'),
    );
    await notifier.refresh();
    await tester.pumpAndSettle();

    state = container.read(purchaseOrdersControllerProvider);
    expect(state.pageNumber, 1);
  });

  testWidgets('loadMore is blocked during refresh', (tester) async {
    final refreshFuture = Completer<PurchaseOrderPage>();
    var calls = 0;
    when(() => getPurchaseOrders(any())).thenAnswer((_) {
      return switch (calls++) {
        0 => Future.value(_page(itemId: 'po-1')),
        _ => refreshFuture.future,
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
    await tester.pumpAndSettle();
    clearInteractions(getPurchaseOrders);

    final refreshHandle = notifier.refresh();
    await tester.pump();
    await notifier.loadMore();
    await tester.pumpAndSettle();

    refreshFuture.complete(_page(itemId: 'refreshed'));
    await refreshHandle;
    await tester.pumpAndSettle();

    verify(() => getPurchaseOrders(any())).called(1);
  });

  testWidgets('stale request after refresh succeeds does not overwrite', (
    tester,
  ) async {
    final refresh = Completer<PurchaseOrderPage>();
    final stale = Completer<PurchaseOrderPage>();
    var calls = 0;
    when(() => getPurchaseOrders(any())).thenAnswer((_) {
      return switch (calls++) {
        0 => Future.value(_page(itemId: 'initial')),
        1 => stale.future,
        _ => refresh.future,
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
    await tester.pumpAndSettle();

    final staleHandle = notifier.loadMore();
    final refreshHandle = notifier.refresh();
    refresh.complete(_page(itemId: 'refreshed'));
    await refreshHandle;
    await tester.pumpAndSettle();
    stale.complete(
      PurchaseOrderPage(
        items: [_item(id: 'stale')],
        totalCount: 40,
        pageNumber: 2,
        pageSize: 20,
      ),
    );
    await staleHandle;
    await tester.pumpAndSettle();

    expect(
      container
          .read(purchaseOrdersControllerProvider)
          .items
          .single
          .purchaseOrderId,
      'refreshed',
    );
  });

  testWidgets(
    'refresh failure at page 2 preserves items, pageNumber, hasMore',
    (
      tester,
    ) async {
      final firstPage = Completer<PurchaseOrderPage>();
      final refreshFuture = Completer<PurchaseOrderPage>();
      var calls = 0;
      when(() => getPurchaseOrders(any())).thenAnswer((_) {
        return switch (calls++) {
          0 => firstPage.future,
          1 => Future.value(
            PurchaseOrderPage(
              items: [_item(id: 'po-2')],
              totalCount: 40,
              pageNumber: 2,
              pageSize: 20,
            ),
          ),
          _ => refreshFuture.future,
        };
      });
      final container = ProviderContainer(
        overrides: [
          getPurchaseOrdersProvider.overrideWithValue(getPurchaseOrders),
        ],
      );
      addTearDown(container.dispose);
      _keepControllerAlive(container);
      final notifier = container.read(
        purchaseOrdersControllerProvider.notifier,
      );

      firstPage.complete(_page(itemId: 'po-1'));
      await tester.pump();
      await tester.pumpAndSettle();

      expect(
        container.read(purchaseOrdersControllerProvider).items,
        hasLength(1),
      );
      await notifier.loadMore();
      await tester.pumpAndSettle();

      expect(
        container.read(purchaseOrdersControllerProvider).items,
        hasLength(2),
      );
      expect(container.read(purchaseOrdersControllerProvider).pageNumber, 2);
      expect(container.read(purchaseOrdersControllerProvider).hasMore, isTrue);

      refreshFuture.completeError(
        AppException(failure: const Failure.network(message: 'fail')),
      );
      await notifier.refresh();
      await tester.pumpAndSettle();

      final state = container.read(purchaseOrdersControllerProvider);
      expect(state.items, hasLength(2));
      expect(state.pageNumber, 2);
      expect(state.hasMore, isTrue);
      expect(state.refreshFailure, isNotNull);
      expect(state.failure, isNull);
    },
  );

  testWidgets('failed refresh keeps the next load-more cursor', (tester) async {
    final refresh = Completer<PurchaseOrderPage>();
    var calls = 0;
    when(() => getPurchaseOrders(any())).thenAnswer((_) {
      return switch (calls++) {
        0 => Future.value(_page(itemId: 'po-1')),
        1 => Future.value(
          PurchaseOrderPage(
            items: [_item(id: 'po-2')],
            totalCount: 60,
            pageNumber: 2,
            pageSize: 20,
          ),
        ),
        2 => refresh.future,
        _ => Future.value(
          PurchaseOrderPage(
            items: [_item(id: 'po-3')],
            totalCount: 60,
            pageNumber: 3,
            pageSize: 20,
          ),
        ),
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
    await tester.pumpAndSettle();

    await notifier.loadMore();
    final refreshHandle = notifier.refresh();
    refresh.completeError(
      AppException(failure: const Failure.network(message: 'refresh failed')),
    );
    await refreshHandle;
    await notifier.loadMore();

    verify(
      () => getPurchaseOrders(
        const PurchaseOrderFilters(page: 3, pageSize: 20),
      ),
    ).called(1);
    expect(
      container
          .read(purchaseOrdersControllerProvider)
          .items
          .map((item) => item.purchaseOrderId),
      ['po-1', 'po-2', 'po-3'],
    );
  });

  testWidgets('failed refresh after an empty filtered result has no failure', (
    tester,
  ) async {
    var calls = 0;
    when(() => getPurchaseOrders(any())).thenAnswer((_) {
      return switch (calls++) {
        0 => Future.value(_page(itemId: 'initial')),
        1 => Future.value(
          const PurchaseOrderPage(
            items: [],
            totalCount: 0,
            pageNumber: 1,
            pageSize: 20,
          ),
        ),
        _ => Future.error(
          AppException(
            failure: const Failure.network(message: 'refresh failed'),
          ),
        ),
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
    await tester.pumpAndSettle();

    notifier.updateStatus(PurchaseOrderStatus.placed);
    await tester.pumpAndSettle();
    await notifier.refresh();

    final state = container.read(purchaseOrdersControllerProvider);
    expect(state.items, isEmpty);
    expect(state.failure, isNull);
    expect(state.refreshFailure, isNotNull);
  });

  testWidgets('stale first-page search after refresh does not overwrite', (
    tester,
  ) async {
    final search = Completer<PurchaseOrderPage>();
    final refresh = Completer<PurchaseOrderPage>();
    var calls = 0;
    when(() => getPurchaseOrders(any())).thenAnswer((_) {
      return switch (calls++) {
        0 => Future.value(_page(itemId: 'initial')),
        1 => search.future,
        _ => refresh.future,
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
    await tester.pumpAndSettle();

    notifier.updateSearch('test');
    await tester.pump(const Duration(milliseconds: 300));
    final refreshHandle = notifier.refresh();
    refresh.complete(_page(itemId: 'refreshed'));
    await refreshHandle;
    await tester.pumpAndSettle();
    search.complete(_page(itemId: 'search-stale'));
    await tester.pump();
    await tester.pumpAndSettle();

    expect(
      container
          .read(purchaseOrdersControllerProvider)
          .items
          .single
          .purchaseOrderId,
      'refreshed',
    );
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
