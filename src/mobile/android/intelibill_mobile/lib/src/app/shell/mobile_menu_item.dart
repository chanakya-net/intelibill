import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/app/pages/placeholder_page.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/shell/menu_visibility.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';

enum MobileMenuSection { primary, management, profile, shop, settings }

enum MobileMenuLabelKey {
  dashboard('shellDashboard'),
  inventory('shellManageInventory'),
  sales('shellManageSales'),
  customers('shellManageCustomers'),
  more('shellMore'),
  suppliers('shellManageSuppliers'),
  expenses('shellManageExpenses'),
  users('shellManageUsers'),
  bankAccounts('shellManageBankAccounts'),
  discounts('shellManageDiscounts'),
  profile('shellProfile'),
  changePassword('shellChangePassword'),
  addShop('shellAddShop'),
  manageShop('shellManageShop'),
  language('shellLanguage'),
  logout('shellLogout'),
  newSale('shellNewSale'),
  salesHistory('shellSalesHistory'),
  profitLoss('shellProfitLossReport'),
  inventoryAdjustments('shellInventoryAdjustments'),
  inventoryBatches('shellInventoryBatchesOverview'),
  inventoryBatchInbound('shellBatchInventoryInbound'),
  addNewProduct('shellAddNewProduct')
  ;

  const MobileMenuLabelKey(this.key);

  final String key;
}

extension MobileMenuLabelKeyX on MobileMenuLabelKey {
  String label(AppLocalizations l10n) {
    switch (this) {
      case MobileMenuLabelKey.dashboard:
        return l10n.shellDashboard;
      case MobileMenuLabelKey.inventory:
        return l10n.shellManageInventory;
      case MobileMenuLabelKey.sales:
        return l10n.shellManageSales;
      case MobileMenuLabelKey.customers:
        return l10n.shellManageCustomers;
      case MobileMenuLabelKey.more:
        return l10n.shellMore;
      case MobileMenuLabelKey.suppliers:
        return l10n.shellManageSuppliers;
      case MobileMenuLabelKey.expenses:
        return l10n.shellManageExpenses;
      case MobileMenuLabelKey.users:
        return l10n.shellManageUsers;
      case MobileMenuLabelKey.bankAccounts:
        return l10n.shellManageBankAccounts;
      case MobileMenuLabelKey.discounts:
        return l10n.shellManageDiscounts;
      case MobileMenuLabelKey.profile:
        return l10n.shellProfile;
      case MobileMenuLabelKey.changePassword:
        return l10n.shellChangePassword;
      case MobileMenuLabelKey.addShop:
        return l10n.shellAddShop;
      case MobileMenuLabelKey.manageShop:
        return l10n.shellManageShop;
      case MobileMenuLabelKey.language:
        return l10n.shellLanguage;
      case MobileMenuLabelKey.logout:
        return l10n.shellLogout;
      case MobileMenuLabelKey.newSale:
        return l10n.shellNewSale;
      case MobileMenuLabelKey.salesHistory:
        return l10n.shellSalesHistory;
      case MobileMenuLabelKey.profitLoss:
        return l10n.shellProfitLossReport;
      case MobileMenuLabelKey.inventoryAdjustments:
        return l10n.shellInventoryAdjustments;
      case MobileMenuLabelKey.inventoryBatches:
        return l10n.shellInventoryBatchesOverview;
      case MobileMenuLabelKey.inventoryBatchInbound:
        return l10n.shellBatchInventoryInbound;
      case MobileMenuLabelKey.addNewProduct:
        return l10n.shellAddNewProduct;
    }
  }
}

sealed class MobileMenuDestination {
  const MobileMenuDestination();
}

enum MobileMenuActionType { openMoreMenu, logout }

class MobileMenuAction extends MobileMenuDestination {
  const MobileMenuAction(this.type);

  final MobileMenuActionType type;
}

typedef MobileMenuExtraBuilder = Object? Function(AppLocalizations l10n);

class MobileMenuRoute extends MobileMenuDestination {
  const MobileMenuRoute(this.route, {this.matchPrefix, this.extraBuilder});

  final String route;
  final String? matchPrefix;
  final MobileMenuExtraBuilder? extraBuilder;
}

class MobileMenuItem {
  const MobileMenuItem({
    required this.labelKey,
    required this.icon,
    required this.destination,
    required this.section,
    this.isVisible,
  });

  final MobileMenuLabelKey labelKey;
  final IconData icon;
  final MobileMenuDestination destination;
  final MobileMenuSection section;
  final bool Function(AuthSession? session)? isVisible;

  bool isVisibleFor(AuthSession? session) {
    return isVisible?.call(session) ?? true;
  }
}

List<MobileMenuItem> primaryNavigationItems(AuthSession? session) {
  return _primaryNavigationItems
      .where((item) => item.isVisibleFor(session))
      .toList();
}

List<MobileMenuItem> moreMenuItems(AuthSession? session) {
  return _moreMenuItems.where((item) => item.isVisibleFor(session)).toList();
}

