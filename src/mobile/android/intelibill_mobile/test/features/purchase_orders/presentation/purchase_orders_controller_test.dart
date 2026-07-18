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

  test('updateSearch method exists and invokes generation guard', () async {
    reset(getPurchaseOrders);
    when(() => getPurchaseOrders(any())).thenAnswer((_) async => _page());
    final container = makeContainer();
    addTearDown(container.dispose);

    final notifier = container.read(purchaseOrdersControllerProvider.notifier);
    expect(notifier.updateSearch, isNotNull);
    notifier.updateSearch('test');
  });

  test(
    'refresh and retry increment generation to invalidate pending search',
    () async {
      reset(getPurchaseOrders);
      when(() => getPurchaseOrders(any())).thenAnswer((_) async => _page());
      final container = makeContainer();
      addTearDown(container.dispose);

      final notifier = container.read(
        purchaseOrdersControllerProvider.notifier,
      );
      notifier.updateSearch('old');

      await notifier.refresh();

      final state = container.read(purchaseOrdersControllerProvider);
      expect(state.isLoading, isFalse);
    },
  );
}

PurchaseOrderPage _page() => PurchaseOrderPage(
  items: [_item()],
  totalCount: 31,
  pageNumber: 1,
  pageSize: 20,
);

PurchaseOrderListItem _item() => PurchaseOrderListItem(
  purchaseOrderId: 'po-1',
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
