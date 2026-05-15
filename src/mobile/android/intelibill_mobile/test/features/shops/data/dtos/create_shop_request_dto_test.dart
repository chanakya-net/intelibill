import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/shops/data/dtos/create_shop_request_dto.dart';

void main() {
  group('CreateShopRequestDto', () {
    test('serializes to JSON with expected keys', () {
      const dto = CreateShopRequestDto(
        name: 'My Shop',
        address: '12 Main Street',
        city: 'Mumbai',
        state: 'Maharashtra',
        pincode: '400001',
        contactPerson: 'John Doe',
        mobileNumber: '9999999999',
        gstNumber: 'GST123',
      );

      expect(dto.toJson(), {
        'name': 'My Shop',
        'address': '12 Main Street',
        'city': 'Mumbai',
        'state': 'Maharashtra',
        'pincode': '400001',
        'contactPerson': 'John Doe',
        'mobileNumber': '9999999999',
        'gstNumber': 'GST123',
      });
    });

    test('serializes null optional fields when missing', () {
      const dto = CreateShopRequestDto(
        name: 'Minimal Shop',
        address: 'Addr',
        city: 'City',
        state: 'State',
        pincode: '000000',
      );

      expect(dto.toJson(), {
        'name': 'Minimal Shop',
        'address': 'Addr',
        'city': 'City',
        'state': 'State',
        'pincode': '000000',
        'contactPerson': null,
        'mobileNumber': null,
        'gstNumber': null,
      });
    });
  });
}
