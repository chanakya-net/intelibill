import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/navigation/authenticated_home_route.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';

void main() {
  group('resolveAuthenticatedHomeRoute', () {
    test('returns dashboard for owners and managers', () {
      expect(
        resolveAuthenticatedHomeRoute(_sessionForRole('Owner')),
        AppRoutes.dashboard,
      );
      expect(
        resolveAuthenticatedHomeRoute(_sessionForRole('Manager')),
        AppRoutes.dashboard,
      );
    });

    test('returns sales for staff', () {
      expect(
        resolveAuthenticatedHomeRoute(_sessionForRole('Staff')),
        AppRoutes.salesHistory,
      );
    });
  });
}

AuthSession _sessionForRole(String role) {
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
    activeShopId: 'shop-1',
    shops: [
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
