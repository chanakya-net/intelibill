import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/customers/data/dto/create_customer_request_dto.dart';

void main() {
  group('CreateCustomerRequestDto', () {
    test('serializes to JSON with expected keys', () {
      const dto = CreateCustomerRequestDto(
        name: 'Alice Sharma',
        phoneNumber: '+919876543210',
        address: '12 Main Street',
        isActive: true,
      );

      expect(dto.toJson(), {
        'name': 'Alice Sharma',
        'phoneNumber': '+919876543210',
        'address': '12 Main Street',
        'isActive': true,
      });
    });

    test('serializes null address when missing', () {
      const dto = CreateCustomerRequestDto(
        name: 'Bob Kumar',
        phoneNumber: '9876543210',
        isActive: false,
      );

      expect(dto.toJson(), {
        'name': 'Bob Kumar',
        'phoneNumber': '9876543210',
        'address': null,
        'isActive': false,
      });
    });
  });
}
