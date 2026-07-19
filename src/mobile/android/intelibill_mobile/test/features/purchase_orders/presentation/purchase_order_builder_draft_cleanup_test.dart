import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_draft_local_data_source.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/create_purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/get_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/update_purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_builder_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/use_cases/get_suppliers.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/controllers/suppliers_controller.dart';
import 'package:mocktail/mocktail.dart';

class _MockLocalDataSource extends Mock
    implements PurchaseOrderDraftLocalDataSource {}

class _MockGetSuppliers extends Mock implements GetSuppliers {}

class _MockGetPurchaseOrder extends Mock implements GetPurchaseOrder {}

class _MockCreatePurchaseOrderDraft extends Mock
    implements CreatePurchaseOrderDraft {}

class _MockUpdatePurchaseOrderDraft extends Mock
    implements UpdatePurchaseOrderDraft {}

void main() {
  setUpAll(() {
    registerFallbackValue(_newKey);
    registerFallbackValue(
      PurchaseOrderDraftLocalRecord(
        updatedAt: DateTime.utc(2026),
        draft: const PurchaseOrderDraft(),
      ),
    );
    registerFallbackValue(const PurchaseOrderDraft());
  });

  late _MockLocalDataSource local;
  late _MockGetSuppliers getSuppliers;
  late _MockGetPurchaseOrder getPurchaseOrder;
  late _MockCreatePurchaseOrderDraft createDraft;
  late _MockUpdatePurchaseOrderDraft updateDraft;

  setUp(() {
    local = _MockLocalDataSource();
    getSuppliers = _MockGetSuppliers();
    getPurchaseOrder = _MockGetPurchaseOrder();
    createDraft = _MockCreatePurchaseOrderDraft();
    updateDraft = _MockUpdatePurchaseOrderDraft();
    when(() => local.save(any(), any())).thenAnswer((_) async {});
    when(() => local.remove(any())).thenAnswer((_) async {});
    when(() => getSuppliers()).thenAnswer((_) async => []);
  });

  ProviderContainer makeContainer({
    String target = 'new',
    PurchaseOrderDraftLocalKey key = _newKey,
  }) {
    return ProviderContainer(
      overrides: [
        purchaseOrderDraftLocalDataSourceProvider.overrideWith(
          (ref) async => local,
        ),
        purchaseOrderDraftLocalKeyProvider(target).overrideWithValue(key),
        getSuppliersUseCaseProvider.overrideWithValue(getSuppliers),
        getPurchaseOrderProvider.overrideWithValue(getPurchaseOrder),
        createPurchaseOrderDraftProvider.overrideWithValue(createDraft),
        updatePurchaseOrderDraftProvider.overrideWithValue(updateDraft),
      ],
    );
  }

  testWidgets('Continue keeps recovered edits and local record', (
    tester,
  ) async {
    when(() => local.load(_newKey)).thenAnswer(
      (_) async => PurchaseOrderDraftLocalRecord(
        updatedAt: DateTime.utc(2026, 7, 19),
        draft: const PurchaseOrderDraft(notes: 'recovered'),
      ),
    );
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

    controller.continueRecoveredDraft();

    final state = container.read(
      purchaseOrderBuilderControllerProvider('new'),
    );
    expect(state.hasRecoveredDraft, isFalse);
    expect(state.notes, 'recovered');
    verifyNever(() => local.remove(any()));
    subscription.close();
    container.dispose();
    await tester.pump();
  });

  testWidgets('discard local reloads fresh edit state then removes exact key', (
    tester,
  ) async {
    when(() => local.load(_editKey)).thenAnswer(
      (_) async => PurchaseOrderDraftLocalRecord(
        updatedAt: DateTime.utc(2026, 7, 19),
        draft: const PurchaseOrderDraft(notes: 'recovered'),
      ),
    );
    var loads = 0;
    when(() => getPurchaseOrder('po-1')).thenAnswer((_) async {
      loads++;
      return _purchaseOrder(notes: loads == 1 ? 'initial' : 'fresh server');
    });
    final container = makeContainer(target: 'po-1', key: _editKey);
    final subscription = container.listen(
      purchaseOrderBuilderControllerProvider('po-1'),
      (_, _) {},
    );
    await tester.pump();
    await tester.pump();
    await tester.pump();
    final controller = container.read(
      purchaseOrderBuilderControllerProvider('po-1').notifier,
    );

    expect(await controller.discardRecoveredDraftAndReload(), isTrue);

    final state = container.read(
      purchaseOrderBuilderControllerProvider('po-1'),
    );
    expect(state.notes, 'fresh server');
    expect(state.hasRecoveredDraft, isFalse);
    verify(() => local.remove(_editKey)).called(1);
    subscription.close();
    container.dispose();
    await tester.pump();
  });

  testWidgets('ordinary discard cancels autosave and removes exact key', (
    tester,
  ) async {
    when(() => local.load(_newKey)).thenAnswer((_) async => null);
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
    controller.setNotes('discard me');

    expect(await controller.discardLocalDraft(), isTrue);
    await tester.pump(const Duration(milliseconds: 300));

    verify(() => local.remove(_newKey)).called(1);
    verifyNever(() => local.save(any(), any()));
    subscription.close();
    container.dispose();
    await tester.pump();
  });

  testWidgets('cleanup retry does not repeat successful create mutation', (
    tester,
  ) async {
    when(() => local.load(_newKey)).thenAnswer((_) async => null);
    final saved = _purchaseOrder(notes: 'saved');
    when(() => createDraft(any())).thenAnswer((_) async => saved);
    when(() => local.remove(_newKey)).thenThrow(StateError('disk full'));
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

    expect(await controller.save(), isNull);
    var state = container.read(
      purchaseOrderBuilderControllerProvider('new'),
    );
    expect(state.savedDraft, isNull);
    expect(state.storageWarning, isNotNull);
    verify(() => createDraft(any())).called(1);

    when(() => local.remove(_newKey)).thenAnswer((_) async {});
    expect(await controller.retryStorage(), isTrue);
    state = container.read(purchaseOrderBuilderControllerProvider('new'));
    expect(state.savedDraft, saved);
    verifyNever(() => createDraft(any()));
    verify(() => local.remove(_newKey)).called(2);
    subscription.close();
    container.dispose();
    await tester.pump();
  });

  testWidgets('API failure preserves editable state and local record', (
    tester,
  ) async {
    when(() => local.load(_newKey)).thenAnswer((_) async => null);
    when(() => createDraft(any())).thenThrow(
      AppException(failure: const Failure.network(message: 'offline')),
    );
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
    controller.setNotes('keep offline');

    expect(await controller.save(), isNull);

    final state = container.read(
      purchaseOrderBuilderControllerProvider('new'),
    );
    expect(state.notes, 'keep offline');
    expect(state.failure, isA<NetworkFailure>());
    verifyNever(() => local.remove(any()));
    subscription.close();
    container.dispose();
    await tester.pump();
  });

  testWidgets('update cleanup preserves unresolved recovered supplier ID', (
    tester,
  ) async {
    when(() => local.load(_editKey)).thenAnswer(
      (_) async => PurchaseOrderDraftLocalRecord(
        updatedAt: DateTime.utc(2026, 7, 19),
        draft: const PurchaseOrderDraft(
          supplierId: 'supplier-offline',
          notes: 'recovered',
        ),
      ),
    );
    when(
      () => getPurchaseOrder('po-1'),
    ).thenAnswer((_) async => _purchaseOrder(notes: 'server'));
    final saved = _purchaseOrder(
      notes: 'recovered',
      supplierId: 'supplier-offline',
    );
    when(() => updateDraft(any(), any())).thenAnswer((_) async => saved);
    final container = makeContainer(target: 'po-1', key: _editKey);
    final subscription = container.listen(
      purchaseOrderBuilderControllerProvider('po-1'),
      (_, _) {},
    );
    await tester.pump();
    await tester.pump();
    await tester.pump();
    final controller = container.read(
      purchaseOrderBuilderControllerProvider('po-1').notifier,
    );

    expect(await controller.save(), saved);

    verify(
      () => updateDraft(
        'po-1',
        const PurchaseOrderDraft(
          supplierId: 'supplier-offline',
          supplierReferenceNumber: '',
          notes: 'recovered',
        ),
      ),
    ).called(1);
    verify(() => local.remove(_editKey)).called(1);
    subscription.close();
    container.dispose();
    await tester.pump();
  });
}

const _newKey = PurchaseOrderDraftLocalKey(
  userId: 'user-1',
  shopId: 'shop-1',
  target: 'new',
);

const _editKey = PurchaseOrderDraftLocalKey(
  userId: 'user-1',
  shopId: 'shop-1',
  target: 'po-1',
);

PurchaseOrder _purchaseOrder({String? notes, String? supplierId}) =>
    PurchaseOrder(
      purchaseOrderId: 'po-1',
      purchaseOrderNumber: 'PO-1',
      status: PurchaseOrderStatus.draft,
      supplierId: supplierId,
      notes: notes,
      lines: const [],
      expectedTotal: 0,
      createdAt: DateTime(2026, 7, 19),
    );
