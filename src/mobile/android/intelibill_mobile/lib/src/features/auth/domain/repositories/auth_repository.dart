import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';

abstract interface class AuthRepository {
  Future<AuthSession> login({
    required String identifier,
    required String password,
    required bool rememberMe,
  });
  Future<AuthSession> updateProfile({
    required String email,
    String? phoneNumber,
    required String firstName,
    required String lastName,
    required String language,
  });

  Future<void> changePassword({
    required String currentPassword,
    required String newPassword,
  });

  Future<AuthSession> switchShop({required String shopId});
  Future<AuthSession> refreshToken({required String refreshToken});

  Future<void> revokeToken({required String refreshToken});

  Future<void> clearTokens();

  Future<String?> getAccessToken();

  Future<String?> getRefreshToken();

  Future<void> saveRememberedIdentifier({required String identifier});

  Future<String?> getRememberedIdentifier();

  Future<void> clearRememberedIdentifier();
}
