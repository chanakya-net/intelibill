import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/dashboard/domain/entities/dashboard.dart';
import 'package:intelibill_mobile/src/features/dashboard/domain/use_cases/get_dashboard.dart';
import 'package:intelibill_mobile/src/features/dashboard/presentation/controllers/dashboard_controller.dart';
import 'package:mocktail/mocktail.dart';

import '../dashboard_test_fixtures.dart';

class MockGetDashboard extends Mock implements GetDashboard {}

void main() {
  late MockGetDashboard getDashboard;

  setUpAll(() {
    registerFallbackValue(DateTime(2000));
  });

  setUp(() {
    getDashboard = MockGetDashboard();
  });

  ProviderContainer makeContainer() {
    return ProviderContainer(
      overrides: [
        getDashboardUseCaseProvider.overrideWithValue(getDashboard),
      ],
    )..listen(dashboardControllerProvider, (_, _) {});
  }

  group('DashboardController', () {
    test('starts in loading state', () {
      when(
        () => getDashboard(
          from: any(named: 'from'),
          to: any(named: 'to'),
        ),
      ).thenAnswer((_) async => testDashboard);

      final container = makeContainer();
      addTearDown(container.dispose);

      expect(container.read(dashboardControllerProvider).isLoading, isTrue);
      expect(container.read(dashboardControllerProvider).dashboard, isNull);
    });

    test('loads dashboard for default last30 period', () async {
      when(
        () => getDashboard(
          from: any(named: 'from'),
          to: any(named: 'to'),
        ),
      ).thenAnswer((_) async => testDashboard);

      final container = makeContainer();
      addTearDown(container.dispose);

      await _waitForDashboardLoad(container);

      final state = container.read(dashboardControllerProvider);
      expect(state.isLoading, isFalse);
      expect(state.dashboard?.salesCount, 5);
      expect(state.selectedPeriod, DashboardPeriod.last30);
      verify(
        () => getDashboard(
          from: any(named: 'from'),
          to: any(named: 'to'),
        ),
      ).called(greaterThanOrEqualTo(2));
    });

    test('stores failure when load fails', () async {
      when(
        () => getDashboard(
          from: any(named: 'from'),
          to: any(named: 'to'),
        ),
      ).thenThrow(
        AppException(failure: const Failure.network(message: 'offline')),
      );

      final container = makeContainer();
      addTearDown(container.dispose);

      await container.read(dashboardControllerProvider.notifier).refresh();

      final state = container.read(dashboardControllerProvider);
      expect(state.isLoading, isFalse);
      expect(state.dashboard, isNull);
      expect(state.failure, isA<NetworkFailure>());
    });

    test('setPeriod last7 reloads dashboard', () async {
      when(
        () => getDashboard(
          from: any(named: 'from'),
          to: any(named: 'to'),
        ),
      ).thenAnswer((_) async => testDashboard);

      final container = makeContainer();
      addTearDown(container.dispose);
      await container.read(dashboardControllerProvider.notifier).refresh();

      await container
          .read(dashboardControllerProvider.notifier)
          .setPeriod(DashboardPeriod.last7);

      final state = container.read(dashboardControllerProvider);
      expect(state.selectedPeriod, DashboardPeriod.last7);
      verify(
        () => getDashboard(
          from: any(named: 'from'),
          to: any(named: 'to'),
        ),
      ).called(greaterThanOrEqualTo(2));
    });

    test('setCustomRange stores range and reloads dashboard', () async {
      when(
        () => getDashboard(
          from: any(named: 'from'),
          to: any(named: 'to'),
        ),
      ).thenAnswer((_) async => testDashboard);

      final container = makeContainer();
      addTearDown(container.dispose);
      await container.read(dashboardControllerProvider.notifier).refresh();

      await container
          .read(dashboardControllerProvider.notifier)
          .setCustomRange(
            from: DateTime(2026, 5),
            to: DateTime(2026, 5, 15),
          );

      final state = container.read(dashboardControllerProvider);
      expect(state.selectedPeriod, DashboardPeriod.custom);
      expect(state.customFrom, DateTime(2026, 5));
      expect(state.customTo, DateTime(2026, 5, 15));
      verify(
        () => getDashboard(
          from: DateTime(2026, 5),
          to: DateTime(2026, 5, 15),
        ),
      ).called(1);
    });

    test('ignores stale responses when loads overlap', () async {
      final slowDashboard = Completer<Dashboard>();
      when(
        () => getDashboard(
          from: any(named: 'from'),
          to: any(named: 'to'),
        ),
      ).thenAnswer((_) => slowDashboard.future);

      final container = makeContainer();
      addTearDown(container.dispose);
      final notifier = container.read(dashboardControllerProvider.notifier);

      final initialLoad = notifier.refresh();
      await Future<void>.delayed(Duration.zero);

      when(
        () => getDashboard(
          from: any(named: 'from'),
          to: any(named: 'to'),
        ),
      ).thenAnswer(
        (_) async => Dashboard(
          generatedAt: testDashboard.generatedAt,
          startDate: testDashboard.startDate,
          endDate: testDashboard.endDate,
          salesCount: 99,
          salesRevenue: testDashboard.salesRevenue,
          hasNoSalesActivity: testDashboard.hasNoSalesActivity,
          customerCreditDue: testDashboard.customerCreditDue,
          netProfit: testDashboard.netProfit,
          netProfitChangePercent: testDashboard.netProfitChangePercent,
          lowStockItemCount: testDashboard.lowStockItemCount,
          stockValue: testDashboard.stockValue,
          supplierPayables: testDashboard.supplierPayables,
          netExpense: testDashboard.netExpense,
          alerts: testDashboard.alerts,
          salesTrendSeries: testDashboard.salesTrendSeries,
          revenueVsExpenses: testDashboard.revenueVsExpenses,
          latestSales: testDashboard.latestSales,
        ),
      );

      await notifier.setPeriod(DashboardPeriod.last7);

      expect(
        container.read(dashboardControllerProvider).dashboard?.salesCount,
        99,
      );

      slowDashboard.complete(testDashboard);
      await initialLoad;

      expect(
        container.read(dashboardControllerProvider).dashboard?.salesCount,
        99,
      );
    });

    test('refresh clears previous error before reloading', () async {
      when(
        () => getDashboard(
          from: any(named: 'from'),
          to: any(named: 'to'),
        ),
      ).thenThrow(
        AppException(failure: const Failure.network(message: 'offline')),
      );

      final container = makeContainer();
      addTearDown(container.dispose);
      await container.read(dashboardControllerProvider.notifier).refresh();

      when(
        () => getDashboard(
          from: any(named: 'from'),
          to: any(named: 'to'),
        ),
      ).thenAnswer((_) async => testDashboard);

      await container.read(dashboardControllerProvider.notifier).refresh();

      final state = container.read(dashboardControllerProvider);
      expect(state.failure, isNull);
      expect(state.dashboard?.salesCount, 5);
    });
  });
}

Future<void> _waitForDashboardLoad(ProviderContainer container) async {
  container.read(dashboardControllerProvider);
  await container.read(dashboardControllerProvider.notifier).refresh();
  for (var attempt = 0; attempt < 20; attempt++) {
    final state = container.read(dashboardControllerProvider);
    if (!state.isLoading && state.dashboard != null) {
      return;
    }
    await Future<void>.delayed(const Duration(milliseconds: 10));
  }
}
