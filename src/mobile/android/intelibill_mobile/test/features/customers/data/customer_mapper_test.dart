import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/customers/data/dto/customer_dto.dart';
import 'package:intelibill_mobile/src/features/customers/data/mappers/customer_mapper.dart';
import 'package:intelibill_mobile/src/features/customers/domain/entities/customer.dart';

void main() {
  group('CustomerMapper', () {
    test('maps all fields from dto to domain entity', () {
      const dto = CustomerDto(
        customerId: 'cust-1',
        name: 'Alice Sharma',
        phoneNumber: '9876543210',
        address: '12 Main St, Mumbai',
        isActive: true,
        outstandingDue: 250.50,
      );

      final customer = CustomerMapper.toDomain(dto);

      expect(customer.customerId, 'cust-1');
      expect(customer.name, 'Alice Sharma');
      expect(customer.phoneNumber, '9876543210');
      expect(customer.address, '12 Main St, Mumbai');
      expect(customer.isActive, true);
      expect(customer.outstandingDue, 250.50);
    });

    test('maps null address safely', () {
      const dto = CustomerDto(
        customerId: 'cust-2',
        name: 'Bob Kumar',
        phoneNumber: '9123456789',
        isActive: false,
      );

      final customer = CustomerMapper.toDomain(dto);

      expect(customer.address, isNull);
    });

    test('defaults outstandingDue to 0.0 when not provided in dto', () {
      const dto = CustomerDto(
        customerId: 'cust-3',
        name: 'Carol Das',
        phoneNumber: '9000000000',
        isActive: true,
      );

      final customer = CustomerMapper.toDomain(dto);

      expect(customer.outstandingDue, 0.0);
    });

    test('returns correct Customer type', () {
      const dto = CustomerDto(
        customerId: 'cust-4',
        name: 'Dave',
        phoneNumber: '9999999999',
        isActive: true,
      );

      final result = CustomerMapper.toDomain(dto);

      expect(result, isA<Customer>());
    });
  });
}
