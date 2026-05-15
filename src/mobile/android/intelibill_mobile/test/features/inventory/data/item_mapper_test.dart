import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/item_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/mappers/item_mapper.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/item.dart';

void main() {
  group('ItemMapper', () {
    test('maps all fields from dto to domain entity', () {
      const dto = ItemDto(
        id: 'item-1',
        name: 'Rice Basmati',
        barcode: '8901234567890',
        description: 'Premium basmati rice',
        uom: 'KG',
        isActive: true,
        currentStock: 50.5,
      );

      final item = ItemMapper.toDomain(dto);

      expect(item.itemId, 'item-1');
      expect(item.name, 'Rice Basmati');
      expect(item.barcode, '8901234567890');
      expect(item.description, 'Premium basmati rice');
      expect(item.uom, 'KG');
      expect(item.isActive, true);
      expect(item.currentStock, 50.5);
    });

    test('maps null description safely', () {
      const dto = ItemDto(
        id: 'item-2',
        name: 'Sugar',
        barcode: '8901234567891',
        uom: 'KG',
        isActive: false,
      );

      final item = ItemMapper.toDomain(dto);

      expect(item.description, isNull);
    });

    test('defaults currentStock to 0.0 when not provided in dto', () {
      const dto = ItemDto(
        id: 'item-3',
        name: 'Salt',
        barcode: '8901234567892',
        uom: 'KG',
        isActive: true,
      );

      final item = ItemMapper.toDomain(dto);

      expect(item.currentStock, 0.0);
    });

    test('returns correct Item type', () {
      const dto = ItemDto(
        id: 'item-4',
        name: 'Oil',
        barcode: '8901234567893',
        uom: 'L',
        isActive: true,
      );

      final result = ItemMapper.toDomain(dto);

      expect(result, isA<Item>());
    });

    test('maps id field to itemId in domain entity', () {
      const dto = ItemDto(
        id: 'server-generated-uuid',
        name: 'Wheat',
        barcode: '8901234567894',
        uom: 'KG',
        isActive: true,
      );

      final item = ItemMapper.toDomain(dto);

      expect(item.itemId, 'server-generated-uuid');
    });
  });
}
