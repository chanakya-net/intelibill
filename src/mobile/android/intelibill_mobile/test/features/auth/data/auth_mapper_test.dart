import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/auth/data/dto/auth_result_dto.dart';
import 'package:intelibill_mobile/src/features/auth/data/dto/auth_user_dto.dart';
import 'package:intelibill_mobile/src/features/auth/data/dto/user_shop_dto.dart';
import 'package:intelibill_mobile/src/features/auth/data/mappers/auth_mapper.dart';

void main() {
  group('AuthMapper', () {
    test('maps dto to domain entity correctly', () {
      final dto = AuthResultDto(
        accessToken: 'access_123',
        refreshToken: 'refresh_123',
        accessTokenExpiresAt: DateTime.utc(2026, 5, 15, 10),
        refreshTokenExpiresAt: DateTime.utc(2026, 6, 14, 10),
        user: const AuthUserDto(
          id: 'user-1',
          email: 'test@example.com',
          phoneNumber: '+1234567890',
          firstName: 'John',
          lastName: 'Doe',
          language: 'en-IN',
        ),
        activeShopId: 'shop-1',
        shops: const [
          UserShopDto(
            shopId: 'shop-1',
            shopName: 'Main Store',
            role: 'Owner',
            isDefault: true,
          ),
        ],
      );

      final session = AuthMapper.toDomain(dto, rememberMe: true);

      expect(session.accessToken, 'access_123');
      expect(session.refreshToken, 'refresh_123');
      expect(session.accessTokenExpiresAt, DateTime.utc(2026, 5, 15, 10));
      expect(session.refreshTokenExpiresAt, DateTime.utc(2026, 6, 14, 10));
      expect(session.user.id, 'user-1');
      expect(session.user.email, 'test@example.com');
      expect(session.user.firstName, 'John');
      expect(session.user.lastName, 'Doe');
      expect(session.activeShopId, 'shop-1');
      expect(session.rememberMe, true);
      expect(session.shops, isNotNull);
      expect(session.shops!.length, 1);
    });

    test('sets default language when not provided', () {
      final dto = AuthResultDto(
        accessToken: 'token',
        refreshToken: 'refresh',
        accessTokenExpiresAt: DateTime.utc(2026, 5, 15, 10),
        refreshTokenExpiresAt: DateTime.utc(2026, 6, 14, 10),
        user: const AuthUserDto(
          id: 'user-1',
          firstName: 'John',
          lastName: 'Doe',
        ),
      );

      final session = AuthMapper.toDomain(dto);

      expect(session.user.language, 'en-IN');
    });

    test('respects rememberMe flag', () {
      final dto = AuthResultDto(
        accessToken: 'token',
        refreshToken: 'refresh',
        accessTokenExpiresAt: DateTime.utc(2026, 5, 15, 10),
        refreshTokenExpiresAt: DateTime.utc(2026, 6, 14, 10),
        user: const AuthUserDto(
          id: 'user-1',
          firstName: 'John',
          lastName: 'Doe',
        ),
      );

      final sessionRemembered = AuthMapper.toDomain(dto, rememberMe: true);
      final sessionForgotten = AuthMapper.toDomain(dto);

      expect(sessionRemembered.rememberMe, true);
      expect(sessionForgotten.rememberMe, false);
    });
  });
}
