import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/suppliers/data/dto/create_supplier_request_dto.dart';

void main() {
  group('CreateSupplierRequestDto', () {
    test('serializes to JSON with expected keys', () {
      const dto = CreateSupplierRequestDto(
        name: 'ABC Traders',
        contactPersonName: 'John Doe',
        contactPersonPhone: '+919876543210',
        address: '12 Main Street',
        city: 'Mumbai',
        state: 'Maharashtra',
        pin: '400001',
        isActive: true,
        isPreferred: false,
      );

      expect(dto.toJson(), {
        'name': 'ABC Traders',
        'contactPersonName': 'John Doe',
        'contactPersonPhone': '+919876543210',
        'address': '12 Main Street',
        'city': 'Mumbai',
        'state': 'Maharashtra',
        'pin': '400001',
        'isActive': true,
        'isPreferred': false,
      });
    });

    test('serializes null optional contact fields when missing', () {
      const dto = CreateSupplierRequestDto(
        name: 'XYZ Suppliers',
        address: '22 Market Road',
        city: 'Pune',
        state: 'Maharashtra',
        pin: '411001',
        isActive: false,
        isPreferred: true,
      );

      expect(dto.toJson(), {
        'name': 'XYZ Suppliers',
        'contactPersonName': null,
        'contactPersonPhone': null,
        'address': '22 Market Road',
        'city': 'Pune',
        'state': 'Maharashtra',
        'pin': '411001',
        'isActive': false,
        'isPreferred': true,
      });
    });
  });
}
