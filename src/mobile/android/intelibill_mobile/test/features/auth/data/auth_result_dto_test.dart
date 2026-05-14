import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/auth/data/dto/auth_result_dto.dart';
import 'package:intelibill_mobile/src/features/auth/data/dto/auth_user_dto.dart';
import 'package:intelibill_mobile/src/features/auth/data/dto/user_shop_dto.dart';
import 'package:intelibill_mobile/src/features/auth/data/mappers/auth_mapper.dart';

void main() {
  group('AuthResultDto', () {
    test('parses auth response json correctly', () {
      final json = <String, dynamic>{
        'accessToken': 'access_token_123',
        'refreshToken': 'refresh_token_123',
        'accessTokenExpiresAt': '2026-05-15T10:00:00Z',
        'refreshTokenExpiresAt': '2026-06-14T10:00:00Z',
        'user': <String, dynamic>{
          'id': 'user-1',
          'email': 'test@example.com',
          'phoneNumber': '+1234567890',
          'firstName': 'John',
          'lastName': 'Doe',
          'language': 'en-IN',
        },
        'activeShopId': 'shop-1',
        'shops': <Map<String, dynamic>>[
          <String, dynamic>{
            'shopId': 'shop-1',
            'shopName': 'Main Store',
            'role': 'Owner',
            'isDefault': true,
            'lastUsedAt': '2026-05-14T10:00:00Z',
          },
        ],
      };

      final dto = AuthResultDto.fromJson(json);

      expect(dto.accessToken, 'access_token_123');
      expect(dto.refreshToken, 'refresh_token_123');
      expect(dto.accessTokenExpiresAt, DateTime.utc(2026, 5, 15, 10));
      expect(dto.refreshTokenExpiresAt, DateTime.utc(2026, 6, 14, 10));
      expect(dto.user.id, 'user-1');
      expect(dto.user.email, 'test@example.com');
      expect(dto.user.firstName, 'John');
      expect(dto.user.lastName, 'Doe');
      expect(dto.activeShopId, 'shop-1');
      expect(dto.shops, isNotNull);
      expect(dto.shops!.length, 1);
      expect(dto.shops![0].shopId, 'shop-1');

      final domain = AuthMapper.toDomain(dto);
      expect(domain.accessToken, dto.accessToken);
    });

    test('serializes back to json correctly', () {
      final dto = AuthResultDto(
        accessToken: 'access_token_123',
        refreshToken: 'refresh_token_123',
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

      final json = dto.toJson();

      expect(json['accessToken'], 'access_token_123');
      expect(json['refreshToken'], 'refresh_token_123');
      expect(json['shops'], isNotNull);
      expect(json['shops'] is List, true);
    });

    test('handles null optional fields', () {
      final json = <String, dynamic>{
        'accessToken': 'token',
        'refreshToken': 'refresh',
        'accessTokenExpiresAt': '2026-05-15T10:00:00Z',
        'refreshTokenExpiresAt': '2026-06-14T10:00:00Z',
        'user': <String, dynamic>{
          'id': 'user-1',
          'firstName': 'John',
          'lastName': 'Doe',
        },
      };

      final dto = AuthResultDto.fromJson(json);

      expect(dto.activeShopId, isNull);
      expect(dto.shops, isNull);
      expect(dto.user.email, isNull);
      expect(dto.user.phoneNumber, isNull);
    });
  });
}
