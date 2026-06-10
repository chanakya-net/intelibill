import 'package:intelibill_mobile/src/app/router/app_router.dart';

String? mapDashboardActionRoute(String actionRoute) {
  switch (actionRoute) {
    case '/sales':
      return AppRoutes.salesHistory;
    case '/sales/new':
      return AppRoutes.salesNew;
    case '/sales/profit-loss':
      return AppRoutes.profitLoss;
    case '/inventory':
      return AppRoutes.inventory;
    case '/inventory/batch':
      return AppRoutes.inventoryBatch;
    case '/inventory/batches':
      return AppRoutes.inventoryBatches;
    case '/inventory/purchase-orders':
      return AppRoutes.inventory;
    case '/expenses':
      return AppRoutes.expenses;
    case '/customers':
      return AppRoutes.customers;
    case '/suppliers':
      return AppRoutes.suppliers;
    default:
      return null;
  }
}
