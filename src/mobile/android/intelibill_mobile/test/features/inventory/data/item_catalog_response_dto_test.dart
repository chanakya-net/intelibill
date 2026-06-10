import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/item_catalog_response_dto.dart';

void main() {
  group('ItemCatalogResponseDto', () {
    test('parses paginated catalog response', () {
      final dto = ItemCatalogResponseDto.fromJson({
        'items': [
          {
            'id': 'item-1',
            'name': 'Rice Basmati',
            'barcode': '8901234567890',
            'uom': 'KG',
            'isActive': true,
            'currentStock': 100.0,
          },
        ],
        'totalCount': 1,
        'pageNumber': 1,
        'pageSize': 100,
      });

      expect(dto.items.length, 1);
      expect(dto.items.first.name, 'Rice Basmati');
      expect(dto.totalCount, 1);
      expect(dto.pageNumber, 1);
      expect(dto.pageSize, 100);
    });
  });
}
