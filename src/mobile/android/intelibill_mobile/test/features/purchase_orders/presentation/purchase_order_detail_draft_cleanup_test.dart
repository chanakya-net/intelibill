import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/core/storage/preferences_storage.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_draft_local_data_source.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/get_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/place_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/pages/purchase_order_detail_page.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/controllers/suppliers_controller.dart';
import 'package:mocktail/mocktail.dart';

class _MockGetPurchaseOrder extends Mock implements GetPurchaseOrder {}

class _MockPlacePurchaseOrder extends Mock implements PlacePurchaseOrder {}

class _MemoryPreferencesStorage implements PreferencesStorage {
  final values = <String, String>{};

  @override
  String? getString(String key) => values[key];

  @override
  Future<void> remove(String key) async => values.remove(key);

  @override
  Future<void> setString(String key, String value) async => values[key] = value;

  @override
  Future<void> clear() async => values.clear();

  @override
  int? getInt(String key) => null;

  @override
  bool? getBool(String key) => null;

  @override
  Future<void> setBool({required String key, required bool value}) async {}

  @override
  Future<void> setInt(String key, int value) async {}
}

void main() {
  const targetKey = PurchaseOrderDraftLocalKey(
    userId: 'user-1',
    shopId: 'shop-1',
    target: 'po-1',
  );
  const otherKey = PurchaseOrderDraftLocalKey(
    userId: 'user-1',
    shopId: 'shop-1',
    target: 'po-2',
  );

  testWidgets('app-bar Place removes only its persisted draft after success', (
    tester,
  ) async {
    final getPurchaseOrder = _MockGetPurchaseOrder();
    final placePurchaseOrder = _MockPlacePurchaseOrder();
    final source = await _seedDrafts(targetKey, otherKey);
    final placed = _purchaseOrder(status: PurchaseOrderStatus.placed);
    when(
      () => getPurchaseOrder('po-1'),
    ).thenAnswer((_) async => _purchaseOrder());
    when(() => placePurchaseOrder('po-1')).thenAnswer((_) async => placed);

    final container = _container(
      getPurchaseOrder: getPurchaseOrder,
      placePurchaseOrder: placePurchaseOrder,
      source: source,
      targetKey: targetKey,
    );
    addTearDown(container.dispose);
    await tester.pumpWidget(_app(container));
    await tester.pumpAndSettle();

    await tester.tap(
      find.byKey(const Key('purchase-order-detail-place-button')),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('Place order'));
    await tester.pumpAndSettle();

    expect(await source.load(targetKey), isNull);
    expect(await source.load(otherKey), isNotNull);
  });

  testWidgets('app-bar Place retains its persisted draft after failure', (
    tester,
  ) async {
    final getPurchaseOrder = _MockGetPurchaseOrder();
    final placePurchaseOrder = _MockPlacePurchaseOrder();
    final source = await _seedDrafts(targetKey, otherKey);
    when(
      () => getPurchaseOrder('po-1'),
    ).thenAnswer((_) async => _purchaseOrder());
    when(() => placePurchaseOrder('po-1')).thenThrow(
      AppException(failure: const Failure.network()),
    );

    final container = _container(
      getPurchaseOrder: getPurchaseOrder,
      placePurchaseOrder: placePurchaseOrder,
      source: source,
      targetKey: targetKey,
    );
    addTearDown(container.dispose);
    await tester.pumpWidget(_app(container));
    await tester.pumpAndSettle();

    await tester.tap(
      find.byKey(const Key('purchase-order-detail-place-button')),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('Place order'));
    await tester.pumpAndSettle();

    expect(await source.load(targetKey), isNotNull);
    expect(await source.load(otherKey), isNotNull);
  });
}

ProviderContainer _container({
  required GetPurchaseOrder getPurchaseOrder,
  required PlacePurchaseOrder placePurchaseOrder,
  required PurchaseOrderDraftLocalDataSource source,
  required PurchaseOrderDraftLocalKey targetKey,
}) {
  return ProviderContainer(
    overrides: [
      getPurchaseOrderProvider.overrideWithValue(getPurchaseOrder),
      placePurchaseOrderProvider.overrideWithValue(placePurchaseOrder),
      purchaseOrderDraftLocalDataSourceProvider.overrideWith(
        (_) async => source,
      ),
      purchaseOrderDraftLocalKeyProvider('po-1').overrideWithValue(targetKey),
      suppliersControllerProvider.overrideWithValue(
        const SuppliersState(suppliers: [_activeSupplier]),
      ),
    ],
  );
}

Widget _app(ProviderContainer container) => UncontrolledProviderScope(
  container: container,
  child: MaterialApp(
    theme: AppTheme.lightTheme,
    localizationsDelegates: AppLocalizations.localizationsDelegates,
    supportedLocales: AppLocalizations.supportedLocales,
    home: const PurchaseOrderDetailPage(purchaseOrderId: 'po-1'),
  ),
);

Future<PurchaseOrderDraftLocalDataSource> _seedDrafts(
  PurchaseOrderDraftLocalKey targetKey,
  PurchaseOrderDraftLocalKey otherKey,
) async {
  final source = PurchaseOrderDraftLocalDataSource(_MemoryPreferencesStorage());
  final record = PurchaseOrderDraftLocalRecord(
    updatedAt: DateTime.utc(2026, 7, 19),
    draft: PurchaseOrderDraft(notes: 'saved locally'),
  );
  await source.save(targetKey, record);
  await source.save(otherKey, record);
  return source;
}

const _activeSupplier = Supplier(
  supplierId: 'supplier-1',
  name: 'Supplier 1',
  address: '123 Street',
  isSystem: false,
  isActive: true,
  isPreferred: false,
  balanceDue: 0,
);

PurchaseOrder _purchaseOrder({
  PurchaseOrderStatus status = PurchaseOrderStatus.draft,
}) => PurchaseOrder(
  purchaseOrderId: 'po-1',
  purchaseOrderNumber: 'PO-001',
  status: status,
  supplierId: 'supplier-1',
  lines: const [
    PurchaseOrderLine(
      lineId: 'line-1',
      itemId: 'item-1',
      description: 'Test item',
      expectedQuantity: 1,
      receivedQuantity: 0,
      remainingQuantity: 1,
      unitCost: 1,
      lineTotal: 1,
    ),
  ],
  expectedTotal: 1,
  createdAt: DateTime(2026, 7, 19),
);
