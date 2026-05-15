import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/shops/data/dtos/shop_details_dto.dart';
import 'package:intelibill_mobile/src/features/shops/data/mappers/shop_mapper.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/add_bank_account_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/create_shop_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/shop_details.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/update_shop_request.dart';

void main() {
  group('ShopMapper', () {
    test('maps ShopDetailsDto to ShopDetails domain entity', () {
      const dto = ShopDetailsDto(
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

      final domain = ShopMapper.toDomain(dto);

      expect(domain, isA<ShopDetails>());
      expect(domain.id, 'shop-1');
      expect(domain.name, 'My Shop');
      expect(domain.contactPerson, 'John');
      expect(domain.bankAccounts, hasLength(1));
      expect(domain.bankAccounts.first, isA<BankAccount>());
      expect(domain.bankAccounts.first.bankName, 'HDFC');
      expect(domain.bankAccounts.first.accountNumber, '123456');
    });

    test('maps CreateShopRequest to CreateShopRequestDto', () {
      const req = CreateShopRequest(
        name: 'My Shop',
        address: 'Addr',
        city: 'City',
        state: 'State',
        pincode: '000000',
        contactPerson: 'John',
        mobileNumber: '999',
        gstNumber: 'GST',
      );

      final dto = ShopMapper.toCreateDto(req);

      expect(dto.name, 'My Shop');
      expect(dto.contactPerson, 'John');
      expect(dto.gstNumber, 'GST');
    });

    test('maps UpdateShopRequest to UpdateShopRequestDto', () {
      const req = UpdateShopRequest(
        name: 'My Shop',
        address: 'Addr',
        city: 'City',
        state: 'State',
        pincode: '000000',
        contactPerson: 'John',
        mobileNumber: '999',
        gstNumber: 'GST',
      );

      final dto = ShopMapper.toUpdateDto(req);

      expect(dto.name, 'My Shop');
      expect(dto.mobileNumber, '999');
    });

    test('maps AddBankAccountRequest to AddBankAccountRequestDto', () {
      const req = AddBankAccountRequest(
        bankName: 'HDFC',
        accountNumber: '123',
        accountType: 'Savings',
        ifscCode: 'HDFC0001',
        accountHolderName: 'John Doe',
      );

      final dto = ShopMapper.toAddBankAccountDto(req);

      expect(dto.bankName, 'HDFC');
      expect(dto.accountNumber, '123');
      expect(dto.ifscCode, 'HDFC0001');
    });
  });
}
