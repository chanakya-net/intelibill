import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/suppliers/data/dto/supplier_dto.dart';
import 'package:intelibill_mobile/src/features/suppliers/data/mappers/supplier_mapper.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';

void main() {
  group('SupplierMapper', () {
    test('maps all fields from dto to domain entity', () {
      const dto = SupplierDto(
        supplierId: 'sup-1',
        name: 'ABC Traders',
        contactPersonName: 'John Doe',
        contactPersonPhone: '9876543210',
        address: '12 Main St',
        city: 'Mumbai',
        state: 'Maharashtra',
        pin: '400001',
        isSystem: false,
        isActive: true,
        isPreferred: true,
        balanceDue: 1500.50,
      );

      final supplier = SupplierMapper.toDomain(dto);

      expect(supplier.supplierId, 'sup-1');
      expect(supplier.name, 'ABC Traders');
      expect(supplier.contactPersonName, 'John Doe');
      expect(supplier.contactPersonPhone, '9876543210');
      expect(supplier.address, '12 Main St');
      expect(supplier.city, 'Mumbai');
      expect(supplier.state, 'Maharashtra');
      expect(supplier.pin, '400001');
      expect(supplier.isSystem, false);
      expect(supplier.isActive, true);
      expect(supplier.isPreferred, true);
      expect(supplier.balanceDue, 1500.50);
    });

    test('maps null contact fields safely', () {
      const dto = SupplierDto(
        supplierId: 'sup-2',
        name: 'XYZ Suppliers',
        isSystem: false,
        isActive: true,
        isPreferred: false,
      );

      final supplier = SupplierMapper.toDomain(dto);

      expect(supplier.contactPersonName, isNull);
      expect(supplier.contactPersonPhone, isNull);
      expect(supplier.address, isNull);
      expect(supplier.city, isNull);
      expect(supplier.state, isNull);
    });

    test('defaults balanceDue to 0.0 when not provided in dto', () {
      const dto = SupplierDto(
        supplierId: 'sup-3',
        name: 'DEF Corp',
        isSystem: false,
        isActive: true,
        isPreferred: false,
      );

      final supplier = SupplierMapper.toDomain(dto);

      expect(supplier.balanceDue, 0.0);
    });

    test('handles numeric balance safely with various precision', () {
      const dto = SupplierDto(
        supplierId: 'sup-4',
        name: 'GHI Ltd',
        isSystem: false,
        isActive: true,
        isPreferred: false,
        balanceDue: 999.99,
      );

      final supplier = SupplierMapper.toDomain(dto);

      expect(supplier.balanceDue, 999.99);
    });

    test('returns correct Supplier type', () {
      const dto = SupplierDto(
        supplierId: 'sup-5',
        name: 'JKL Industries',
        isSystem: false,
        isActive: true,
        isPreferred: false,
      );

      final result = SupplierMapper.toDomain(dto);

      expect(result, isA<Supplier>());
    });

    test('preserves system supplier flag', () {
      const systemDto = SupplierDto(
        supplierId: 'sup-sys-1',
        name: 'System Supplier',
        isSystem: true,
        isActive: true,
        isPreferred: false,
      );

      final systemSupplier = SupplierMapper.toDomain(systemDto);

      expect(systemSupplier.isSystem, true);
    });
  });
}
