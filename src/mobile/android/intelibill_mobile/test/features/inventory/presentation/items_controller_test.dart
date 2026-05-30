import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/item.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/create_item.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/get_items.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/update_item.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/items_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockGetItems extends Mock implements GetItems {}

class MockCreateItem extends Mock implements CreateItem {}

class MockUpdateItem extends Mock implements UpdateItem {}

final _testItems = [
  const Item(
    itemId: 'item-1',
    name: 'Widget A',
    barcode: 'BAR001',
    uom: 'pcs',
    isActive: true,
    currentStock: 100,
  ),
  const Item(
    itemId: 'item-2',
    name: 'Widget B',
    barcode: 'BAR002',
    description: 'A quality widget',
    uom: 'kg',
    isActive: false,
    currentStock: 3,
  ),
  const Item(
    itemId: 'item-3',
    name: 'Gadget C',
    barcode: 'BAR003',
    uom: 'box',
    isActive: true,
    currentStock: 25,
  ),
];

void main() {
  late MockGetItems getItems;
  late MockCreateItem createItem;
  late MockUpdateItem updateItem;

  setUp(() {
    getItems = MockGetItems();
    createItem = MockCreateItem();
    updateItem = MockUpdateItem();
  });

  ProviderContainer makeContainer() {
    return ProviderContainer(
      overrides: [
        getItemsProvider.overrideWithValue(getItems),
        createItemProvider.overrideWithValue(createItem),
        updateItemProvider.overrideWithValue(updateItem),
      ],
    );
  }

  group('ItemsController', () {
    test('starts in loading state', () {
      when(() => getItems()).thenAnswer((_) async => _testItems);

      final container = makeContainer();
      addTearDown(container.dispose);

      expect(container.read(itemsControllerProvider).isLoading, true);
      expect(container.read(itemsControllerProvider).items, isEmpty);
    });

    test('loads items on build and transitions to loaded state', () async {
      when(() => getItems()).thenAnswer((_) async => _testItems);

      final container = makeContainer();
      addTearDown(container.dispose);

      await container.read(itemsControllerProvider.notifier).refresh();

      final state = container.read(itemsControllerProvider);
      expect(state.isLoading, false);
      expect(state.items, _testItems);
      expect(state.failure, isNull);
    });

    test('loads empty list and shows empty state', () async {
      when(() => getItems()).thenAnswer((_) async => []);

      final container = makeContainer();
      addTearDown(container.dispose);

      await container.read(itemsControllerProvider.notifier).refresh();

      final state = container.read(itemsControllerProvider);
      expect(state.isLoading, false);
      expect(state.items, isEmpty);
      expect(state.failure, isNull);
    });

    test('transitions to error state when use case throws', () async {
      when(() => getItems()).thenThrow(Exception('network error'));

      final container = makeContainer();
      addTearDown(container.dispose);

      await container.read(itemsControllerProvider.notifier).refresh();

      final state = container.read(itemsControllerProvider);
      expect(state.isLoading, false);
      expect(state.items, isEmpty);
      expect(state.failure, isNotNull);
    });

    test('transitions to error state when AppException thrown', () async {
      when(
        () => getItems(),
      ).thenThrow(AppException(failure: const Failure.network()));

      final container = makeContainer();
      addTearDown(container.dispose);

      await container.read(itemsControllerProvider.notifier).refresh();

      final state = container.read(itemsControllerProvider);
      expect(state.failure, isA<NetworkFailure>());
    });

    group('search filtering', () {
      test('filters items by name', () async {
        when(() => getItems()).thenAnswer((_) async => _testItems);

        final container = makeContainer();
        addTearDown(container.dispose);

        await container.read(itemsControllerProvider.notifier).refresh();
        container.read(itemsControllerProvider.notifier).updateSearch('Widget');

        final filtered = container.read(itemsControllerProvider).filteredItems;
        expect(filtered.length, 2);
      });

      test('filters items by barcode', () async {
        when(() => getItems()).thenAnswer((_) async => _testItems);

        final container = makeContainer();
        addTearDown(container.dispose);

        await container.read(itemsControllerProvider.notifier).refresh();
        container.read(itemsControllerProvider.notifier).updateSearch('BAR003');

        final filtered = container.read(itemsControllerProvider).filteredItems;
        expect(filtered.length, 1);
        expect(filtered[0].name, 'Gadget C');
      });

      test('filters items by unit of measure', () async {
        when(() => getItems()).thenAnswer((_) async => _testItems);

        final container = makeContainer();
        addTearDown(container.dispose);

        await container.read(itemsControllerProvider.notifier).refresh();
        container.read(itemsControllerProvider.notifier).updateSearch('kg');

        final filtered = container.read(itemsControllerProvider).filteredItems;
        expect(filtered.length, 1);
        expect(filtered[0].name, 'Widget B');
      });

      test('returns all items when search is cleared', () async {
        when(() => getItems()).thenAnswer((_) async => _testItems);

        final container = makeContainer();
        addTearDown(container.dispose);

        await container.read(itemsControllerProvider.notifier).refresh();
        container.read(itemsControllerProvider.notifier).updateSearch('Widget');
        container.read(itemsControllerProvider.notifier).updateSearch('');

        final filtered = container.read(itemsControllerProvider).filteredItems;
        expect(filtered.length, _testItems.length);
      });
    });

    group('create item', () {
      test('sets lastAction to created on success', () async {
        when(() => getItems()).thenAnswer((_) async => _testItems);
        when(
          () => createItem(
            name: any(named: 'name'),
            barcode: any(named: 'barcode'),
            uom: any(named: 'uom'),
            description: any(named: 'description'),
          ),
        ).thenAnswer(
          (_) async => const Item(
            itemId: 'item-new',
            name: 'New Widget',
            barcode: 'BARNEW',
            uom: 'pcs',
            isActive: true,
            currentStock: 0,
          ),
        );

        final container = makeContainer();
        addTearDown(container.dispose);

        await container
            .read(itemsControllerProvider.notifier)
            .createItem(name: 'New Widget', barcode: 'BARNEW', uom: 'pcs');

        final state = container.read(itemsControllerProvider);
        expect(state.lastAction, 'created');
        expect(state.isSubmitting, false);
        expect(state.submitFailure, isNull);
        verify(() => getItems()).called(greaterThanOrEqualTo(2));
      });

      test('stores submitFailure on AppException', () async {
        when(() => getItems()).thenAnswer((_) async => _testItems);
        when(
          () => createItem(
            name: any(named: 'name'),
            barcode: any(named: 'barcode'),
            uom: any(named: 'uom'),
            description: any(named: 'description'),
          ),
        ).thenThrow(
          AppException(failure: const Failure.validation(message: 'dup')),
        );

        final container = makeContainer();
        addTearDown(container.dispose);

        await container
            .read(itemsControllerProvider.notifier)
            .createItem(name: 'X', barcode: 'Y', uom: 'Z');

        final state = container.read(itemsControllerProvider);
        expect(state.submitFailure, isA<ValidationFailure>());
        expect(state.lastAction, isNull);
        expect(state.isSubmitting, false);
      });

      test('ignores duplicate submissions when already submitting', () async {
        when(() => getItems()).thenAnswer((_) async => _testItems);

        final container = makeContainer();
        addTearDown(container.dispose);

        container.read(itemsControllerProvider.notifier).state =
            const ItemsState(isSubmitting: true);

        await container
            .read(itemsControllerProvider.notifier)
            .createItem(name: 'X', barcode: 'Y', uom: 'Z');

        verifyNever(
          () => createItem(
            name: any(named: 'name'),
            barcode: any(named: 'barcode'),
            uom: any(named: 'uom'),
            description: any(named: 'description'),
          ),
        );
      });
    });

    group('update item', () {
      test('sets lastAction to updated on success', () async {
        when(() => getItems()).thenAnswer((_) async => _testItems);
        when(
          () => updateItem(
            itemId: any(named: 'itemId'),
            name: any(named: 'name'),
            barcode: any(named: 'barcode'),
            uom: any(named: 'uom'),
            description: any(named: 'description'),
            isActive: any(named: 'isActive'),
          ),
        ).thenAnswer(
          (_) async => const Item(
            itemId: 'item-1',
            name: 'Updated Widget',
            barcode: 'BAR001',
            uom: 'pcs',
            isActive: true,
            currentStock: 100,
          ),
        );

        final container = makeContainer();
        addTearDown(container.dispose);

        await container
            .read(itemsControllerProvider.notifier)
            .updateItem(
              itemId: 'item-1',
              name: 'Updated Widget',
              barcode: 'BAR001',
              uom: 'pcs',
              isActive: true,
            );

        final state = container.read(itemsControllerProvider);
        expect(state.lastAction, 'updated');
        expect(state.isSubmitting, false);
        expect(state.submitFailure, isNull);
        verify(() => getItems()).called(greaterThanOrEqualTo(2));
      });

      test('stores submitFailure when update throws AppException', () async {
        when(() => getItems()).thenAnswer((_) async => _testItems);
        when(
          () => updateItem(
            itemId: any(named: 'itemId'),
            name: any(named: 'name'),
            barcode: any(named: 'barcode'),
            uom: any(named: 'uom'),
            description: any(named: 'description'),
            isActive: any(named: 'isActive'),
          ),
        ).thenThrow(
          AppException(failure: const Failure.server(message: 'server err')),
        );

        final container = makeContainer();
        addTearDown(container.dispose);

        await container
            .read(itemsControllerProvider.notifier)
            .updateItem(
              itemId: 'item-1',
              name: 'X',
              barcode: 'Y',
              uom: 'Z',
              isActive: true,
            );

        final state = container.read(itemsControllerProvider);
        expect(state.submitFailure, isA<ServerFailure>());
        expect(state.lastAction, isNull);
      });
    });
  });
}
