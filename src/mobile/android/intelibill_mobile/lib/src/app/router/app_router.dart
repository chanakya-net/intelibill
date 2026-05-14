import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'package:intelibill_mobile/src/app/pages/language_page.dart';
import 'package:intelibill_mobile/src/app/pages/placeholder_page.dart';
import 'package:intelibill_mobile/src/app/shell/mobile_app_shell.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/pages/change_password_page.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/pages/login_page.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/pages/profile_settings_page.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/pages/update_profile_page.dart';
import 'package:intelibill_mobile/src/features/customers/presentation/pages/customers_page.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/pages/suppliers_page.dart';

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
        // Redirect authenticated users away from login to home
        return AppRoutes.dashboard;
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
            redirect: (context, state) => AppRoutes.dashboard,
          ),
          GoRoute(
            path: AppRoutes.dashboard,
            builder: (context, state) => _buildPlaceholder(
              context,
              title: AppLocalizations.of(context)!.shellDashboard,
            ),
          ),
          GoRoute(
            path: AppRoutes.inventory,
            builder: (context, state) => _buildPlaceholder(
              context,
              title: AppLocalizations.of(context)!.shellManageInventory,
            ),
          ),
          GoRoute(
            path: AppRoutes.inventoryBatch,
            builder: (context, state) => _buildPlaceholder(
              context,
              title: AppLocalizations.of(context)!.shellBatchInventoryInbound,
            ),
          ),
          GoRoute(
            path: AppRoutes.inventoryBatches,
            builder: (context, state) => _buildPlaceholder(
              context,
              title: AppLocalizations.of(
                context,
              )!.shellInventoryBatchesOverview,
            ),
          ),
          GoRoute(
            path: AppRoutes.inventoryAdjustments,
            builder: (context, state) => _buildPlaceholder(
              context,
              title: AppLocalizations.of(context)!.shellInventoryAdjustments,
            ),
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
            builder: (context, state) => _buildPlaceholder(
              context,
              title: AppLocalizations.of(context)!.shellSalesHistory,
            ),
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
            path: AppRoutes.expenses,
            builder: (context, state) => _buildPlaceholder(
              context,
              title: AppLocalizations.of(context)!.shellManageExpenses,
            ),
          ),
          GoRoute(
            path: AppRoutes.users,
            builder: (context, state) => _buildPlaceholder(
              context,
              title: AppLocalizations.of(context)!.shellManageUsers,
            ),
          ),
          GoRoute(
            path: AppRoutes.discounts,
            builder: (context, state) => _buildPlaceholder(
              context,
              title: AppLocalizations.of(context)!.shellManageDiscounts,
            ),
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

PlaceholderPage _buildPlaceholder(
  BuildContext context, {
  required String title,
}) {
  final l10n = AppLocalizations.of(context)!;
  return PlaceholderPage(
    title: title,
    body: l10n.placeholderBody,
  );
}
