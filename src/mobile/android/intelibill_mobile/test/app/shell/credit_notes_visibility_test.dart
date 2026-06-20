import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/shell/menu_visibility.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';

AuthSession _session(String role) {
  return AuthSession(
    accessToken: 'a',
    refreshToken: 'r',
    accessTokenExpiresAt: DateTime.utc(2026, 1, 1),
    refreshTokenExpiresAt: DateTime.utc(2026, 1, 2),
    user: const AuthUser(
      id: 'u',
      email: 'x@y.z',
      phoneNumber: null,
      firstName: 'A',
      lastName: 'B',
      language: 'en-IN',
    ),
    activeShopId: 'shop-1',
    shops: [
      UserShop(
        shopId: 'shop-1',
        shopName: 'Shop',
        role: role,
        isDefault: true,
        lastUsedAt: null,
      ),
    ],
    rememberMe: false,
  );
}

void main() {
  test('owner and manager can manage credit notes', () {
    expect(canManageCreditNotes(_session('Owner')), isTrue);
    expect(canManageCreditNotes(_session('Manager')), isTrue);
  });

  test('staff cannot manage credit notes', () {
    expect(canManageCreditNotes(_session('Staff')), isFalse);
  });

  test('owner manager staff can view credit notes', () {
    expect(canViewCreditNotes(_session('Owner')), isTrue);
    expect(canViewCreditNotes(_session('Manager')), isTrue);
    expect(canViewCreditNotes(_session('Staff')), isTrue);
  });

  test('null session cannot view credit notes', () {
    expect(canViewCreditNotes(null), isFalse);
  });

  test('other roles cannot view credit notes', () {
    expect(canViewCreditNotes(_session('Accountant')), isFalse);
    expect(canViewCreditNotes(_session('Viewer')), isFalse);
  });
}
