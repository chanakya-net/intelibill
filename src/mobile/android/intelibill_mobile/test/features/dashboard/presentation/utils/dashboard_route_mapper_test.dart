import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/features/dashboard/presentation/utils/dashboard_route_mapper.dart';

void main() {
  group('mapDashboardActionRoute', () {
    test('maps known web routes to mobile routes', () {
      expect(mapDashboardActionRoute('/sales'), AppRoutes.salesHistory);
      expect(mapDashboardActionRoute('/sales/new'), AppRoutes.salesNew);
      expect(mapDashboardActionRoute('/inventory/batch'), AppRoutes.inventoryBatch);
      expect(mapDashboardActionRoute('/inventory/batches'), AppRoutes.inventoryBatches);
      expect(
        mapDashboardActionRoute('/inventory/purchase-orders'),
        AppRoutes.inventory,
      );
      expect(mapDashboardActionRoute('/expenses'), AppRoutes.expenses);
      expect(mapDashboardActionRoute('/customers'), AppRoutes.customers);
      expect(mapDashboardActionRoute('/suppliers'), AppRoutes.suppliers);
    });

    test('returns null for unsupported routes', () {
      expect(mapDashboardActionRoute('/unknown'), isNull);
    });
  });
}
