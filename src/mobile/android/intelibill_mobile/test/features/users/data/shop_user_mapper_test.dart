import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/users/data/dto/shop_user_dto.dart';
import 'package:intelibill_mobile/src/features/users/data/mappers/shop_user_mapper.dart';

void main() {
  group('ShopUserMapper', () {
    test('maps DTO to domain entity', () {
      const dto = ShopUserDto(
        userId: 'user-1',
        firstName: 'Alice',
        lastName: 'Sharma',
        email: 'alice@example.com',
        phoneNumber: '9876543210',
        role: 'Manager',
        isLoginEnabled: true,
        shopIds: ['shop-1'],
      );

      final user = ShopUserMapper.toDomain(dto);

      expect(user.userId, 'user-1');
      expect(user.fullName, 'Alice Sharma');
      expect(user.isOwner, false);
      expect(user.shopIds, ['shop-1']);
    });

    test('identifies owner role', () {
      const dto = ShopUserDto(
        userId: 'user-2',
        firstName: 'Bob',
        lastName: 'Kumar',
        role: 'Owner',
        isLoginEnabled: true,
      );

      final user = ShopUserMapper.toDomain(dto);

      expect(user.isOwner, true);
    });
  });
}
