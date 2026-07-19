import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/get_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_preview_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/shop_details.dart';
import 'package:intelibill_mobile/src/features/shops/domain/use_cases/get_shop_use_case.dart';
import 'package:intelibill_mobile/src/features/shops/shops_providers.dart';
import 'package:mocktail/mocktail.dart';

class _MockGetPurchaseOrder extends Mock implements GetPurchaseOrder {}

class _MockGetShopUseCase extends Mock implements GetShopUseCase {}

void main() {
  final order = PurchaseOrder(
    purchaseOrderId: 'po-1',
    purchaseOrderNumber: 'PO-1',
    status: PurchaseOrderStatus.placed,
    lines: const [],
    expectedTotal: 0,
    createdAt: DateTime(2026),
  );

  test(
    'PO failure is retryable while shop load is started concurrently',
    () async {
      final getOrder = _MockGetPurchaseOrder();
      final getShop = _MockGetShopUseCase();
      var calls = 0;
      when(() => getOrder('po-1')).thenAnswer((_) async {
        calls++;
        if (calls == 1) throw AppException(failure: const Failure.network());
        return order;
      });
      when(() => getShop('shop-1')).thenAnswer(
        (_) async => const ShopDetails(
          id: 'shop-1',
          name: 'Store',
          address: 'A',
          city: 'Pune',
          state: 'MH',
          pincode: '411001',
          bankAccounts: [],
        ),
      );
      final container = ProviderContainer(
        overrides: [
          getPurchaseOrderProvider.overrideWithValue(getOrder),
          getShopUseCaseProvider.overrideWithValue(getShop),
          activeShopIdProvider.overrideWithValue('shop-1'),
        ],
      );
      addTearDown(container.dispose);
      final subscription = container.listen(
        purchaseOrderPreviewControllerProvider('po-1'),
        (_, _) {},
      );
      addTearDown(subscription.close);

      container.read(purchaseOrderPreviewControllerProvider('po-1'));
      await _settle();
      expect(
        container.read(purchaseOrderPreviewControllerProvider('po-1')).failure,
        isNotNull,
      );
      verify(() => getShop('shop-1')).called(1);

      await container
          .read(purchaseOrderPreviewControllerProvider('po-1').notifier)
          .retry();
      expect(
        container
            .read(purchaseOrderPreviewControllerProvider('po-1'))
            .purchaseOrder,
        order,
      );
    },
  );

  test('shop failure is tolerated when purchase order loads', () async {
    final getOrder = _MockGetPurchaseOrder();
    final getShop = _MockGetShopUseCase();
    when(() => getOrder('po-1')).thenAnswer((_) async => order);
    when(() => getShop('shop-1')).thenThrow(Exception('shop unavailable'));
    final container = ProviderContainer(
      overrides: [
        getPurchaseOrderProvider.overrideWithValue(getOrder),
        getShopUseCaseProvider.overrideWithValue(getShop),
        activeShopIdProvider.overrideWithValue('shop-1'),
      ],
    );
    addTearDown(container.dispose);
    final subscription = container.listen(
      purchaseOrderPreviewControllerProvider('po-1'),
      (_, _) {},
    );
    addTearDown(subscription.close);

    container.read(purchaseOrderPreviewControllerProvider('po-1'));
    await _settle();

    final state = container.read(
      purchaseOrderPreviewControllerProvider('po-1'),
    );
    expect(state.purchaseOrder, order);
    expect(state.shop, isNull);
    expect(state.failure, isNull);
  });
}

Future<void> _settle() =>
    Future<void>.delayed(const Duration(milliseconds: 10));
