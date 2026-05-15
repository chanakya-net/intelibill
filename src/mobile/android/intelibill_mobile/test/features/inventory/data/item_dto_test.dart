import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/item_dto.dart';

void main() {
  group('ItemDto', () {
    test('parses full JSON with all fields present', () {
      final json = {
        'id': 'item-123',
        'name': 'Rice Basmati',
        'barcode': '8901234567890',
        'description': 'Premium basmati rice',
        'uom': 'KG',
        'isActive': true,
        'currentStock': 50.5,
      };

      final dto = ItemDto.fromJson(json);

      expect(dto.id, 'item-123');
      expect(dto.name, 'Rice Basmati');
      expect(dto.barcode, '8901234567890');
      expect(dto.description, 'Premium basmati rice');
      expect(dto.uom, 'KG');
      expect(dto.isActive, true);
      expect(dto.currentStock, 50.5);
    });

    test('parses JSON with nullable description missing', () {
      final json = {
        'id': 'item-456',
        'name': 'Sugar',
        'barcode': '8901234567891',
        'uom': 'KG',
        'isActive': true,
        'currentStock': 100.0,
      };

      final dto = ItemDto.fromJson(json);

      expect(dto.description, isNull);
    });

    test('defaults currentStock to 0.0 when field is missing', () {
      final json = {
        'id': 'item-789',
        'name': 'Salt',
        'barcode': '8901234567892',
        'uom': 'KG',
        'isActive': false,
      };

      final dto = ItemDto.fromJson(json);

      expect(dto.currentStock, 0.0);
    });

    test('supports value equality via Freezed', () {
      const dto1 = ItemDto(
        id: 'item-1',
        name: 'Rice',
        barcode: '123',
        uom: 'KG',
        isActive: true,
      );
      const dto2 = ItemDto(
        id: 'item-1',
        name: 'Rice',
        barcode: '123',
        uom: 'KG',
        isActive: true,
      );

      expect(dto1, equals(dto2));
    });

    test('serializes back to JSON correctly', () {
      const dto = ItemDto(
        id: 'item-1',
        name: 'Rice',
        barcode: '123',
        uom: 'KG',
        isActive: true,
        currentStock: 25,
      );

      final json = dto.toJson();

      expect(json['id'], 'item-1');
      expect(json['name'], 'Rice');
      expect(json['currentStock'], 25.0);
    });
  });
}
