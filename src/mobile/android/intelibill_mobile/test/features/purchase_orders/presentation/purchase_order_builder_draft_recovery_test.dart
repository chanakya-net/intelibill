import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_draft_local_data_source.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/get_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_builder_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/use_cases/get_suppliers.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/controllers/suppliers_controller.dart';
import 'package:mocktail/mocktail.dart';

class _MockLocalDataSource extends Mock
    implements PurchaseOrderDraftLocalDataSource {}

class _MockGetSuppliers extends Mock implements GetSuppliers {}

class _MockGetPurchaseOrder extends Mock implements GetPurchaseOrder {}

class _ScopeNotifier extends Notifier<PurchaseOrderDraftLocalKey> {
  @override
  PurchaseOrderDraftLocalKey build() => _key;

  PurchaseOrderDraftLocalKey get key => state;

  set key(PurchaseOrderDraftLocalKey value) => state = value;
}

final _scopeProvider =
    NotifierProvider<_ScopeNotifier, PurchaseOrderDraftLocalKey>(
      _ScopeNotifier.new,
    );

void main() {
  setUpAll(() {
    registerFallbackValue(_key);
    registerFallbackValue(
      PurchaseOrderDraftLocalRecord(
        updatedAt: DateTime.utc(2026),
        draft: const PurchaseOrderDraft(),
      ),
    );
  });

  late _MockLocalDataSource local;
  late _MockGetSuppliers getSuppliers;
  late _MockGetPurchaseOrder getPurchaseOrder;

  setUp(() {
    local = _MockLocalDataSource();
    getSuppliers = _MockGetSuppliers();
    getPurchaseOrder = _MockGetPurchaseOrder();
    when(() => local.load(_key)).thenAnswer((_) async => null);
    when(() => local.save(any(), any())).thenAnswer((_) async {});
    when(() => local.remove(any())).thenAnswer((_) async {});
    when(() => getSuppliers()).thenAnswer((_) async => []);
  });

  ProviderContainer makeContainer({
    String target = 'new',
    PurchaseOrderDraftLocalKey key = _key,
  }) {
    return ProviderContainer(
      overrides: [
        purchaseOrderDraftLocalDataSourceProvider.overrideWith(
          (ref) async => local,
        ),
        purchaseOrderDraftLocalKeyProvider(target).overrideWithValue(key),
        getSuppliersUseCaseProvider.overrideWithValue(getSuppliers),
        getPurchaseOrderProvider.overrideWithValue(getPurchaseOrder),
      ],
    );
  }

  testWidgets('line changes persist the full draft immediately', (
    tester,
  ) async {
    final container = makeContainer();
    final subscription = container.listen(
      purchaseOrderBuilderControllerProvider('new'),
      (_, _) {},
    );
    await tester.pump();
    await tester.pump();
    final controller = container.read(
      purchaseOrderBuilderControllerProvider('new').notifier,
    );

    controller.addItem(
      itemId: 'item-1',
      description: 'Widget',
      expectedQuantity: 2,
      unitCost: 3.5,
    );
    await tester.pump();

    final captured =
        verify(
              () => local.save(_key, captureAny()),
            ).captured.single
            as PurchaseOrderDraftLocalRecord;
    expect(captured.updatedAt.isUtc, isTrue);
    expect(captured.draft.lines.single.itemId, 'item-1');
    subscription.close();
    container.dispose();
    await tester.pump();
  });

  testWidgets('header changes coalesce behind a 275 ms debounce', (
    tester,
  ) async {
    final container = makeContainer();
    final subscription = container.listen(
      purchaseOrderBuilderControllerProvider('new'),
      (_, _) {},
    );
    await tester.pump();
    await tester.pump();
    final controller = container.read(
      purchaseOrderBuilderControllerProvider('new').notifier,
    );

    controller.setNotes('first');
    await tester.pump(const Duration(milliseconds: 200));
    controller.setNotes('latest');
    await tester.pump(const Duration(milliseconds: 274));
    verifyNever(() => local.save(any(), any()));
    await tester.pump(const Duration(milliseconds: 1));
    await tester.pump();

    final record =
        verify(
              () => local.save(_key, captureAny()),
            ).captured.single
            as PurchaseOrderDraftLocalRecord;
    expect(record.draft.notes, 'latest');
    subscription.close();
    container.dispose();
    await tester.pump();
  });

  testWidgets('storage failure is nonblocking and retryable', (tester) async {
    when(() => local.save(any(), any())).thenThrow(StateError('disk full'));
    final container = makeContainer();
    final subscription = container.listen(
      purchaseOrderBuilderControllerProvider('new'),
      (_, _) {},
    );
    await tester.pump();
    await tester.pump();
    final controller = container.read(
      purchaseOrderBuilderControllerProvider('new').notifier,
    );

    controller.setNotes('editable offline');
    await tester.pump(const Duration(milliseconds: 275));
    await tester.pump();

    var state = container.read(
      purchaseOrderBuilderControllerProvider('new'),
    );
    expect(state.notes, 'editable offline');
    expect(state.storageWarning, isNotNull);

    when(() => local.save(any(), any())).thenAnswer((_) async {});
    await controller.retryStorage();
    state = container.read(purchaseOrderBuilderControllerProvider('new'));
    expect(state.storageWarning, isNull);
    subscription.close();
    container.dispose();
    await tester.pump();
  });

  testWidgets('matching local draft wins over edit server state', (
    tester,
  ) async {
    final localRecord = PurchaseOrderDraftLocalRecord(
      updatedAt: DateTime.utc(2026, 7, 19, 14),
      draft: const PurchaseOrderDraft(
        supplierId: 'supplier-offline',
        notes: 'local notes',
      ),
    );
    when(() => local.load(_editKey)).thenAnswer((_) async => localRecord);
    when(
      () => getPurchaseOrder('po-1'),
    ).thenAnswer((_) async => _purchaseOrder(notes: 'server notes'));
    final container = makeContainer(target: 'po-1', key: _editKey);
    final subscription = container.listen(
      purchaseOrderBuilderControllerProvider('po-1'),
      (_, _) {},
    );

    await tester.pump();
    await tester.pump();
    await tester.pump();

    final state = container.read(
      purchaseOrderBuilderControllerProvider('po-1'),
    );
    expect(state.notes, 'local notes');
    expect(state.hasRecoveredDraft, isTrue);
    expect(state.recoveredDraftUpdatedAt, localRecord.updatedAt);
    expect(state.isLoadingEdit, isFalse);
    subscription.close();
    container.dispose();
    await tester.pump();
  });

  testWidgets('shop change rejects stale local-load completion', (
    tester,
  ) async {
    final staleLoad = Completer<PurchaseOrderDraftLocalRecord?>();
    when(() => local.load(_key)).thenAnswer((_) => staleLoad.future);
    when(() => local.load(_otherShopKey)).thenAnswer(
      (_) async => PurchaseOrderDraftLocalRecord(
        updatedAt: DateTime.utc(2026, 7, 20),
        draft: const PurchaseOrderDraft(notes: 'shop two'),
      ),
    );
    final container = ProviderContainer(
      overrides: [
        purchaseOrderDraftLocalDataSourceProvider.overrideWith(
          (ref) async => local,
        ),
        purchaseOrderDraftLocalKeyProvider(
          'new',
        ).overrideWith((ref) => ref.watch(_scopeProvider)),
        getSuppliersUseCaseProvider.overrideWithValue(getSuppliers),
      ],
    );
    final subscription = container.listen(
      purchaseOrderBuilderControllerProvider('new'),
      (_, _) {},
    );
    await tester.pump();

    container.read(_scopeProvider.notifier).key = _otherShopKey;
    await tester.pump();
    await tester.pump();
    staleLoad.complete(
      PurchaseOrderDraftLocalRecord(
        updatedAt: DateTime.utc(2026, 7, 19),
        draft: const PurchaseOrderDraft(notes: 'stale shop one'),
      ),
    );
    await tester.pump();

    final state = container.read(
      purchaseOrderBuilderControllerProvider('new'),
    );
    expect(state.notes, 'shop two');
    expect(state.hasRecoveredDraft, isTrue);
    subscription.close();
    container.dispose();
    await tester.pump();
  });
}

const _key = PurchaseOrderDraftLocalKey(
  userId: 'user-1',
  shopId: 'shop-1',
  target: 'new',
);

const _editKey = PurchaseOrderDraftLocalKey(
  userId: 'user-1',
  shopId: 'shop-1',
  target: 'po-1',
);

const _otherShopKey = PurchaseOrderDraftLocalKey(
  userId: 'user-1',
  shopId: 'shop-2',
  target: 'new',
);

PurchaseOrder _purchaseOrder({String? notes}) => PurchaseOrder(
  purchaseOrderId: 'po-1',
  purchaseOrderNumber: 'PO-1',
  status: PurchaseOrderStatus.draft,
  notes: notes,
  lines: const [],
  expectedTotal: 0,
  createdAt: DateTime(2026, 7, 19),
);
