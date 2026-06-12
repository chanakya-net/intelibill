import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/users/data/dto/shop_user_dto.dart';

void main() {
  group('ShopUserDto', () {
    test('parses full JSON with all fields present', () {
      final json = {
        'userId': 'user-123',
        'firstName': 'Alice',
        'lastName': 'Sharma',
        'email': 'alice@example.com',
        'phoneNumber': '9876543210',
        'role': 'Manager',
        'isLoginEnabled': true,
        'shopIds': ['shop-1', 'shop-2'],
      };

      final dto = ShopUserDto.fromJson(json);

      expect(dto.userId, 'user-123');
      expect(dto.firstName, 'Alice');
      expect(dto.lastName, 'Sharma');
      expect(dto.email, 'alice@example.com');
      expect(dto.phoneNumber, '9876543210');
      expect(dto.role, 'Manager');
      expect(dto.isLoginEnabled, true);
      expect(dto.shopIds, ['shop-1', 'shop-2']);
    });

    test('parses JSON with nullable email and phone missing', () {
      final json = {
        'userId': 'user-456',
        'firstName': 'Bob',
        'lastName': 'Kumar',
        'role': 'Owner',
        'isLoginEnabled': false,
      };

      final dto = ShopUserDto.fromJson(json);

      expect(dto.email, isNull);
      expect(dto.phoneNumber, isNull);
      expect(dto.isLoginEnabled, false);
      expect(dto.shopIds, isEmpty);
    });

    test('supports value equality via Freezed', () {
      const dto1 = ShopUserDto(
        userId: 'user-1',
        firstName: 'Alice',
        lastName: 'Sharma',
        role: 'Manager',
        isLoginEnabled: true,
      );
      const dto2 = ShopUserDto(
        userId: 'user-1',
        firstName: 'Alice',
        lastName: 'Sharma',
        role: 'Manager',
        isLoginEnabled: true,
      );

      expect(dto1, equals(dto2));
    });
  });
}
