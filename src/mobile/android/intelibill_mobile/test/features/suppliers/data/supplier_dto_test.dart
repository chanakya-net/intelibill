import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/suppliers/data/dto/supplier_dto.dart';

void main() {
  group('SupplierDto', () {
    test('parses full JSON with all fields present', () {
      final json = {
        'supplierId': 'sup-123',
        'name': 'ABC Traders',
        'contactPersonName': 'John Doe',
        'contactPersonPhone': '9876543210',
        'address': '12 Main Street',
        'city': 'Mumbai',
        'state': 'Maharashtra',
        'pin': '400001',
        'isSystem': false,
        'isActive': true,
        'isPreferred': true,
        'balanceDue': 1500.50,
      };

      final dto = SupplierDto.fromJson(json);

      expect(dto.supplierId, 'sup-123');
      expect(dto.name, 'ABC Traders');
      expect(dto.contactPersonName, 'John Doe');
      expect(dto.contactPersonPhone, '9876543210');
      expect(dto.address, '12 Main Street');
      expect(dto.city, 'Mumbai');
      expect(dto.state, 'Maharashtra');
      expect(dto.pin, '400001');
      expect(dto.isSystem, false);
      expect(dto.isActive, true);
      expect(dto.isPreferred, true);
      expect(dto.balanceDue, 1500.50);
    });

    test('defaults balanceDue to 0.0 when field is missing', () {
      final json = {
        'supplierId': 'sup-789',
        'name': 'XYZ Suppliers',
        'isSystem': false,
        'isActive': true,
        'isPreferred': false,
      };

      final dto = SupplierDto.fromJson(json);

      expect(dto.balanceDue, 0.0);
    });

    test('handles null contact person fields', () {
      final json = {
        'supplierId': 'sup-456',
        'name': 'DEF Corp',
        'contactPersonName': null,
        'contactPersonPhone': null,
        'isSystem': false,
        'isActive': true,
        'isPreferred': false,
      };

      final dto = SupplierDto.fromJson(json);

      expect(dto.supplierId, 'sup-456');
      expect(dto.name, 'DEF Corp');
      expect(dto.contactPersonName, isNull);
      expect(dto.contactPersonPhone, isNull);
    });

    test('handles numeric balance with high precision', () {
      final json = {
        'supplierId': 'sup-999',
        'name': 'GHI Ltd',
        'isSystem': false,
        'isActive': true,
        'isPreferred': false,
        'balanceDue': 9999.99,
      };

      final dto = SupplierDto.fromJson(json);

      expect(dto.balanceDue, 9999.99);
    });

    test('handles zero balance due', () {
      final json = {
        'supplierId': 'sup-111',
        'name': 'JKL Industries',
        'isSystem': false,
        'isActive': true,
        'isPreferred': true,
        'balanceDue': 0.0,
      };

      final dto = SupplierDto.fromJson(json);

      expect(dto.balanceDue, 0.0);
    });

    test('preserves system supplier flag', () {
      final json = {
        'supplierId': 'sup-sys-1',
        'name': 'System Default',
        'isSystem': true,
        'isActive': true,
        'isPreferred': false,
      };

      final dto = SupplierDto.fromJson(json);

      expect(dto.isSystem, true);
    });

    test('handles all optional fields as null', () {
      final json = {
        'supplierId': 'sup-222',
        'name': 'Minimal Supplier',
        'isSystem': false,
        'isActive': true,
        'isPreferred': false,
      };

      final dto = SupplierDto.fromJson(json);

      expect(dto.contactPersonName, isNull);
      expect(dto.contactPersonPhone, isNull);
      expect(dto.address, isNull);
      expect(dto.city, isNull);
      expect(dto.state, isNull);
      expect(dto.pin, isNull);
    });

    test('serializes and deserializes correctly', () {
      const originalDto = SupplierDto(
        supplierId: 'sup-333',
        name: 'Test Supplier',
        contactPersonName: 'Jane Smith',
        contactPersonPhone: '9111111111',
        address: '22 Test Road',
        city: 'Delhi',
        state: 'Delhi',
        pin: '110001',
        isSystem: false,
        isActive: true,
        isPreferred: false,
        balanceDue: 2500,
      );

      final json = originalDto.toJson();
      final deserializedDto = SupplierDto.fromJson(json);

      expect(deserializedDto.supplierId, originalDto.supplierId);
      expect(deserializedDto.name, originalDto.name);
      expect(deserializedDto.contactPersonName, originalDto.contactPersonName);
      expect(deserializedDto.balanceDue, originalDto.balanceDue);
    });
  });
}
