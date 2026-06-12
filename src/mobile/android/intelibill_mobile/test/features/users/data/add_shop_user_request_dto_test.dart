import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/users/data/dto/add_shop_user_request_dto.dart';
import 'package:intelibill_mobile/src/features/users/data/dto/edit_shop_user_request_dto.dart';

void main() {
  group('AddShopUserRequestDto', () {
    test('serializes to JSON with expected keys', () {
      const dto = AddShopUserRequestDto(
        shopIds: ['shop-1'],
        email: 'alice@example.com',
        firstName: 'Alice',
        lastName: 'Sharma',
        phoneNumber: '+919876543210',
        password: 'password1',
        confirmPassword: 'password1',
        role: 'Manager',
      );

      expect(dto.toJson(), {
        'shopIds': ['shop-1'],
        'email': 'alice@example.com',
        'firstName': 'Alice',
        'lastName': 'Sharma',
        'phoneNumber': '+919876543210',
        'password': 'password1',
        'confirmPassword': 'password1',
        'role': 'Manager',
      });
    });
  });

  group('EditShopUserRequestDto', () {
    test('serializes to JSON with expected keys', () {
      const dto = EditShopUserRequestDto(
        email: 'alice@example.com',
        firstName: 'Alice',
        lastName: 'Sharma',
        phoneNumber: '9876543210',
        role: 'Staff',
        isLoginEnabled: false,
        shopIds: ['shop-1', 'shop-2'],
      );

      expect(dto.toJson(), {
        'email': 'alice@example.com',
        'firstName': 'Alice',
        'lastName': 'Sharma',
        'phoneNumber': '9876543210',
        'role': 'Staff',
        'isLoginEnabled': false,
        'shopIds': ['shop-1', 'shop-2'],
      });
    });
  });
}
