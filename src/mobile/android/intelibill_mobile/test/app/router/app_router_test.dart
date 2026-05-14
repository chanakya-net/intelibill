import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';

import 'package:intelibill_mobile/src/app/router/app_router.dart';

void main() {
  group('AppRouter', () {
    test('goRouterProvider returns a GoRouter instance', () {
      final container = ProviderContainer();
      final router = container.read(goRouterProvider);
      expect(router, isA<GoRouter>());
    });

    test('AppRoutes constants are defined correctly', () {
      expect(AppRoutes.root, equals('/'));
      expect(AppRoutes.login, equals('/login'));
      expect(AppRoutes.forgotPassword, equals('/forgot-password'));
      expect(AppRoutes.register, equals('/register'));
      expect(AppRoutes.dashboard, equals('/dashboard'));
      expect(AppRoutes.inventory, equals('/inventory'));
      expect(AppRoutes.inventoryBatch, equals('/inventory/batch'));
      expect(AppRoutes.inventoryBatches, equals('/inventory/batches'));
      expect(AppRoutes.inventoryAdjustments, equals('/inventory/adjustments'));
      expect(AppRoutes.salesNew, equals('/sales/new'));
      expect(AppRoutes.salesHistory, equals('/sales/history'));
      expect(AppRoutes.profitLoss, equals('/sales/profit-loss'));
      expect(AppRoutes.customers, equals('/customers'));
      expect(AppRoutes.suppliers, equals('/suppliers'));
      expect(AppRoutes.expenses, equals('/expenses'));
      expect(AppRoutes.users, equals('/users'));
      expect(AppRoutes.discounts, equals('/discounts'));
      expect(AppRoutes.bankAccounts, equals('/bank-accounts'));
      expect(AppRoutes.language, equals('/language'));
      expect(AppRoutes.placeholders, equals('/placeholder'));
    });

    test('router includes authenticated shell', () {
      final container = ProviderContainer();
      final router = container.read(goRouterProvider);

      expect(
        router.configuration.routes.any((route) => route is ShellRoute),
        isTrue,
      );
    });
  });
}
