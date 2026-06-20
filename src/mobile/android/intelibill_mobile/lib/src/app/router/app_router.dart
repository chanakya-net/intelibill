import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'package:intelibill_mobile/src/app/navigation/authenticated_home_route.dart';
import 'package:intelibill_mobile/src/app/pages/language_page.dart';
import 'package:intelibill_mobile/src/app/pages/placeholder_page.dart';
import 'package:intelibill_mobile/src/app/shell/menu_visibility.dart';
import 'package:intelibill_mobile/src/app/shell/mobile_app_shell.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/pages/change_password_page.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/pages/login_page.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/pages/profile_settings_page.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/pages/update_profile_page.dart';
import 'package:intelibill_mobile/src/features/customers/presentation/pages/customers_page.dart';
import 'package:intelibill_mobile/src/features/dashboard/presentation/pages/dashboard_page.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/pages/add_inventory_page.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/pages/adjustment_history_page.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/pages/inventory_batches_page.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/pages/items_page.dart';
import 'package:intelibill_mobile/src/features/discounts/presentation/pages/discounts_page.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/pages/sales_history_page.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/pages/create_shop_page.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/pages/manage_shop_page.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/pages/suppliers_page.dart';
import 'package:intelibill_mobile/src/features/users/presentation/pages/users_page.dart';

class AppRoutes {
  static const String root = '/';
  static const String login = '/login';
  static const String forgotPassword = '/forgot-password';
  static const String register = '/register';
  static const String profile = '/profile';
  static const String profileEdit = '/profile/edit';
  static const String profileChangePassword = '/profile/change-password';
  static const String dashboard = '/dashboard';
  static const String inventory = '/inventory';
  static const String inventoryBatch = '/inventory/batch';
  static const String inventoryBatches = '/inventory/batches';
  static const String inventoryAdjustments = '/inventory/adjustments';
  static const String salesNew = '/sales/new';
  static const String salesHistory = '/sales/history';
  static const String profitLoss = '/sales/profit-loss';
  static const String customers = '/customers';
  static const String suppliers = '/suppliers';
  static const String createShop = '/shops/create';
  static const String manageShop = '/shops/manage';
  static const String expenses = '/expenses';
  static const String users = '/users';
  static const String discounts = '/discounts';
  static const String bankAccounts = '/bank-accounts';
  static const String language = '/language';
  static const String placeholders = '/placeholder';
}

