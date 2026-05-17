import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/shops/data/dtos/shop_details_dto.dart';

void main() {
  group('ShopDetailsDto', () {
    test('serializes and deserializes with primary bank account fields', () {
      const original = ShopDetailsDto(
        shopId: 'shop-1',
        name: 'My Shop',
        address: '12 Main Street',
        city: 'Mumbai',
        state: 'Maharashtra',
        pincode: '400001',
        contactPerson: 'John',
        mobileNumber: '9999999999',
        gstNumber: 'GST123',
        bankName: 'HDFC',
        bankAccountNumber: '123456',
        bankAccountType: 'Savings',
        ifscCode: 'HDFC0001',
        accountHolderName: 'John Doe',
      );

      final json = original.toJson();
      final deserialized = ShopDetailsDto.fromJson(json);

      expect(deserialized, original);
      expect(deserialized.bankName, 'HDFC');
      expect(deserialized.bankAccountNumber, '123456');
    });

    test('bank fields are nullable when missing from JSON', () {
      final json = {
        'shopId': 'shop-2',
        'name': 'Shop 2',
        'address': 'Addr',
        'city': 'City',
        'state': 'State',
        'pincode': '000000',
      };

      final dto = ShopDetailsDto.fromJson(json);

      expect(dto.bankName, isNull);
      expect(dto.bankAccountNumber, isNull);
      expect(dto.bankAccountType, isNull);
      expect(dto.ifscCode, isNull);
      expect(dto.accountHolderName, isNull);
    });
  });
}
