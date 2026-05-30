import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/inventory_batch.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/adjust_inventory_batch.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/get_inventory_batches.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/inventory_batches_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockGetInventoryBatches extends Mock implements GetInventoryBatches {}

class MockAdjustInventoryBatch extends Mock implements AdjustInventoryBatch {}

final _testBatches = [
  InventoryBatch(
    batchId: 'batch-1',
    itemId: 'item-1',
    itemName: 'Rice Premium',
    itemBarcode: 'BAR001',
    itemUom: 'kg',
    batchNumber: 'BN-001',
    quantity: 100,
    costPrice: 45,
    mrp: 60,
    salesPrice: 55,
    taxRate: 0,
    taxIncluded: false,
    isVoided: false,
    createdAt: DateTime(2026),
  ),
  InventoryBatch(
    batchId: 'batch-2',
    itemId: 'item-2',
    itemName: 'Wheat Flour',
    itemBarcode: 'BAR002',
    itemUom: 'kg',
    batchNumber: 'BN-002',
    quantity: 50,
    costPrice: 30,
    mrp: 40,
    salesPrice: 38,
    taxRate: 5,
    taxIncluded: true,
    isVoided: false,
    createdAt: DateTime(2026, 1, 2),
  ),
];

void main() {
  late MockGetInventoryBatches getInventoryBatches;
  late MockAdjustInventoryBatch adjustInventoryBatch;

  setUp(() {
    getInventoryBatches = MockGetInventoryBatches();
    adjustInventoryBatch = MockAdjustInventoryBatch();
  });

  ProviderContainer makeContainer() {
    return ProviderContainer(
      overrides: [
        getInventoryBatchesProvider.overrideWithValue(getInventoryBatches),
        adjustInventoryBatchProvider.overrideWithValue(adjustInventoryBatch),
      ],
    );
  }

  group('InventoryBatchesController', () {
    test('starts in loading state', () {
      when(getInventoryBatches.call).thenAnswer((_) async => _testBatches);

      final container = makeContainer();
      addTearDown(container.dispose);

      expect(
        container.read(inventoryBatchesControllerProvider).isLoading,
        true,
      );
      expect(
        container.read(inventoryBatchesControllerProvider).batches,
        isEmpty,
      );
    });

    test('loads batches and transitions to loaded state', () async {
      when(getInventoryBatches.call).thenAnswer((_) async => _testBatches);

      final container = makeContainer();
      addTearDown(container.dispose);

      await container
          .read(inventoryBatchesControllerProvider.notifier)
          .refresh();

      final state = container.read(inventoryBatchesControllerProvider);
      expect(state.isLoading, false);
      expect(state.batches, _testBatches);
      expect(state.failure, isNull);
    });

    test('loads empty list and shows empty state', () async {
      when(getInventoryBatches.call).thenAnswer((_) async => []);

      final container = makeContainer();
      addTearDown(container.dispose);

      await container
          .read(inventoryBatchesControllerProvider.notifier)
          .refresh();

      final state = container.read(inventoryBatchesControllerProvider);
      expect(state.isLoading, false);
      expect(state.batches, isEmpty);
      expect(state.failure, isNull);
    });

    test('transitions to error state when use case throws', () async {
      when(getInventoryBatches.call).thenThrow(Exception('network error'));

      final container = makeContainer();
      addTearDown(container.dispose);

      await container
          .read(inventoryBatchesControllerProvider.notifier)
          .refresh();

      final state = container.read(inventoryBatchesControllerProvider);
      expect(state.isLoading, false);
      expect(state.failure, isNotNull);
    });

    test('transitions to error state when AppException thrown', () async {
      when(
        getInventoryBatches.call,
      ).thenThrow(AppException(failure: const Failure.network()));

      final container = makeContainer();
      addTearDown(container.dispose);

      await container
          .read(inventoryBatchesControllerProvider.notifier)
          .refresh();

      final state = container.read(inventoryBatchesControllerProvider);
      expect(state.failure, isA<NetworkFailure>());
    });

    group('search filtering', () {
      test('filters batches by item name', () async {
        when(getInventoryBatches.call).thenAnswer((_) async => _testBatches);

        final container = makeContainer();
        addTearDown(container.dispose);

        await container
            .read(inventoryBatchesControllerProvider.notifier)
            .refresh();
        container
            .read(inventoryBatchesControllerProvider.notifier)
            .updateSearch('Rice');

        final filtered = container
            .read(inventoryBatchesControllerProvider)
            .filteredBatches;
        expect(filtered.length, 1);
        expect(filtered[0].itemName, 'Rice Premium');
      });

      test('filters batches by batch number', () async {
        when(getInventoryBatches.call).thenAnswer((_) async => _testBatches);

        final container = makeContainer();
        addTearDown(container.dispose);

        await container
            .read(inventoryBatchesControllerProvider.notifier)
            .refresh();
        container
            .read(inventoryBatchesControllerProvider.notifier)
            .updateSearch('BN-002');

        final filtered = container
            .read(inventoryBatchesControllerProvider)
            .filteredBatches;
        expect(filtered.length, 1);
        expect(filtered[0].batchNumber, 'BN-002');
      });

      test('returns all batches when search is cleared', () async {
        when(getInventoryBatches.call).thenAnswer((_) async => _testBatches);

        final container = makeContainer();
        addTearDown(container.dispose);

        await container
            .read(inventoryBatchesControllerProvider.notifier)
            .refresh();
        container
            .read(inventoryBatchesControllerProvider.notifier)
            .updateSearch('Rice');
        container
            .read(inventoryBatchesControllerProvider.notifier)
            .updateSearch('');

        final filtered = container
            .read(inventoryBatchesControllerProvider)
            .filteredBatches;
        expect(filtered.length, _testBatches.length);
      });
    });

    group('adjustBatch', () {
      test(
        'success sets lastAdjustedBatchId and clears submitFailure',
        () async {
          when(getInventoryBatches.call).thenAnswer((_) async => _testBatches);
          when(
            () => adjustInventoryBatch(
              batchId: any(named: 'batchId'),
              direction: any(named: 'direction'),
              reason: any(named: 'reason'),
              quantity: any(named: 'quantity'),
              performedAt: any(named: 'performedAt'),
              notes: any(named: 'notes'),
            ),
          ).thenAnswer((_) async {});

          final container = makeContainer();
          addTearDown(container.dispose);

          await container
              .read(inventoryBatchesControllerProvider.notifier)
              .refresh();
          await container
              .read(inventoryBatchesControllerProvider.notifier)
              .adjustBatch(
                batchId: 'batch-1',
                direction: 'Decrease',
                reason: 'Damaged',
                quantity: 10,
              );

          final state = container.read(inventoryBatchesControllerProvider);
          expect(state.lastAdjustedBatchId, 'batch-1');
          expect(state.submitFailure, isNull);
          expect(state.isSubmitting, false);
        },
      );

      test(
        'failure sets submitFailure and leaves lastAdjustedBatchId null',
        () async {
          when(getInventoryBatches.call).thenAnswer((_) async => _testBatches);
          when(
            () => adjustInventoryBatch(
              batchId: any(named: 'batchId'),
              direction: any(named: 'direction'),
              reason: any(named: 'reason'),
              quantity: any(named: 'quantity'),
              performedAt: any(named: 'performedAt'),
              notes: any(named: 'notes'),
            ),
          ).thenThrow(
            AppException(
              failure: const Failure.server(message: 'server error'),
            ),
          );

          final container = makeContainer();
          addTearDown(container.dispose);

          await container
              .read(inventoryBatchesControllerProvider.notifier)
              .refresh();
          await container
              .read(inventoryBatchesControllerProvider.notifier)
              .adjustBatch(
                batchId: 'batch-1',
                direction: 'Decrease',
                reason: 'Damaged',
                quantity: 10,
              );

          final state = container.read(inventoryBatchesControllerProvider);
          expect(state.submitFailure, isA<ServerFailure>());
          expect(state.lastAdjustedBatchId, isNull);
          expect(state.isSubmitting, false);
        },
      );

      test('ignores duplicate submissions when already submitting', () async {
        when(getInventoryBatches.call).thenAnswer((_) async => _testBatches);

        final container = makeContainer();
        addTearDown(container.dispose);

        container.read(inventoryBatchesControllerProvider.notifier).state =
            const InventoryBatchesState(isSubmitting: true);

        await container
            .read(inventoryBatchesControllerProvider.notifier)
            .adjustBatch(
              batchId: 'batch-1',
              direction: 'Decrease',
              reason: 'Damaged',
              quantity: 10,
            );

        verifyNever(
          () => adjustInventoryBatch(
            batchId: any(named: 'batchId'),
            direction: any(named: 'direction'),
            reason: any(named: 'reason'),
            quantity: any(named: 'quantity'),
            performedAt: any(named: 'performedAt'),
            notes: any(named: 'notes'),
          ),
        );
      });
    });
  });
}
