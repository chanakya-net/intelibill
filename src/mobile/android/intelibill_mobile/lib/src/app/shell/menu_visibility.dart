import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';

UserShop? activeShopForSession(AuthSession? session) {
  if (session == null) {
    return null;
  }

  final shops = session.shops;
  if (shops == null || shops.isEmpty) {
    return null;
  }

  final activeShopId = session.activeShopId;
  if (activeShopId != null && activeShopId.isNotEmpty) {
    for (final shop in shops) {
      if (shop.shopId == activeShopId) {
        return shop;
      }
    }
  }

  for (final shop in shops) {
    if (shop.isDefault) {
      return shop;
    }
  }

  return shops.first;
}

bool isOwner(AuthSession? session) {
  return _hasRole(session, const {'owner'});
}

bool isOwnerOrManager(AuthSession? session) {
  return _hasRole(session, const {'owner', 'manager'});
}

bool canManageCustomers(AuthSession? session) {
  return isOwnerOrManager(session);
}

bool canManageSuppliers(AuthSession? session) {
  return isOwner(session);
}

bool canManageSales(AuthSession? session) {
  return _hasRole(session, const {'owner', 'manager', 'staff'});
}

bool canManageExpenses(AuthSession? session) {
  return isOwnerOrManager(session);
}

bool canManageDiscounts(AuthSession? session) {
  return isOwnerOrManager(session);
}

bool canManageBankAccounts(AuthSession? session) {
  return isOwner(session);
}

bool _hasRole(AuthSession? session, Set<String> allowedRoles) {
  final role = activeShopForSession(session)?.role.toLowerCase();
  if (role == null || role.isEmpty) {
    return false;
  }

  return allowedRoles.contains(role);
}
