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
  });
}