final goRouterProvider = Provider<GoRouter>((ref) {
  final refreshNotifier = ValueNotifier<int>(0);
  ref
    ..onDispose(refreshNotifier.dispose)
    ..listen(authControllerProvider, (previous, next) {
      refreshNotifier.value += 1;
    });

  return GoRouter(
    initialLocation: AppRoutes.dashboard,
    refreshListenable: refreshNotifier,
    redirect: (context, state) async {
      final authAsync = ref.read(authControllerProvider);
      final authState =
          authAsync.asData?.value ??
          await ref.read(authControllerProvider.future) ??
          const AuthControllerState();
      final isAuthenticated = authState.isAuthenticated;
      final isAuthRoute = _authRoutes.contains(state.matchedLocation);

      if (!isAuthenticated && !isAuthRoute) {
        // Redirect unauthenticated users to login
        return AppRoutes.login;
      }

      if (isAuthenticated && isAuthRoute) {
        return resolveAuthenticatedHomeRoute(authState.session);
      }

      if (isAuthenticated &&
          state.matchedLocation == AppRoutes.dashboard &&
          !canViewDashboard(authState.session)) {
        return AppRoutes.salesHistory;
      }

      if (_requiresDiscountAccess(state.matchedLocation) &&
          !canManageDiscounts(authState.session)) {
        return AppRoutes.salesHistory;
      }

      return null;
    },
    routes: [
      GoRoute(
        path: AppRoutes.login,
        builder: (context, state) => const LoginPage(),
      ),
      GoRoute(
        path: AppRoutes.forgotPassword,
        builder: (context, state) => _buildPlaceholder(
          context,
          title: AppLocalizations.of(context)!.authForgotPassword,
        ),
      ),
      GoRoute(
        path: AppRoutes.register,
        builder: (context, state) => _buildPlaceholder(
          context,
          title: AppLocalizations.of(context)!.authRegister,
        ),
      ),
      ShellRoute(
        builder: (context, state, child) => MobileAppShell(child: child),
        routes: [
          GoRoute(
            path: AppRoutes.root,
            redirect: (context, state) {
              final authAsync = ref.read(authControllerProvider);
              final session = authAsync.asData?.value.session;
              return resolveAuthenticatedHomeRoute(session);
            },
          ),
          GoRoute(
            path: AppRoutes.dashboard,
            builder: (context, state) => const DashboardPage(),
          ),
          GoRoute(
            path: AppRoutes.inventory,
            builder: (context, state) => const ItemsPage(),
          ),
          GoRoute(
            path: AppRoutes.inventoryBatch,
            builder: (context, state) => const AddInventoryPage(),
          ),
          GoRoute(
            path: AppRoutes.inventoryBatches,
            builder: (context, state) => const InventoryBatchesPage(),
          ),
          GoRoute(
            path: AppRoutes.inventoryAdjustments,
            builder: (context, state) => const AdjustmentHistoryPage(),
          ),
          GoRoute(
            path: AppRoutes.salesNew,
            builder: (context, state) => _buildPlaceholder(
              context,
              title: AppLocalizations.of(context)!.shellNewSale,
            ),
          ),
          GoRoute(
            path: AppRoutes.salesHistory,
            builder: (context, state) => const SalesHistoryPage(),
          ),
          GoRoute(
            path: AppRoutes.profitLoss,
            builder: (context, state) => _buildPlaceholder(
              context,
              title: AppLocalizations.of(context)!.shellProfitLossReport,
            ),
          ),
          GoRoute(
            path: AppRoutes.customers,
            builder: (context, state) => const CustomersPage(),
          ),
          GoRoute(
            path: AppRoutes.suppliers,
            builder: (context, state) => const SuppliersPage(),
          ),
          GoRoute(
            path: AppRoutes.createShop,
            builder: (context, state) => const CreateShopPage(),
          ),
          GoRoute(
            path: AppRoutes.manageShop,
            builder: (context, state) => const ManageShopPage(),
          ),
          GoRoute(
            path: AppRoutes.expenses,
            builder: (context, state) => _buildPlaceholder(
              context,
              title: AppLocalizations.of(context)!.shellManageExpenses,
            ),
          ),
          GoRoute(
            path: AppRoutes.users,
            builder: (context, state) => const UsersPage(),
          ),
          GoRoute(
            path: AppRoutes.discounts,
            builder: (context, state) => const DiscountsPage(),
          ),
          GoRoute(
            path: AppRoutes.bankAccounts,
            builder: (context, state) => _buildPlaceholder(
              context,
              title: AppLocalizations.of(context)!.shellManageBankAccounts,
            ),
          ),
          GoRoute(
            path: AppRoutes.language,
            builder: (context, state) => const LanguagePage(),
          ),
          GoRoute(
            path: AppRoutes.profile,
            builder: (context, state) => const ProfileSettingsPage(),
          ),
          GoRoute(
            path: AppRoutes.profileEdit,
            builder: (context, state) => const UpdateProfilePage(),
          ),
          GoRoute(
            path: AppRoutes.profileChangePassword,
            builder: (context, state) => const ChangePasswordPage(),
          ),
          GoRoute(
            path: AppRoutes.placeholders,
            builder: (context, state) {
              final l10n = AppLocalizations.of(context)!;
              final details = state.extra as PlaceholderPageDetails?;

              return PlaceholderPage(
                title: details?.title ?? l10n.notFoundTitle,
                body: details?.body ?? l10n.placeholderBody,
              );
            },
          ),
        ],
      ),
    ],
  );
});

const Set<String> _authRoutes = {
  AppRoutes.login,
  AppRoutes.forgotPassword,
  AppRoutes.register,
};

bool _requiresDiscountAccess(String location) {
  return location == AppRoutes.discounts;
}

PlaceholderPage _buildPlaceholder(
  BuildContext context, {
  required String title,
}) {
  final l10n = AppLocalizations.of(context)!;
  return PlaceholderPage(title: title, body: l10n.placeholderBody);
}
