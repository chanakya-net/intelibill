import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/inventory_adjustment.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/get_adjustment_history.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/adjustment_history_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockGetAdjustmentHistory extends Mock implements GetAdjustmentHistory {}

InventoryAdjustment _makeAdjustment(String id) => InventoryAdjustment(
      adjustmentId: id,
      batchId: 'batch-1',
      itemId: 'item-1',
      itemName: 'Rice Premium',
      batchNumber: 'BN-001',
      direction: 'Increase',
      reason: 'Found Stock',
      quantity: 10,
      costImpact: 450,
      performedAt: DateTime(2026),
      performedBy: 'Admin',
      isVoided: false,
    );

final InventoryAdjustment _adj1 = _makeAdjustment('adj-1');
final InventoryAdjustment _adj2 = _makeAdjustment('adj-2');
final InventoryAdjustment _adj3 = _makeAdjustment('adj-3');

void main() {
  late MockGetAdjustmentHistory getAdjustmentHistory;

  setUp(() {
    getAdjustmentHistory = MockGetAdjustmentHistory();
  });

  ProviderContainer makeContainer() {
    return ProviderContainer(
      overrides: [
        getAdjustmentHistoryProvider.overrideWithValue(getAdjustmentHistory),
      ],
    );
  }

  group('AdjustmentHistoryController', () {
    group('initial load', () {
      test(
          'success populates adjustments, '
          'isLoading false, hasMore reflects API', () async {
        when(
          () => getAdjustmentHistory(pageNumber: 1, pageSize: 50),
        ).thenAnswer(
          (_) async => (items: [_adj1, _adj2], hasMore: true),
        );

        final container = makeContainer();
        addTearDown(container.dispose);

        await container
            .read(adjustmentHistoryControllerProvider.notifier)
            .refresh();

        final state = container.read(adjustmentHistoryControllerProvider);
        expect(state.adjustments, [_adj1, _adj2]);
        expect(state.isLoading, false);
        expect(state.hasMore, true);
        expect(state.failure, isNull);
      });

      test('empty response yields empty adjustments and hasMore false',
          () async {
        when(
          () => getAdjustmentHistory(pageNumber: 1, pageSize: 50),
        ).thenAnswer(
          (_) async =>
              (items: <InventoryAdjustment>[], hasMore: false),
        );

        final container = makeContainer();
        addTearDown(container.dispose);

        await container
            .read(adjustmentHistoryControllerProvider.notifier)
            .refresh();

        final state = container.read(adjustmentHistoryControllerProvider);
        expect(state.adjustments, isEmpty);
        expect(state.hasMore, false);
        expect(state.isLoading, false);
      });

      test('failure sets non-null failure on state', () async {
        when(
          () => getAdjustmentHistory(pageNumber: 1, pageSize: 50),
        ).thenThrow(
          AppException(
            failure: const Failure.server(message: 'server error'),
          ),
        );

        final container = makeContainer();
        addTearDown(container.dispose);

        await container
            .read(adjustmentHistoryControllerProvider.notifier)
            .refresh();

        final state = container.read(adjustmentHistoryControllerProvider);
        expect(state.failure, isNotNull);
        expect(state.isLoading, false);
      });
    });

    group('refresh', () {
      test('resets to page 1 and replaces list', () async {
        when(
          () => getAdjustmentHistory(pageNumber: 1, pageSize: 50),
        ).thenAnswer(
          (_) async => (items: [_adj1], hasMore: true),
        );

        final container = makeContainer();
        addTearDown(container.dispose);

        await container
            .read(adjustmentHistoryControllerProvider.notifier)
            .refresh();

        // Simulate pagination state
        container.read(adjustmentHistoryControllerProvider.notifier).state =
            AdjustmentHistoryState(
          adjustments: [_adj1, _adj2],
          pageNumber: 2,
        );

        when(
          () => getAdjustmentHistory(pageNumber: 1, pageSize: 50),
        ).thenAnswer(
          (_) async => (items: [_adj3], hasMore: false),
        );

        await container
            .read(adjustmentHistoryControllerProvider.notifier)
            .refresh();

        final state = container.read(adjustmentHistoryControllerProvider);
        expect(state.adjustments, [_adj3]);
        expect(state.pageNumber, 1);
        expect(state.hasMore, false);
      });
    });

    group('loadMore', () {
      test('appends adjustments and increments pageNumber', () async {
        when(
          () => getAdjustmentHistory(pageNumber: 1, pageSize: 50),
        ).thenAnswer(
          (_) async => (items: [_adj1], hasMore: true),
        );

        final container = makeContainer();
        addTearDown(container.dispose);

        await container
            .read(adjustmentHistoryControllerProvider.notifier)
            .refresh();

        when(
          () => getAdjustmentHistory(pageNumber: 2, pageSize: 50),
        ).thenAnswer(
          (_) async => (items: [_adj2], hasMore: false),
        );

        await container
            .read(adjustmentHistoryControllerProvider.notifier)
            .loadMore();

        final state = container.read(adjustmentHistoryControllerProvider);
        expect(state.adjustments, [_adj1, _adj2]);
        expect(state.pageNumber, 2);
        expect(state.hasMore, false);
        expect(state.isLoadingMore, false);
      });

      test('does nothing when hasMore is false', () async {
        when(
          () => getAdjustmentHistory(pageNumber: 1, pageSize: 50),
        ).thenAnswer(
          (_) async => (items: [_adj1], hasMore: false),
        );

        final container = makeContainer();
        addTearDown(container.dispose);

        await container
            .read(adjustmentHistoryControllerProvider.notifier)
            .refresh();

        await container
            .read(adjustmentHistoryControllerProvider.notifier)
            .loadMore();

        final state = container.read(adjustmentHistoryControllerProvider);
        expect(state.adjustments, [_adj1]);
        expect(state.hasMore, false);
        verifyNever(
          () => getAdjustmentHistory(
            pageNumber: any(named: 'pageNumber', that: greaterThan(1)),
            pageSize: any(named: 'pageSize'),
          ),
        );
      });

      test('does not make duplicate API call while isLoadingMore is true',
          () async {
        when(
          () => getAdjustmentHistory(pageNumber: 1, pageSize: 50),
        ).thenAnswer(
          (_) async => (items: [_adj1], hasMore: true),
        );

        final container = makeContainer();
        addTearDown(container.dispose);

        await container
            .read(adjustmentHistoryControllerProvider.notifier)
            .refresh();

        container.read(adjustmentHistoryControllerProvider.notifier).state =
            container
                .read(adjustmentHistoryControllerProvider)
                .copyWith(isLoadingMore: true);

        await container
            .read(adjustmentHistoryControllerProvider.notifier)
            .loadMore();

        verifyNever(
          () => getAdjustmentHistory(
            pageNumber: any(named: 'pageNumber', that: greaterThan(1)),
            pageSize: any(named: 'pageSize'),
          ),
        );
      });
    });
  });
}
