import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/dashboard/data/mappers/dashboard_mapper.dart';
import 'package:intelibill_mobile/src/features/dashboard/domain/entities/dashboard.dart';

import '../dashboard_test_fixtures.dart';

void main() {
  group('DashboardMapper', () {
    test('maps dto fields to domain entity', () {
      final dashboard = DashboardMapper.toDomain(dashboardDto);

      expect(dashboard.salesCount, 5);
      expect(dashboard.salesRevenue, 12000);
      expect(dashboard.netProfit, 3000);
      expect(dashboard.lowStockItemCount, 2);
      expect(dashboard.stockValue, 45000);
      expect(dashboard.supplierPayables, 800);
      expect(dashboard.alerts, hasLength(1));
      expect(dashboard.alerts.first.actionLabel, 'Manage inventory');
      expect(dashboard.salesTrendSeries, hasLength(2));
      expect(dashboard.revenueVsExpenses, hasLength(2));
      expect(dashboard.latestSales, hasLength(1));
    });

    test('parses date-only fields at start of day', () {
      final dashboard = DashboardMapper.toDomain(dashboardDto);

      expect(dashboard.startDate, DateTime.parse('2026-05-12T00:00:00'));
      expect(dashboard.endDate, DateTime.parse('2026-06-10T00:00:00'));
      expect(
        dashboard.salesTrendSeries.first.date,
        DateTime.parse('2026-06-09T00:00:00'),
      );
    });

    test('maps nullable trend collections to empty lists', () {
      final dashboard = DashboardMapper.toDomain(
        dashboardDto.copyWith(
          salesTrendSeries: null,
          revenueVsExpenses: null,
        ),
      );

      expect(dashboard.salesTrendSeries, isEmpty);
      expect(dashboard.revenueVsExpenses, isEmpty);
    });

    test('returns Dashboard domain type', () {
      final dashboard = DashboardMapper.toDomain(dashboardDto);

      expect(dashboard, isA<Dashboard>());
    });
  });
}
