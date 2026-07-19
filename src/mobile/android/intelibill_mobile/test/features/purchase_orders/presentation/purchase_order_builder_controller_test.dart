import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/create_purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/get_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/update_purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_builder_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/item.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/use_cases/get_suppliers.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/controllers/suppliers_controller.dart';
import 'package:mocktail/mocktail.dart';

class _MockGetSuppliers extends Mock implements GetSuppliers {}

class _MockCreatePurchaseOrderDraft extends Mock
    implements CreatePurchaseOrderDraft {}

class _MockGetPurchaseOrder extends Mock implements GetPurchaseOrder {}

class _MockUpdatePurchaseOrderDraft extends Mock
    implements UpdatePurchaseOrderDraft {}

void main() {
  setUpAll(() {
    registerFallbackValue(const PurchaseOrderDraft());
  });

  late _MockGetSuppliers getSuppliers;
  late _MockCreatePurchaseOrderDraft createDraft;
  late _MockGetPurchaseOrder getPurchaseOrder;
  late _MockUpdatePurchaseOrderDraft updateDraft;

  setUp(() {
    getSuppliers = _MockGetSuppliers();
    createDraft = _MockCreatePurchaseOrderDraft();
    getPurchaseOrder = _MockGetPurchaseOrder();
    updateDraft = _MockUpdatePurchaseOrderDraft();
    when(() => getSuppliers()).thenAnswer((_) async => _suppliers);
  });

  ProviderContainer makeContainer() {
    return ProviderContainer(
      overrides: [
        getSuppliersUseCaseProvider.overrideWithValue(getSuppliers),
        createPurchaseOrderDraftProvider.overrideWithValue(createDraft),
        getPurchaseOrderProvider.overrideWithValue(getPurchaseOrder),
        updatePurchaseOrderDraftProvider.overrideWithValue(updateDraft),
      ],
    );
  }

  test('loads only active non-system suppliers', () async {
    final container = makeContainer();
    addTearDown(container.dispose);

    await container
        .read(purchaseOrderBuilderControllerProvider('new').notifier)
        .loadSuppliers();

    final state = container.read(purchaseOrderBuilderControllerProvider('new'));
    expect(state.suppliers.map((supplier) => supplier.supplierId), ['active']);
  });

  test(
    'selecting a created catalog item preserves the current draft',
    () async {
      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(
        purchaseOrderBuilderControllerProvider('new').notifier,
      );
      controller.setSupplierReferenceNumber('REF-3');
      controller.setNotes('Existing notes');
      controller.addItem(
        itemId: 'existing-item',
        description: 'Existing item',
        expectedQuantity: 2,
        unitCost: 5,
      );
      const created = Item(
        itemId: 'created-item',
        name: 'Created item',
        barcode: 'CREATED-1',
        uom: 'pcs',
        isActive: true,
        currentStock: 0,
      );

      controller.selectCreatedCatalogItem(created);

      final state = container.read(
        purchaseOrderBuilderControllerProvider('new'),
      );
      expect(state.selectedCatalogItem, created);
      expect(state.supplierReferenceNumber, 'REF-3');
      expect(state.notes, 'Existing notes');
      expect(state.lines.single.itemId, 'existing-item');
    },
  );

  test('rejects header limits and invalid date ordering', () async {
    final container = makeContainer();
    addTearDown(container.dispose);
    final controller = container.read(
      purchaseOrderBuilderControllerProvider('new').notifier,
    );
    await controller.loadSuppliers();

    controller.setSupplierReferenceNumber('x' * 101);
    controller.setNotes('n' * 1001);
    controller.setOrderDate(DateTime(2026, 7, 20));
    controller.setExpectedDeliveryDate(DateTime(2026, 7, 19));
    final saved = await controller.save();

    expect(saved, isNull);
    expect(
      container.read(purchaseOrderBuilderControllerProvider('new')).failure,
      isA<ValidationFailure>(),
    );
    verifyNever(() => createDraft(any()));
  });

  test('saves an incomplete draft without supplier or lines', () async {
    final container = makeContainer();
    addTearDown(container.dispose);
    final expected = _purchaseOrder();
    when(() => createDraft(any())).thenAnswer((_) async => expected);
    final controller = container.read(
      purchaseOrderBuilderControllerProvider('new').notifier,
    );
    await controller.loadSuppliers();

    final result = await controller.save();

    expect(result, expected);
    verify(
      () => createDraft(
        const PurchaseOrderDraft(
          supplierReferenceNumber: '',
          notes: '',
          lines: [],
        ),
      ),
    ).called(1);
  });

  test('retains entered values after a failed save and allows retry', () async {
    final container = makeContainer();
    addTearDown(container.dispose);
    final controller = container.read(
      purchaseOrderBuilderControllerProvider('new').notifier,
    );
    await controller.loadSuppliers();
    controller.selectSupplier(_suppliers.first);
    controller.setNotes('Keep this note');
    when(() => createDraft(any())).thenThrow(
      AppException(failure: const Failure.network(message: 'offline')),
    );

    expect(await controller.save(), isNull);
    var state = container.read(purchaseOrderBuilderControllerProvider('new'));
    expect(state.selectedSupplier, _suppliers.first);
    expect(state.notes, 'Keep this note');
    expect(state.failure, isA<NetworkFailure>());

    when(() => createDraft(any())).thenAnswer((_) async => _purchaseOrder());
    expect(await controller.save(), isNotNull);
    state = container.read(purchaseOrderBuilderControllerProvider('new'));
    expect(state.selectedSupplier, _suppliers.first);
    expect(state.notes, 'Keep this note');
  });

  test('guards duplicate submissions while first save is pending', () async {
    final container = makeContainer();
    addTearDown(container.dispose);
    final pending = Completer<PurchaseOrder>();
    when(() => createDraft(any())).thenAnswer((_) => pending.future);
    final controller = container.read(
      purchaseOrderBuilderControllerProvider('new').notifier,
    );

    final first = controller.save();
    final second = controller.save();
    expect(await second, isNull);
    verify(() => createDraft(any())).called(1);

    pending.complete(_purchaseOrder());
    expect(await first, isNotNull);
  });

  test('saves with added and merged lines', () async {
    final container = makeContainer();
    addTearDown(container.dispose);
    final expected = _purchaseOrder();
    PurchaseOrderDraft? capturedDraft;
    when(() => createDraft(any())).thenAnswer((invocation) async {
      capturedDraft = invocation.positionalArguments[0] as PurchaseOrderDraft;
      return expected;
    });
    final controller = container.read(
      purchaseOrderBuilderControllerProvider('new').notifier,
    );
    await controller.loadSuppliers();

    controller.addItem(
      itemId: 'item-1',
      description: 'Widget A',
      expectedQuantity: 2,
      unitCost: 10.0,
    );
    controller.addItem(
      itemId: 'item-2',
      description: 'Widget B',
      expectedQuantity: 3,
      unitCost: 5.0,
    );
    controller.addItem(
      itemId: 'item-1',
      description: 'Widget A',
      expectedQuantity: 1,
      unitCost: 12.0,
    );

    final result = await controller.save();

    expect(result, expected);
    expect(capturedDraft, isNotNull);
    expect(capturedDraft!.lines.length, 2);
    final line1 = capturedDraft!.lines.firstWhere((l) => l.itemId == 'item-1');
    expect(line1.expectedQuantity, 3);
    expect(line1.unitCost, 12.0);
    final line2 = capturedDraft!.lines.firstWhere((l) => l.itemId == 'item-2');
    expect(line2.expectedQuantity, 3);
    expect(line2.unitCost, 5.0);
  });

  test('loads an existing draft and updates it by route ID', () async {
    final container = makeContainer();
    addTearDown(container.dispose);
    final loaded = _purchaseOrder(
      supplierId: 'active',
      supplierReferenceNumber: 'REF-OLD',
      notes: 'Original note',
      lines: const [
        PurchaseOrderLine(
          lineId: 'line-1',
          itemId: 'item-1',
          description: 'Widget',
          expectedQuantity: 2,
          receivedQuantity: 0,
          remainingQuantity: 2,
          unitCost: 10,
          lineTotal: 20,
        ),
      ],
    );
    when(() => getPurchaseOrder('po-1')).thenAnswer((_) async => loaded);
    when(() => updateDraft(any(), any())).thenAnswer((_) async => loaded);
    final subscription = container.listen(
      purchaseOrderBuilderControllerProvider('po-1'),
      (_, _) {},
    );
    addTearDown(subscription.close);

    final controller = container.read(
      purchaseOrderBuilderControllerProvider('po-1').notifier,
    );
    await controller.loadSuppliers();
    await Future<void>.delayed(Duration.zero);
    controller.setNotes('Changed note');

    final result = await controller.save();

    expect(result, loaded);
    final state = container.read(
      purchaseOrderBuilderControllerProvider('po-1'),
    );
    expect(state.selectedSupplier, _suppliers.first);
    expect(state.supplierReferenceNumber, 'REF-OLD');
    expect(state.lines.single.itemId, 'item-1');
    verify(
      () => updateDraft(
        'po-1',
        const PurchaseOrderDraft(
          supplierId: 'active',
          supplierReferenceNumber: 'REF-OLD',
          notes: 'Changed note',
          lines: [
            PurchaseOrderDraftLine(
              itemId: 'item-1',
              description: 'Widget',
              expectedQuantity: 2,
              unitCost: 10,
            ),
          ],
        ),
      ),
    ).called(1);
  });

  test('rejects a non-draft edit target without updating', () async {
    final container = makeContainer();
    addTearDown(container.dispose);
    when(
      () => getPurchaseOrder('po-placed'),
    ).thenAnswer(
      (_) async => _purchaseOrder(status: PurchaseOrderStatus.placed),
    );
    final subscription = container.listen(
      purchaseOrderBuilderControllerProvider('po-placed'),
      (_, _) {},
    );
    addTearDown(subscription.close);

    container.read(purchaseOrderBuilderControllerProvider('po-placed'));
    await Future<void>.delayed(Duration.zero);

    final state = container.read(
      purchaseOrderBuilderControllerProvider('po-placed'),
    );
    expect(state.redirectToDetailId, 'po-placed');
    verifyNever(() => updateDraft(any(), any()));
  });

  test('retains local changes after a failed draft update', () async {
    final container = makeContainer();
    addTearDown(container.dispose);
    when(
      () => getPurchaseOrder('po-1'),
    ).thenAnswer((_) async => _purchaseOrder());
    when(() => updateDraft(any(), any())).thenThrow(
      AppException(failure: const Failure.server(message: 'Changed elsewhere')),
    );
    final subscription = container.listen(
      purchaseOrderBuilderControllerProvider('po-1'),
      (_, _) {},
    );
    addTearDown(subscription.close);

    final controller = container.read(
      purchaseOrderBuilderControllerProvider('po-1').notifier,
    );
    await Future<void>.delayed(Duration.zero);
    controller.setNotes('Keep this edit');

    expect(await controller.save(), isNull);
    final state = container.read(
      purchaseOrderBuilderControllerProvider('po-1'),
    );
    expect(state.notes, 'Keep this edit');
    expect(state.failure, isA<ServerFailure>());
  });
}

final _suppliers = [
  const Supplier(
    supplierId: 'active',
    name: 'Active Supplier',
    isSystem: false,
    isActive: true,
    isPreferred: true,
    balanceDue: 0,
  ),
  const Supplier(
    supplierId: 'inactive',
    name: 'Inactive Supplier',
    isSystem: false,
    isActive: false,
    isPreferred: false,
    balanceDue: 0,
  ),
  const Supplier(
    supplierId: 'system',
    name: 'System Supplier',
    isSystem: true,
    isActive: true,
    isPreferred: false,
    balanceDue: 0,
  ),
];

PurchaseOrder _purchaseOrder({
  PurchaseOrderStatus status = PurchaseOrderStatus.draft,
  String? supplierId,
  String? supplierReferenceNumber,
  String? notes,
  List<PurchaseOrderLine> lines = const [],
}) => PurchaseOrder(
  purchaseOrderId: 'po-1',
  purchaseOrderNumber: 'PO-2026-000001',
  status: status,
  supplierId: supplierId,
  supplierReferenceNumber: supplierReferenceNumber,
  notes: notes,
  lines: lines,
  expectedTotal: 0,
  createdAt: DateTime(2026, 7, 19),
);