final List<MobileMenuItem> _primaryNavigationItems = [
  const MobileMenuItem(
    labelKey: MobileMenuLabelKey.dashboard,
    icon: Icons.home_outlined,
    destination: MobileMenuRoute(
      AppRoutes.dashboard,
      matchPrefix: AppRoutes.dashboard,
    ),
    section: MobileMenuSection.primary,
  ),
  const MobileMenuItem(
    labelKey: MobileMenuLabelKey.inventory,
    icon: Icons.inventory_2_outlined,
    destination: MobileMenuRoute(
      AppRoutes.inventory,
      matchPrefix: AppRoutes.inventory,
    ),
    section: MobileMenuSection.primary,
    isVisible: _hasActiveShop,
  ),
  const MobileMenuItem(
    labelKey: MobileMenuLabelKey.sales,
    icon: Icons.point_of_sale_outlined,
    destination: MobileMenuRoute(AppRoutes.salesHistory, matchPrefix: '/sales'),
    section: MobileMenuSection.primary,
    isVisible: canManageSales,
  ),
  const MobileMenuItem(
    labelKey: MobileMenuLabelKey.customers,
    icon: Icons.people_outline,
    destination: MobileMenuRoute(
      AppRoutes.customers,
      matchPrefix: AppRoutes.customers,
    ),
    section: MobileMenuSection.primary,
    isVisible: canManageCustomers,
  ),
  const MobileMenuItem(
    labelKey: MobileMenuLabelKey.more,
    icon: Icons.more_horiz,
    destination: MobileMenuAction(MobileMenuActionType.openMoreMenu),
    section: MobileMenuSection.primary,
  ),
];

final List<MobileMenuItem> _moreMenuItems = [
  const MobileMenuItem(
    labelKey: MobileMenuLabelKey.suppliers,
    icon: Icons.local_shipping_outlined,
    destination: MobileMenuRoute(AppRoutes.suppliers),
    section: MobileMenuSection.management,
    isVisible: canManageSuppliers,
  ),
  const MobileMenuItem(
    labelKey: MobileMenuLabelKey.expenses,
    icon: Icons.receipt_long_outlined,
    destination: MobileMenuRoute(AppRoutes.expenses),
    section: MobileMenuSection.management,
    isVisible: canManageExpenses,
  ),
  const MobileMenuItem(
    labelKey: MobileMenuLabelKey.users,
    icon: Icons.groups_outlined,
    destination: MobileMenuRoute(AppRoutes.users),
    section: MobileMenuSection.management,
    isVisible: _hasSession,
  ),
  const MobileMenuItem(
    labelKey: MobileMenuLabelKey.bankAccounts,
    icon: Icons.account_balance_outlined,
    destination: MobileMenuRoute(AppRoutes.bankAccounts),
    section: MobileMenuSection.management,
    isVisible: canManageBankAccounts,
  ),
  const MobileMenuItem(
    labelKey: MobileMenuLabelKey.discounts,
    icon: Icons.sell_outlined,
    destination: MobileMenuRoute(AppRoutes.discounts),
    section: MobileMenuSection.management,
    isVisible: canManageDiscounts,
  ),
  MobileMenuItem(
    labelKey: MobileMenuLabelKey.profile,
    icon: Icons.person_outline,
    destination: MobileMenuRoute(
      AppRoutes.placeholders,
      extraBuilder: (l10n) => PlaceholderPageDetails(
        title: l10n.shellProfile,
        body: l10n.placeholderBody,
      ),
    ),
    section: MobileMenuSection.profile,
    isVisible: _hasSession,
  ),
  MobileMenuItem(
    labelKey: MobileMenuLabelKey.changePassword,
    icon: Icons.lock_outline,
    destination: MobileMenuRoute(
      AppRoutes.placeholders,
      extraBuilder: (l10n) => PlaceholderPageDetails(
        title: l10n.shellChangePassword,
        body: l10n.placeholderBody,
      ),
    ),
    section: MobileMenuSection.profile,
    isVisible: _hasSession,
  ),
  const MobileMenuItem(
    labelKey: MobileMenuLabelKey.addShop,
    icon: Icons.add_business_outlined,
    destination: MobileMenuRoute(AppRoutes.createShop),
    section: MobileMenuSection.shop,
    isVisible: isOwner,
  ),
  const MobileMenuItem(
    labelKey: MobileMenuLabelKey.manageShop,
    icon: Icons.storefront_outlined,
    destination: MobileMenuRoute(AppRoutes.manageShop),
    section: MobileMenuSection.shop,
    isVisible: _canManageShop,
  ),
  const MobileMenuItem(
    labelKey: MobileMenuLabelKey.language,
    icon: Icons.language_outlined,
    destination: MobileMenuRoute(AppRoutes.language),
    section: MobileMenuSection.settings,
  ),
  const MobileMenuItem(
    labelKey: MobileMenuLabelKey.logout,
    icon: Icons.logout,
    destination: MobileMenuAction(MobileMenuActionType.logout),
    section: MobileMenuSection.settings,
  ),
];

bool _hasActiveShop(AuthSession? session) {
  return activeShopForSession(session) != null;
}

bool _hasSession(AuthSession? session) {
  return session != null;
}

bool _canManageShop(AuthSession? session) {
  if (!isOwner(session)) {
    return false;
  }

  final shops = session?.shops;
  return shops != null && shops.isNotEmpty;
}
