import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/create_purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_builder_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/use_cases/get_suppliers.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/controllers/suppliers_controller.dart';
import 'package:mocktail/mocktail.dart';

class _MockGetSuppliers extends Mock implements GetSuppliers {}

class _MockCreatePurchaseOrderDraft extends Mock
    implements CreatePurchaseOrderDraft {}

void main() {
  setUpAll(() {
    registerFallbackValue(const PurchaseOrderDraft());
  });

  late _MockGetSuppliers getSuppliers;
  late _MockCreatePurchaseOrderDraft createDraft;

  setUp(() {
    getSuppliers = _MockGetSuppliers();
    createDraft = _MockCreatePurchaseOrderDraft();
    when(() => getSuppliers()).thenAnswer((_) async => _suppliers);
  });

  ProviderContainer makeContainer() {
    return ProviderContainer(
      overrides: [
        getSuppliersUseCaseProvider.overrideWithValue(getSuppliers),
        createPurchaseOrderDraftProvider.overrideWithValue(createDraft),
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

PurchaseOrder _purchaseOrder() => PurchaseOrder(
  purchaseOrderId: 'po-1',
  purchaseOrderNumber: 'PO-2026-000001',
  status: PurchaseOrderStatus.draft,
  lines: const [],
  expectedTotal: 0,
  createdAt: DateTime(2026, 7, 19),
);
