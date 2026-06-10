import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/dashboard/data/dto/dashboard_dto.dart';

import '../dashboard_test_fixtures.dart';

void main() {
  group('DashboardDto', () {
    test('parses full dashboard JSON payload', () {
      final dto = DashboardDto.fromJson(dashboardJson);

      expect(dto.salesCount, 5);
      expect(dto.salesRevenue, 12000);
      expect(dto.netProfit, 3000);
      expect(dto.lowStockItemCount, 2);
      expect(dto.alerts, hasLength(1));
      expect(dto.alerts.first.alertType, 'LowStock');
      expect(dto.salesTrendSeries, hasLength(2));
      expect(dto.revenueVsExpenses, hasLength(2));
      expect(dto.latestSales, hasLength(1));
      expect(dto.latestSales.first.invoiceNumber, 'INV-001');
    });

    test('defaults list fields when omitted', () {
      final json = Map<String, dynamic>.from(dashboardJson)
        ..remove('alerts')
        ..remove('latestSales')
        ..remove('rankedShortageList');

      final dto = DashboardDto.fromJson(json);

      expect(dto.alerts, isEmpty);
      expect(dto.latestSales, isEmpty);
      expect(dto.rankedShortageList, isEmpty);
    });

    test('serializes core fields through toJson', () {
      final dto = DashboardDto.fromJson(dashboardJson);
      final encoded = dto.toJson();

      expect(encoded['salesCount'], 5);
      expect(encoded['stockValue'], 45000);
      expect(encoded['latestSales'], isA<List<dynamic>>());
      expect((encoded['latestSales'] as List), isNotEmpty);
    });
  });
}
