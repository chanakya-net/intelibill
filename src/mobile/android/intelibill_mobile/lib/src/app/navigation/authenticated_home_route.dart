import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/shell/menu_visibility.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';

/// Resolves the default authenticated landing route based on shop role.
///
/// Owners and managers land on the dashboard; staff land on sales, matching
/// the web [dashboardGuard] behavior.
String resolveAuthenticatedHomeRoute(AuthSession? session) {
  if (isOwnerOrManager(session)) {
    return AppRoutes.dashboard;
  }

  if (canManageSales(session)) {
    return AppRoutes.salesHistory;
  }

  if (canManageInventory(session)) {
    return AppRoutes.inventory;
  }

  return AppRoutes.salesHistory;
}
