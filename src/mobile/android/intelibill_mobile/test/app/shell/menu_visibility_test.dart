import 'package:flutter_test/flutter_test.dart';

import 'package:intelibill_mobile/src/app/shell/menu_visibility.dart';
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
  group('menu visibility helpers', () {
    test('owner role grants owner-only permissions', () {
      final session = _sessionForRole('Owner');

      expect(isOwner(session), isTrue);
      expect(isOwnerOrManager(session), isTrue);
      expect(canManageSuppliers(session), isTrue);
      expect(canManageCustomers(session), isTrue);
      expect(canManageSales(session), isTrue);
      expect(canManageExpenses(session), isTrue);
      expect(canManageDiscounts(session), isTrue);
      expect(canManageBankAccounts(session), isTrue);
    });

    test('manager role grants shared permissions', () {
      final session = _sessionForRole('Manager');

      expect(isOwner(session), isFalse);
      expect(isOwnerOrManager(session), isTrue);
      expect(canManageSuppliers(session), isFalse);
      expect(canManageCustomers(session), isTrue);
      expect(canManageSales(session), isTrue);
      expect(canManageExpenses(session), isTrue);
      expect(canManageDiscounts(session), isTrue);
      expect(canManageBankAccounts(session), isFalse);
    });

    test('staff role grants sales-only permissions', () {
      final session = _sessionForRole('Staff');

      expect(isOwner(session), isFalse);
      expect(isOwnerOrManager(session), isFalse);
      expect(canManageSuppliers(session), isFalse);
      expect(canManageCustomers(session), isFalse);
      expect(canManageSales(session), isTrue);
      expect(canManageExpenses(session), isFalse);
      expect(canManageDiscounts(session), isFalse);
      expect(canManageBankAccounts(session), isFalse);
    });

    test('falls back to default shop when active shop is missing', () {
      final session = _sessionForRole(
        'Owner',
        shops: [
          UserShop(
            shopId: 'shop-1',
            shopName: 'Default Shop',
            role: 'Manager',
            isDefault: true,
            lastUsedAt: DateTime.utc(2026, 5, 12, 10),
          ),
          UserShop(
            shopId: 'shop-2',
            shopName: 'Secondary Shop',
            role: 'Owner',
            isDefault: false,
            lastUsedAt: DateTime.utc(2026, 5, 10, 10),
          ),
        ],
      );

      expect(isOwner(session), isFalse);
      expect(canManageCustomers(session), isTrue);
    });

    test('returns false when no shops are present', () {
      final session = _sessionForRole('Owner', shops: []);

      expect(isOwner(session), isFalse);
      expect(isOwnerOrManager(session), isFalse);
      expect(canManageSales(session), isFalse);
    });
  });
}
