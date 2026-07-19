import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_builder_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/create_purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/use_cases/get_suppliers.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/controllers/suppliers_controller.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';
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
    when(() => getSuppliers()).thenAnswer((_) async => []);
  });

  ProviderContainer makeContainer() {
    return ProviderContainer(
      overrides: [
        getSuppliersUseCaseProvider.overrideWithValue(getSuppliers),
        createPurchaseOrderDraftProvider.overrideWithValue(createDraft),
      ],
    );
  }

  group('PurchaseOrderBuilderController.addItem', () {
    test('adds new line', () async {
      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(
        purchaseOrderBuilderControllerProvider('new').notifier,
      );

      controller.addItem(
        itemId: 'item-1',
        description: 'Widget',
        expectedQuantity: 5,
        unitCost: 10.0,
      );

      final state = container.read(
        purchaseOrderBuilderControllerProvider('new'),
      );
      expect(state.lines.length, 1);
      expect(state.lines[0].itemId, 'item-1');
      expect(state.lines[0].description, 'Widget');
      expect(state.lines[0].expectedQuantity, 5);
      expect(state.lines[0].unitCost, 10.0);
    });

    test('merges duplicate item using latest cost', () async {
      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(
        purchaseOrderBuilderControllerProvider('new').notifier,
      );

      controller.addItem(
        itemId: 'item-1',
        description: 'Widget',
        expectedQuantity: 3,
        unitCost: 10.0,
      );
      controller.addItem(
        itemId: 'item-1',
        description: 'Widget',
        expectedQuantity: 2,
        unitCost: 12.0,
      );

      final state = container.read(
        purchaseOrderBuilderControllerProvider('new'),
      );
      expect(state.lines.length, 1);
      expect(state.lines[0].expectedQuantity, 5);
      expect(state.lines[0].unitCost, 12.0);
    });

    test('rejects invalid quantity', () async {
      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(
        purchaseOrderBuilderControllerProvider('new').notifier,
      );

      controller.addItem(
        itemId: 'item-1',
        description: 'Widget',
        expectedQuantity: 0,
        unitCost: 10.0,
      );

      final state = container.read(
        purchaseOrderBuilderControllerProvider('new'),
      );
      expect(state.lines, isEmpty);
      expect(state.failure, isA<ValidationFailure>());
    });

    test('calculates running expected total after add', () async {
      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(
        purchaseOrderBuilderControllerProvider('new').notifier,
      );

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

      final state = container.read(
        purchaseOrderBuilderControllerProvider('new'),
      );
      expect(state.expectedTotal, 35.0);
    });
  });

  group('PurchaseOrderBuilderController.updateLine', () {
    test('updates existing line by index', () async {
      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(
        purchaseOrderBuilderControllerProvider('new').notifier,
      );

      controller.addItem(
        itemId: 'item-1',
        description: 'Widget',
        expectedQuantity: 5,
        unitCost: 10.0,
      );

      controller.updateLine(
        index: 0,
        expectedQuantity: 8,
        unitCost: 12.0,
      );

      final state = container.read(
        purchaseOrderBuilderControllerProvider('new'),
      );
      expect(state.lines[0].expectedQuantity, 8);
      expect(state.lines[0].unitCost, 12.0);
    });

    test('rejects invalid quantity on update', () async {
      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(
        purchaseOrderBuilderControllerProvider('new').notifier,
      );

      controller.addItem(
        itemId: 'item-1',
        description: 'Widget',
        expectedQuantity: 5,
        unitCost: 10.0,
      );

      controller.updateLine(
        index: 0,
        expectedQuantity: -1,
        unitCost: 12.0,
      );

      final state = container.read(
        purchaseOrderBuilderControllerProvider('new'),
      );
      expect(state.lines[0].expectedQuantity, 5);
      expect(state.failure, isA<ValidationFailure>());
    });

    test('recalculates total after update', () async {
      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(
        purchaseOrderBuilderControllerProvider('new').notifier,
      );

      controller.addItem(
        itemId: 'item-1',
        description: 'Widget',
        expectedQuantity: 5,
        unitCost: 10.0,
      );

      controller.updateLine(index: 0, expectedQuantity: 10, unitCost: 15.0);

      final state = container.read(
        purchaseOrderBuilderControllerProvider('new'),
      );
      expect(state.expectedTotal, 150.0);
    });
  });

  group('PurchaseOrderBuilderController.removeLine', () {
    test('removes line by index', () async {
      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(
        purchaseOrderBuilderControllerProvider('new').notifier,
      );

      controller.addItem(
        itemId: 'item-1',
        description: 'Widget',
        expectedQuantity: 5,
        unitCost: 10.0,
      );
      controller.addItem(
        itemId: 'item-2',
        description: 'Gadget',
        expectedQuantity: 3,
        unitCost: 5.0,
      );

      controller.removeLine(0);

      final state = container.read(
        purchaseOrderBuilderControllerProvider('new'),
      );
      expect(state.lines.length, 1);
      expect(state.lines[0].itemId, 'item-2');
    });

    test('recalculates total after remove', () async {
      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(
        purchaseOrderBuilderControllerProvider('new').notifier,
      );

      controller.addItem(
        itemId: 'item-1',
        description: 'Widget',
        expectedQuantity: 2,
        unitCost: 10.0,
      );
      controller.addItem(
        itemId: 'item-2',
        description: 'Gadget',
        expectedQuantity: 3,
        unitCost: 5.0,
      );

      controller.removeLine(0);

      final state = container.read(
        purchaseOrderBuilderControllerProvider('new'),
      );
      expect(state.expectedTotal, 15.0);
    });
  });

  group('PurchaseOrderBuilderController.expectedTotal', () {
    test('returns zero for empty lines', () async {
      final container = makeContainer();
      addTearDown(container.dispose);

      final state = container.read(
        purchaseOrderBuilderControllerProvider('new'),
      );
      expect(state.expectedTotal, 0.0);
    });

    test('sums all line totals', () async {
      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(
        purchaseOrderBuilderControllerProvider('new').notifier,
      );

      controller.addItem(
        itemId: 'item-1',
        description: 'Widget',
        expectedQuantity: 2,
        unitCost: 10.5,
      );
      controller.addItem(
        itemId: 'item-2',
        description: 'Gadget',
        expectedQuantity: 3,
        unitCost: 7.0,
      );

      final state = container.read(
        purchaseOrderBuilderControllerProvider('new'),
      );
      expect(state.expectedTotal, 42.0);
    });
  });
}
