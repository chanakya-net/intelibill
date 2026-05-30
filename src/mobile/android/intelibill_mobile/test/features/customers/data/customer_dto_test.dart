import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/customers/data/dto/customer_dto.dart';

void main() {
  group('CustomerDto', () {
    test('parses full JSON with all fields present', () {
      final json = {
        'customerId': 'cust-123',
        'name': 'Alice Sharma',
        'phoneNumber': '9876543210',
        'address': '12 Main Street, Mumbai',
        'isActive': true,
        'outstandingDue': 500.75,
      };

      final dto = CustomerDto.fromJson(json);

      expect(dto.customerId, 'cust-123');
      expect(dto.name, 'Alice Sharma');
      expect(dto.phoneNumber, '9876543210');
      expect(dto.address, '12 Main Street, Mumbai');
      expect(dto.isActive, true);
      expect(dto.outstandingDue, 500.75);
    });

    test('parses JSON with nullable address missing', () {
      final json = {
        'customerId': 'cust-456',
        'name': 'Bob Kumar',
        'phoneNumber': '9123456789',
        'isActive': false,
        'outstandingDue': 0.0,
      };

      final dto = CustomerDto.fromJson(json);

      expect(dto.address, isNull);
      expect(dto.isActive, false);
    });

    test('defaults outstandingDue to 0.0 when field is missing', () {
      final json = {
        'customerId': 'cust-789',
        'name': 'Carol Das',
        'phoneNumber': '9000000000',
        'isActive': true,
      };

      final dto = CustomerDto.fromJson(json);

      expect(dto.outstandingDue, 0.0);
    });

    test('supports value equality via Freezed', () {
      const dto1 = CustomerDto(
        customerId: 'cust-1',
        name: 'Alice',
        phoneNumber: '9876543210',
        isActive: true,
      );
      const dto2 = CustomerDto(
        customerId: 'cust-1',
        name: 'Alice',
        phoneNumber: '9876543210',
        isActive: true,
      );

      expect(dto1, equals(dto2));
    });
  });
}
