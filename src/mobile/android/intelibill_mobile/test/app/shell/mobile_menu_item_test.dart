import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/shell/mobile_menu_item.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';

AuthSession _sessionForRole(
  String role, {
  String? activeShopId,
  List<UserShop>? shops,
}) {
  return AuthSession(
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    accessTokenExpiresAt: DateTime.utc(2026, 5, 15, 10),
    refreshTokenExpiresAt: DateTime.utc(2026, 6, 15, 10),
    user: const AuthUser(
      id: 'user-1',
      email: 'owner@example.com',
      phoneNumber: null,
      firstName: 'Alex',
      lastName: 'Smith',
      language: 'en-IN',
    ),
    activeShopId: activeShopId,
    shops:
        shops ??
        [
          UserShop(
            shopId: 'shop-1',
            shopName: 'Primary Shop',
            role: role,
            isDefault: true,
            lastUsedAt: DateTime.utc(2026, 5, 12, 10),
          ),
        ],
    rememberMe: false,
  );
}

void main() {
  group('Mobile menu catalog', () {
    test('owner sees all primary destinations', () {
      final session = _sessionForRole('Owner');

      final keys = primaryNavigationItems(
        session,
      ).map((item) => item.labelKey).toList();

      expect(
        keys,
        equals([
          MobileMenuLabelKey.dashboard,
          MobileMenuLabelKey.inventory,
          MobileMenuLabelKey.sales,
          MobileMenuLabelKey.customers,
          MobileMenuLabelKey.more,
        ]),
      );
    });

    test('staff sees permitted primary navigation only', () {
      final session = _sessionForRole('Staff');

      final keys = primaryNavigationItems(
        session,
      ).map((item) => item.labelKey).toList();

      expect(
        keys,
        equals([
          MobileMenuLabelKey.inventory,
          MobileMenuLabelKey.sales,
          MobileMenuLabelKey.more,
        ]),
      );
    });

    test('owner sees management items in more menu', () {
      final session = _sessionForRole('Owner');

      final keys = moreMenuItems(session).map((item) => item.labelKey).toList();

      expect(keys, contains(MobileMenuLabelKey.services));
      expect(keys, contains(MobileMenuLabelKey.suppliers));
      expect(keys, contains(MobileMenuLabelKey.expenses));
      expect(keys, contains(MobileMenuLabelKey.users));
      expect(keys, contains(MobileMenuLabelKey.bankAccounts));
      expect(keys, contains(MobileMenuLabelKey.discounts));
      expect(keys, contains(MobileMenuLabelKey.profile));
      expect(keys, contains(MobileMenuLabelKey.changePassword));
      expect(keys, contains(MobileMenuLabelKey.addShop));
      expect(keys, contains(MobileMenuLabelKey.manageShop));
      expect(keys, contains(MobileMenuLabelKey.language));
      expect(keys, contains(MobileMenuLabelKey.logout));
    });

    test('staff sees limited more menu options', () {
      final session = _sessionForRole('Staff');

      final keys = moreMenuItems(session).map((item) => item.labelKey).toList();

      expect(keys, contains(MobileMenuLabelKey.users));
      expect(keys, contains(MobileMenuLabelKey.profile));
      expect(keys, contains(MobileMenuLabelKey.changePassword));
      expect(keys, contains(MobileMenuLabelKey.language));
      expect(keys, contains(MobileMenuLabelKey.logout));
      expect(keys, isNot(contains(MobileMenuLabelKey.services)));
      expect(keys, isNot(contains(MobileMenuLabelKey.suppliers)));
      expect(keys, isNot(contains(MobileMenuLabelKey.expenses)));
      expect(keys, isNot(contains(MobileMenuLabelKey.bankAccounts)));
      expect(keys, isNot(contains(MobileMenuLabelKey.discounts)));
      expect(keys, isNot(contains(MobileMenuLabelKey.addShop)));
      expect(keys, isNot(contains(MobileMenuLabelKey.manageShop)));
    });

    test('primary items expose selected icon variants', () {
      final session = _sessionForRole('Owner');
      final items = primaryNavigationItems(session);

      final dashboard = items.firstWhere(
        (item) => item.labelKey == MobileMenuLabelKey.dashboard,
      );
      final inventory = items.firstWhere(
        (item) => item.labelKey == MobileMenuLabelKey.inventory,
      );

      expect(dashboard.selectedIcon, Icons.home);
      expect(inventory.selectedIcon, Icons.inventory_2);
    });

    test('logout item is marked destructive', () {
      final session = _sessionForRole('Owner');
      final logoutItem = moreMenuItems(session).firstWhere(
        (item) => item.labelKey == MobileMenuLabelKey.logout,
      );

      expect(logoutItem.isDestructive, isTrue);
    });

    test('language and logout destinations are wired', () {
      final session = _sessionForRole('Owner');
      final items = moreMenuItems(session);

      final languageItem = items.firstWhere(
        (item) => item.labelKey == MobileMenuLabelKey.language,
      );
      final logoutItem = items.firstWhere(
        (item) => item.labelKey == MobileMenuLabelKey.logout,
      );

      expect(languageItem.destination, isA<MobileMenuRoute>());
      final languageDestination = languageItem.destination as MobileMenuRoute;
      expect(languageDestination.route, AppRoutes.language);

      expect(logoutItem.destination, isA<MobileMenuAction>());
      final logoutDestination = logoutItem.destination as MobileMenuAction;
      expect(logoutDestination.type, MobileMenuActionType.logout);
    });
  });
}
